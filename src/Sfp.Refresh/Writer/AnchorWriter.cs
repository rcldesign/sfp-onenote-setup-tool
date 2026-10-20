using System.Xml.Linq;
using Serilog;
using Sfp.Core.Manifests;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Writer;

/// <summary>One write to a page: the planned change, the slot's geometry (needed for a Create) and the owned object ID (needed for Update and Remove).</summary>
/// <param name="Change">The planned change.</param>
/// <param name="Geometry">Where a created outline goes and how it is styled; null is allowed for Update and Remove.</param>
/// <param name="ObjectId">The sidecar's object ID for the slot, or null when the slot has no object yet.</param>
public sealed record AnchorWrite(PlannedChange Change, SlotGeometry? Geometry, string? ObjectId);

/// <summary>What one page write did.</summary>
/// <param name="Created">Slot id to the object ID of each outline created, learned by re-reading the page.</param>
/// <param name="Removed">Slot ids whose outline was deleted, or was already gone from the page (either way the sidecar drops its ID).</param>
/// <param name="Updated">Slot ids whose outline text was replaced.</param>
/// <param name="Unresolved">Slot ids whose created outline could not be found again by position (the sidecar keeps no ID for them; the next run re-creates or reports them).</param>
public sealed record AnchorWriteResult(
    IReadOnlyDictionary<string, string> Created,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Unresolved)
{
    public int Count => Created.Count + Removed.Count + Updated.Count;

    public static AnchorWriteResult Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>Writes, updates or deletes tool-owned outlines on a page by object ID, through the page's existing XML plus the changed outlines only.</summary>
public interface IAnchorWriter
{
    /// <summary>
    /// Applies the writes to one page. Each Remove is one DeletePageContent call on the outline's object ID (OneNote's
    /// UpdatePageContent never deletes an object the XML leaves out); an outline already gone is reported removed without
    /// a call. The Creates and Updates then go in one UpdatePageContent call: the page's current XML (as read with
    /// piBasic, so the background image travels as a callback ID and its bytes are not resent) with each Update's one:T
    /// content replaced and each Create's outline appended. Nothing else on the page is touched. After a Create the page
    /// is re-read to learn the new outline's object ID. Throws <see cref="OneNoteException"/> when OneNote rejects a call.
    /// </summary>
    AnchorWriteResult Write(string pageId, PageXml current, IReadOnlyList<AnchorWrite> writes);
}

/// <summary>
/// Production <see cref="IAnchorWriter"/>. Created outlines are built with <see cref="PageXmlBuilder.AnchorOutline"/>
/// from the slot geometry; positions are placed relative to the sheet outline as it sits on the page (its actual origin
/// and placed scale), so a hidden-title or scaled notebook gets its anchors over the right spot without the sidecar
/// having to store the generate options. Multi-line text travels as &lt;br&gt; inside the one:T, which the parser reads
/// back as line breaks.
/// </summary>
public sealed class AnchorWriter : IAnchorWriter
{
    /// <summary>How far (points, each axis) a created outline may sit from where it was written and still be recognized on re-read.</summary>
    public const double MatchTolerancePt = 4.0;

    private readonly IOneNoteApp app;
    private readonly PageXmlOptions options;
    private readonly ILogger log;

    /// <param name="app">The run's COM session.</param>
    /// <param name="options">Page XML settings (font family, colors, the manifest OneNote block).</param>
    /// <param name="log">Run log.</param>
    public AnchorWriter(IOneNoteApp app, PageXmlOptions options, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        this.app = app;
        this.options = options;
        this.log = log;
    }

    /// <inheritdoc />
    public AnchorWriteResult Write(string pageId, PageXml current, IReadOnlyList<AnchorWrite> writes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(writes);
        List<AnchorWrite> active = writes.Where(w => !w.Change.Blocked).ToList();
        foreach (AnchorWrite write in active.Where(w => !Enum.IsDefined(w.Change.Kind)))
        {
            throw new ArgumentOutOfRangeException(nameof(writes), write.Change.Kind, "Unknown change kind.");
        }

        if (active.Count == 0)
        {
            return AnchorWriteResult.Empty;
        }

        // Removes first, one DeletePageContent each, so the UpdatePageContent below is built from the page as it is
        // after them and can never bring a deleted outline back. Each call after the first works on a fresh read.
        PageXml latest = current;
        bool written = false;
        List<string> removed = new();
        foreach (AnchorWrite write in active.Where(w => w.Change.Kind == ChangeKind.Remove))
        {
            string objectId = RequireObjectId(write);
            if (written)
            {
                latest = Read(pageId);
            }

            if (!latest.Outlines.Any(o => string.Equals(o.ObjectId, objectId, StringComparison.Ordinal)))
            {
                log.Information("Page {PageId}: the outline {ObjectId} of {Slot} is already gone", pageId, objectId, write.Change.SlotId);
                removed.Add(write.Change.SlotId);
                continue;
            }

            try
            {
                app.DeletePageContent(pageId, objectId, ExpectedLastModified(latest));
            }
            catch (OneNoteException ex) when (ex is not OneNoteUnavailableException && IsGone(ex.HResult))
            {
                log.Information("Page {PageId}: the outline {ObjectId} of {Slot} was gone when it was deleted ({HResult})", pageId, objectId, write.Change.SlotId, ex.HResultHex);
            }

            removed.Add(write.Change.SlotId);
            written = true;
        }

        List<AnchorWrite> edits = active.Where(w => w.Change.Kind != ChangeKind.Remove).ToList();
        if (edits.Count == 0)
        {
            log.Information("Page {PageId}: {Removed} removed", pageId, removed.Count);
            return new AnchorWriteResult(new Dictionary<string, string>(StringComparer.Ordinal), removed, Array.Empty<string>(), Array.Empty<string>());
        }

        if (written)
        {
            latest = Read(pageId);
        }

        XDocument document = new(latest.Document);
        XElement root = document.Root ?? throw new InvalidOperationException("The page XML has no root element.");
        XNamespace one = root.Name.Namespace;
        PageXmlOptions placed = PlacedOptions(latest);

        List<string> updated = new();
        List<(string SlotId, RectPt Expected)> created = new();
        HashSet<string> knownIds = latest.Outlines.Select(o => o.ObjectId).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (AnchorWrite write in edits)
        {
            PlannedChange change = write.Change;
            if (change.Kind == ChangeKind.Update)
            {
                XElement outline = FindOutline(root, one, write.ObjectId, change);
                SetText(outline, one, Html(change.SlotId, change.NewText, write.Geometry, placed));
                updated.Add(change.SlotId);
                continue;
            }

            SlotGeometry geometry = write.Geometry ?? throw new ArgumentException($"Create for {change.SlotId} on {change.Page} needs slot geometry.", nameof(writes));
            XElement added = PageXmlBuilder.AnchorOutline(Anchor(change.SlotId, change.NewText, geometry), placed);
            SetText(added, one, Html(change.SlotId, change.NewText, geometry, placed));
            root.Add(added);
            RectPt expected = PageXmlParser.Bounds(added, one) ?? throw new InvalidOperationException("The anchor outline builder produced no position.");
            created.Add((change.SlotId, expected));
        }

        app.UpdatePageContent(OneNoteXml.Serialize(root), ExpectedLastModified(latest));
        log.Information("Page {PageId}: {Created} created, {Updated} updated, {Removed} removed", pageId, created.Count, updated.Count, removed.Count);

        Dictionary<string, string> createdIds = new(StringComparer.Ordinal);
        List<string> unresolved = new();
        if (created.Count > 0)
        {
            PageXml reread = Read(pageId);
            List<OutlineInfo> candidates = reread.Outlines.Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0 && !knownIds.Contains(o.ObjectId)).ToList();
            foreach ((string slotId, RectPt expected) in created)
            {
                OutlineInfo? match = Nearest(candidates, expected);
                if (match is null)
                {
                    unresolved.Add(slotId);
                    log.Warning("Page {PageId}: the outline created for {Slot} was not found again at ({X}, {Y})", pageId, slotId, expected.X, expected.Y);
                    continue;
                }

                candidates.Remove(match);
                createdIds[slotId] = match.ObjectId;
            }
        }

        return new AnchorWriteResult(createdIds, removed, updated, unresolved);
    }

    /// <summary>
    /// The dateExpectedLastModified to pass with a write to the page as read; <see cref="DateTime.MinValue"/> skips OneNote's check.
    /// </summary>
    public DateTime ExpectedLastModified(PageXml page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return DateTime.MinValue;
    }

    /// <summary>True for the results OneNote gives when the object to delete is no longer on the page.</summary>
    private static bool IsGone(int hresult) => hresult is OneNoteHResults.PageObjectDoesNotExist or OneNoteHResults.ObjectDoesNotExist;

    private static string RequireObjectId(AnchorWrite write)
    {
        PlannedChange change = write.Change;
        return string.IsNullOrEmpty(write.ObjectId)
            ? throw new ArgumentException($"{change.Kind} for {change.SlotId} on {change.Page} needs the slot's object ID.", nameof(write))
            : write.ObjectId;
    }

    private PageXml Read(string pageId) => PageXmlParser.Parse(app.GetPageContent(pageId, PageInfo.Basic));

    /// <summary>
    /// The options with the title offset and page scale taken from the sheet outline as it sits on the page, so anchors
    /// are placed relative to the sheet image's actual top-left corner and size.
    /// </summary>
    public PageXmlOptions PlacedOptions(PageXml current)
    {
        ArgumentNullException.ThrowIfNull(current);
        OutlineInfo? sheet = current.SheetOutline;
        if (sheet is null)
        {
            return options;
        }

        double scale = sheet.Width > 0 && options.SheetWidthPt > 0 ? sheet.Width / options.SheetWidthPt : options.PageScale;
        return options with
        {
            HideTitle = false,
            OneNote = options.OneNote with { TitleOffsetPt = sheet.Y },
            PageScale = scale,
        };
    }

    /// <summary>The styled HTML for a slot's text, line breaks as &lt;br&gt;.</summary>
    public static string Html(string slotId, string text, SlotGeometry? geometry, PageXmlOptions placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        SlotGeometry style = geometry ?? new SlotGeometry(new RectPt(0, 0, 0, 0), Core.TextAlign.Left, 10, 1.0);
        return PageXmlBuilder.AnchorHtml(Anchor(slotId, text, style), placed).Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static ResolvedAnchor Anchor(string slotId, string text, SlotGeometry geometry) =>
        new(geometry.ToPlacement(slotId, text), "", true);

    private static XElement FindOutline(XElement root, XNamespace one, string? objectId, PlannedChange change)
    {
        if (string.IsNullOrEmpty(objectId))
        {
            throw new ArgumentException($"{change.Kind} for {change.SlotId} on {change.Page} needs the slot's object ID.", nameof(change));
        }

        return root.Elements(one + "Outline").FirstOrDefault(o => string.Equals((string?)o.Attribute("objectID"), objectId, StringComparison.Ordinal))
            ?? throw new OneNoteException("UpdatePageContent", OneNoteHResults.PageObjectDoesNotExist, $"Outline {objectId} for {change.SlotId} is no longer on page {change.Page}.", null);
    }

    /// <summary>Replaces the content of the outline's first one:T (or builds the paragraph when the outline has none); nothing else in the outline changes.</summary>
    private static void SetText(XElement outline, XNamespace one, string html)
    {
        XElement? t = outline.Descendants(one + "T").FirstOrDefault();
        if (t is not null)
        {
            t.ReplaceNodes(new XCData(html));
            return;
        }

        XElement? children = outline.Element(one + "OEChildren");
        if (children is null)
        {
            children = new XElement(one + "OEChildren");
            outline.Add(children);
        }

        XElement? oe = children.Element(one + "OE");
        if (oe is null)
        {
            oe = new XElement(one + "OE");
            children.Add(oe);
        }

        oe.Add(new XElement(one + "T", new XCData(html)));
    }

    private static OutlineInfo? Nearest(List<OutlineInfo> candidates, RectPt expected)
    {
        OutlineInfo? best = null;
        double bestDistance = double.MaxValue;
        foreach (OutlineInfo outline in candidates)
        {
            double distance = Math.Max(Math.Abs(outline.X - expected.X), Math.Abs(outline.Y - expected.Y));
            if (distance <= MatchTolerancePt && distance < bestDistance)
            {
                best = outline;
                bestDistance = distance;
            }
        }

        return best;
    }
}
