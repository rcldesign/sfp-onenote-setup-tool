using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Serilog;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Writer;

/// <summary>What the Next quarter step did to the previous notebook.</summary>
public enum NextQuarterLinkOutcome
{
    /// <summary>Nothing was written; <see cref="NextQuarterLinkResult.Warning"/> says why.</summary>
    NotWritten,

    /// <summary>A link outline was added to the previous notebook's quarterly review.</summary>
    Added,

    /// <summary>The link outline an earlier run added was given the new text and target in place.</summary>
    Updated,

    /// <summary>The link outline an earlier run added already says the same and points to the same page; nothing was sent.</summary>
    Unchanged,
}

/// <summary>Outcome of linking a previous notebook's quarterly review to the notebook just generated.</summary>
public sealed record NextQuarterLinkResult
{
    public required NextQuarterLinkOutcome Outcome { get; init; }

    /// <summary>The previous notebook's name from its state file, or the path given when the file could not be read.</summary>
    public required string PreviousNotebook { get; init; }

    /// <summary>Object ID of the link outline on the previous notebook's quarterly review, when it is known.</summary>
    public string? ObjectId { get; init; }

    /// <summary>A line for the run's warnings: why nothing was written, or what went wrong after the link was written; null when all went well.</summary>
    public string? Warning { get; init; }

    /// <summary>True when the previous notebook's quarterly review carries the link after the run.</summary>
    public bool Written => Outcome is NextQuarterLinkOutcome.Added or NextQuarterLinkOutcome.Updated or NextQuarterLinkOutcome.Unchanged;

    /// <summary>One line for the run summary, or null when nothing was written.</summary>
    public string? Summary => Outcome switch
    {
        NextQuarterLinkOutcome.Added => "Next quarter link added to the quarterly review of " + PreviousNotebook + ".",
        NextQuarterLinkOutcome.Updated => "Next quarter link on the quarterly review of " + PreviousNotebook + " now points to this notebook.",
        NextQuarterLinkOutcome.Unchanged => "Next quarter link on the quarterly review of " + PreviousNotebook + " already points to this notebook.",
        _ => null,
    };
}

/// <summary>Fills the "Next quarter" link slot on the quarterly review of a previous notebook (Generate with --previous).</summary>
public interface INextQuarterLinkWriter
{
    /// <summary>
    /// Points the previous notebook's Next quarter link at <paramref name="coverHref"/>. Never throws for an expected
    /// failure (no state file, notebook not reachable, page gone, OneNote refusing the write): the result carries a warning.
    /// </summary>
    /// <param name="previousNotebook">The previous notebook's folder, or the path of its state file.</param>
    /// <param name="next">The notebook just generated.</param>
    /// <param name="coverHref">onenote: URL of the new notebook's cover page.</param>
    NextQuarterLinkResult Link(string previousNotebook, NotebookHandle next, string coverHref);
}

/// <summary>
/// The production <see cref="INextQuarterLinkWriter"/>. Reads the previous notebook's state file, finds its quarterly
/// review page ("review:quarter"), reads the page without binary data, and inserts one anchor outline at the manifest's
/// links.nextQuarter geometry (text from its textFormat, the link to the new cover) through UpdatePageContent with that
/// outline alone inside the page element: UpdatePageContent never removes what the XML leaves out, so nothing else on
/// the page changes, not even an edit made after the page was read. The page is read again to learn the outline's object ID,
/// which is recorded under anchors["nextQuarter"] in the previous state file. A later run finds that outline by its ID
/// (or, when an earlier run wrote it but could not record it, by its exact position and text) and updates its text and
/// target in place instead of adding a second one. Anything else overlapping the slot (the user's text or ink) is left
/// alone and the link is not written. When the notebook is not open in OneNote, or the page ID no longer resolves, the
/// notebook is opened from its folder once (or, when that folder is no longer on disk, from the location its state file
/// records) and the page is tried again. Anchors are placed relative to the sheet image as it sits on the page, so a
/// hidden-title or scaled notebook is linked correctly.
/// </summary>
public sealed class NextQuarterLinkWriter : INextQuarterLinkWriter
{
    /// <summary>The anchor id of the slot, as the renderer reserves it and the state file records it.</summary>
    public const string AnchorId = "nextQuarter";

    /// <summary>How far (points, each axis) the written outline may sit from where it was placed and still be recognized on re-read.</summary>
    public const double MatchTolerancePt = 4.0;

    private readonly IOneNoteApp _app;
    private readonly ISidecarStore _sidecars;
    private readonly Manifest _manifest;
    private readonly PageXmlOptions _options;
    private readonly ILogger _log;

    /// <param name="app">The run's COM session.</param>
    /// <param name="sidecars">State file store.</param>
    /// <param name="manifest">The manifest (links.nextQuarter geometry and text).</param>
    /// <param name="options">Page XML settings of the run (font family, ink color, the manifest OneNote block).</param>
    /// <param name="log">Run log.</param>
    public NextQuarterLinkWriter(IOneNoteApp app, ISidecarStore sidecars, Manifest manifest, PageXmlOptions options, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _app = app;
        _sidecars = sidecars;
        _manifest = manifest;
        _options = options;
        _log = log;
    }

    /// <summary>The warning line for a previous notebook that did not get its link.</summary>
    public static string WarningFor(string previousNotebook, string reason) =>
        string.Format(CultureInfo.InvariantCulture, "The Next quarter link was not written to the previous notebook {0}: {1}.", previousNotebook, reason.TrimEnd('.'));

    /// <inheritdoc />
    public NextQuarterLinkResult Link(string previousNotebook, NotebookHandle next, string coverHref)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousNotebook);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(coverHref);
        string given = previousNotebook.Trim();
        try
        {
            return LinkCore(given, next, coverHref);
        }
        catch (Exception ex) when (ex is OneNoteException or IOException or UnauthorizedAccessException or JsonException or XmlException or FormatException)
        {
            _log.Warning(ex, "The Next quarter link could not be written to {Previous}", given);
            return NotWritten(given, given, ex.Message);
        }
    }

    private NextQuarterLinkResult LinkCore(string given, NotebookHandle next, string coverHref)
    {
        (string? folder, string sidecarPath) = Locate(given);
        Sidecar? previous = _sidecars.Load(sidecarPath);
        if (previous is null)
        {
            string reason = folder is not null && !Directory.Exists(folder)
                ? "no notebook folder was found there and no state file at " + sidecarPath
                : "its state file " + sidecarPath + " was not found";
            return NotWritten(given, given, reason);
        }

        string name = previous.NotebookName;
        if (string.Equals(previous.NotebookId, next.Id, StringComparison.Ordinal))
        {
            return NotWritten(given, name, "it is the notebook this run generated");
        }

        if (!previous.Pages.TryGetValue(PageKey.QuarterlyReview.Value, out SidecarPage? review))
        {
            return NotWritten(given, name, "its state file lists no quarterly review page");
        }

        string openPath = OpenPath(folder, previous.NotebookPath);
        (PageXml? page, string? unreadable) = ReadReview(previous, review, openPath);
        if (page is null)
        {
            return NotWritten(given, name, unreadable ?? "its quarterly review page could not be read");
        }

        if (page.SfpKey is string key && !string.Equals(key, PageKey.QuarterlyReview.Value, StringComparison.Ordinal))
        {
            return NotWritten(given, name, "the page its state file records as the quarterly review now holds '" + key + "'");
        }

        FixedAnchor slot = _manifest.Links.NextQuarter;
        PageXmlOptions placed = Placed(page);
        ResolvedAnchor anchor = new(
            new AnchorPlacement(AnchorId, (float)slot.X, (float)slot.Y, (float)slot.W, (float)slot.H, LinkGraphBuilder.LinkText(slot, next.Name), slot.Align, (float)slot.SizePt) { Kind = AnchorKind.Link },
            coverHref,
            ToolOwned: true);
        XDocument document = new(page.Document);
        XElement root = document.Root ?? throw new FormatException("The quarterly review page XML has no root element.");
        XNamespace one = root.Name.Namespace;
        string html = PageXmlBuilder.AnchorHtml(anchor, placed);
        XElement outline = PageXmlBuilder.AnchorOutline(anchor, placed);
        RectPt expected = PageXmlParser.Bounds(outline, one) ?? throw new FormatException("The anchor outline builder produced no position.");

        // The link an earlier run recorded, else one an earlier run wrote but could not record (exactly where the tool
        // writes it, with exactly its text): either is updated in place rather than joined by a second one.
        string? recordedId = review.Anchors.TryGetValue(AnchorId, out string? recorded) && page.Outlines.Any(o => !o.IsBackgroundImage && o.ObjectId == recorded) ? recorded : null;
        string? existingId = recordedId ?? Adoptable(page, expected, anchor.Placement.Text, review.Anchors.Values)?.ObjectId;
        XElement? existing = existingId is null
            ? null
            : root.Elements(one + "Outline").FirstOrDefault(o => string.Equals((string?)o.Attribute("objectID"), existingId, StringComparison.Ordinal));
        if (existing is not null)
        {
            NextQuarterLinkResult updated = UpdateOwned(review.PageId, one, existing, html, name, existingId!);
            return recordedId is not null ? updated : Record(updated, previous, review, sidecarPath, name, existingId!);
        }

        string? blocker = Blocker(page, expected, review.Anchors.Values);
        if (blocker is not null)
        {
            return NotWritten(given, name, "something that is not the tool's sits where the link goes (" + blocker + ")");
        }

        HashSet<string> before = page.Outlines.Select(o => o.ObjectId).Where(objectId => objectId.Length > 0).ToHashSet(StringComparer.Ordinal);
        _app.UpdatePageContent(OneNoteXml.Serialize(LinkPage(review.PageId, outline)), DateTime.MinValue);
        _log.Information("Next quarter link added to the quarterly review of {Name} ({PageId})", name, review.PageId);

        PageXml reread = PageXmlParser.Parse(_app.GetPageContent(review.PageId, PageInfo.Basic));
        OutlineInfo? created = FindCreated(reread, before, expected, anchor.Placement.Text);
        if (created is null)
        {
            _log.Warning("The Next quarter link written to {Name} was not found again at ({X}, {Y})", name, expected.X, expected.Y);
            return new NextQuarterLinkResult
            {
                Outcome = NextQuarterLinkOutcome.Added,
                PreviousNotebook = name,
                Warning = "The Next quarter link was added to the quarterly review of " + name + " but could not be found again, so its state file does not record it.",
            };
        }

        NextQuarterLinkResult added = new() { Outcome = NextQuarterLinkOutcome.Added, PreviousNotebook = name, ObjectId = created.ObjectId };
        return Record(added, previous, review, sidecarPath, name, created.ObjectId);
    }

    /// <summary>Records the link's object ID under anchors["nextQuarter"] of the quarterly review in the previous state file; a save failure becomes the result's warning.</summary>
    private NextQuarterLinkResult Record(NextQuarterLinkResult result, Sidecar previous, SidecarPage review, string sidecarPath, string name, string objectId)
    {
        Dictionary<string, string> anchors = new(review.Anchors, StringComparer.Ordinal) { [AnchorId] = objectId };
        Dictionary<string, SidecarPage> pages = new(previous.Pages, StringComparer.Ordinal) { [PageKey.QuarterlyReview.Value] = review with { Anchors = anchors } };
        try
        {
            _sidecars.Save(sidecarPath, previous with { Pages = pages });
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "The state file {Path} could not be updated with the Next quarter link", sidecarPath);
            return result with
            {
                Warning = "The Next quarter link is on the quarterly review of " + name + " but its state file " + sidecarPath + " could not be updated: " + ex.Message,
            };
        }
    }

    /// <summary>
    /// An outline the tool wrote for the link but did not record: not owned by any recorded anchor, within the match
    /// tolerance of where the tool writes the link, and holding exactly its text. Null when there is not exactly one.
    /// </summary>
    private static OutlineInfo? Adoptable(PageXml page, RectPt expected, string text, IEnumerable<string> ownedIds)
    {
        HashSet<string> owned = ownedIds.ToHashSet(StringComparer.Ordinal);
        List<OutlineInfo> matches = page.Outlines
            .Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0 && !owned.Contains(o.ObjectId))
            .Where(o => Math.Max(Math.Abs(o.X - expected.X), Math.Abs(o.Y - expected.Y)) <= MatchTolerancePt)
            .Where(o => string.Equals(o.PlainText, text.Trim(), StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// The XML UpdatePageContent takes to add or update the link alone: the page, by its ID, holding that one outline.
    /// UpdatePageContent never removes an object the XML leaves out, so the rest of the page stays as OneNote has it,
    /// including anything the user changed after the page was read.
    /// </summary>
    private static XElement LinkPage(string pageId, XElement outline)
    {
        XNamespace one = outline.Name.Namespace;
        return new XElement(one + "Page", new XAttribute(XNamespace.Xmlns + OneNoteXml.Prefix, one.NamespaceName), new XAttribute("ID", pageId), outline);
    }

    /// <summary>
    /// The link an earlier run added: its text and target are replaced in place (it stays where it is, even if the user
    /// moved it). Only that outline is sent, under its object ID.
    /// </summary>
    private NextQuarterLinkResult UpdateOwned(string pageId, XNamespace one, XElement owned, string html, string name, string ownedId)
    {
        XElement? t = owned.Descendants(one + "T").FirstOrDefault();
        if (t is not null && string.Equals(t.Value, html, StringComparison.Ordinal))
        {
            return new NextQuarterLinkResult { Outcome = NextQuarterLinkOutcome.Unchanged, PreviousNotebook = name, ObjectId = ownedId };
        }

        if (t is not null)
        {
            t.ReplaceNodes(new XCData(html));
        }
        else
        {
            XElement children = owned.Element(one + "OEChildren") ?? new XElement(one + "OEChildren");
            if (children.Parent is null)
            {
                owned.Add(children);
            }

            XElement oe = children.Element(one + "OE") ?? new XElement(one + "OE");
            if (oe.Parent is null)
            {
                children.Add(oe);
            }

            oe.Add(new XElement(one + "T", new XCData(html)));
        }

        _app.UpdatePageContent(OneNoteXml.Serialize(LinkPage(pageId, owned)), DateTime.MinValue);
        _log.Information("Next quarter link on the quarterly review of {Name} updated in place ({ObjectId})", name, ownedId);
        return new NextQuarterLinkResult { Outcome = NextQuarterLinkOutcome.Updated, PreviousNotebook = name, ObjectId = ownedId };
    }

    /// <summary>
    /// The folder to open and the state file to read. A path ending in ".sfp-state.json" is the state file beside the
    /// folder of the same name; another existing file is taken as a state file whose own NotebookPath names the folder
    /// (folder null); anything else is the notebook folder.
    /// </summary>
    private (string? Folder, string SidecarPath) Locate(string given)
    {
        string trimmed = given.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string suffix = "." + Sidecar.FileName;
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return (trimmed[..^suffix.Length], trimmed);
        }

        return File.Exists(trimmed) ? (null, trimmed) : (trimmed, _sidecars.PathFor(trimmed));
    }

    /// <summary>
    /// Where to open the notebook from: the folder given, unless it is not on disk (a notebook moved to OneDrive leaves
    /// its state file behind) and the state file records another location (the notebook's URL, once a Refresh has
    /// recovered it), which is then the only place it can be opened from.
    /// </summary>
    private static string OpenPath(string? folder, string recorded)
    {
        if (folder is not null && (Directory.Exists(folder) || string.IsNullOrWhiteSpace(recorded)))
        {
            return folder;
        }

        return string.IsNullOrWhiteSpace(recorded) ? "" : recorded;
    }

    /// <summary>
    /// Reads the quarterly review page. When the notebook is not open in OneNote, or the page ID does not resolve, the
    /// notebook is opened from <paramref name="openPath"/> once and the page is read again.
    /// </summary>
    private (PageXml? Page, string? Reason) ReadReview(Sidecar previous, SidecarPage review, string openPath)
    {
        if (IsOpen(previous.NotebookId))
        {
            PageXml? page = TryRead(review.PageId);
            if (page is not null)
            {
                return (page, null);
            }

            _log.Information("The quarterly review page {PageId} of {Name} did not resolve; opening the notebook from {Path}", review.PageId, previous.NotebookName, openPath);
        }
        else
        {
            _log.Information("{Name} is not open in OneNote; opening it from {Path}", previous.NotebookName, openPath);
        }

        if (string.IsNullOrWhiteSpace(openPath))
        {
            return (null, "it is not open in OneNote and its state file names no folder to open it from");
        }

        try
        {
            string id = _app.OpenHierarchy(openPath, null, CreateFileType.None);
            _log.Information("Opened {Path} as {Id}", openPath, id);
        }
        catch (OneNoteException ex) when (ex is not OneNoteUnavailableException)
        {
            _log.Warning(ex, "{Path} could not be opened", openPath);
            return (null, "its quarterly review page is not reachable and the notebook could not be opened from " + openPath + " (" + ex.Message.TrimEnd('.') + ")");
        }

        PageXml? reopened = TryRead(review.PageId);
        return reopened is not null
            ? (reopened, null)
            : (null, "its quarterly review page could not be found in OneNote, even after opening the notebook from " + openPath);
    }

    /// <summary>True when OneNote lists the notebook ID among its open notebooks.</summary>
    private bool IsOpen(string notebookId)
    {
        XElement? root = XDocument.Parse(_app.GetHierarchy(null, HierarchyScope.Notebooks)).Root;
        return root is not null && root.Elements()
            .Any(e => e.Name.LocalName == "Notebook" && string.Equals((string?)e.Attribute("ID"), notebookId, StringComparison.Ordinal));
    }

    private PageXml? TryRead(string pageId)
    {
        try
        {
            return PageXmlParser.Parse(_app.GetPageContent(pageId, PageInfo.Basic));
        }
        catch (OneNoteException ex) when (ex is not OneNoteUnavailableException)
        {
            _log.Information("Page {PageId} could not be read: {Message}", pageId, ex.Message);
            return null;
        }
    }

    /// <summary>The run's options with the title offset and scale taken from the sheet image as it sits on the page.</summary>
    private PageXmlOptions Placed(PageXml page)
    {
        OutlineInfo? sheet = page.SheetOutline;
        if (sheet is null)
        {
            return _options;
        }

        double scale = sheet.Width > 0 && _options.SheetWidthPt > 0 ? sheet.Width / _options.SheetWidthPt : _options.PageScale;
        return _options with
        {
            HideTitle = false,
            OneNote = _options.OneNote with { TitleOffsetPt = sheet.Y },
            PageScale = scale,
        };
    }

    /// <summary>Something not owned by the tool (an outline or page-level ink) that overlaps the slot, described for the warning, or null.</summary>
    private static string? Blocker(PageXml page, RectPt slot, IEnumerable<string> ownedIds)
    {
        HashSet<string> owned = ownedIds.ToHashSet(StringComparer.Ordinal);
        foreach (OutlineInfo outline in page.Outlines)
        {
            if (outline.IsBackgroundImage || owned.Contains(outline.ObjectId) || !Overlaps(outline.Rect, slot))
            {
                continue;
            }

            string text = outline.PlainText.Length > 0 ? outline.PlainText : outline.InkText;
            if (text.Length == 0)
            {
                return outline.InkWords.Count > 0 ? "ink" : "an empty text box";
            }

            const int max = 40;
            return "text '" + (text.Length > max ? text[..max].TrimEnd() + "..." : text).Replace('\n', ' ') + "'";
        }

        return page.InkDrawings().Any(d => d.OutlineObjectId is null && d.Bounds is RectPt bounds && Overlaps(bounds, slot)) ? "ink" : null;
    }

    /// <summary>True when an object overlaps the slot; an object without a reported size counts when its position lies in the slot (with the match tolerance).</summary>
    private static bool Overlaps(RectPt objectRect, RectPt slot)
    {
        if (objectRect.W > 0 && objectRect.H > 0)
        {
            return objectRect.Overlaps(slot);
        }

        RectPt tolerant = new(slot.X - MatchTolerancePt, slot.Y - MatchTolerancePt, slot.W + (2 * MatchTolerancePt), slot.H + (2 * MatchTolerancePt));
        return tolerant.Contains(objectRect.X, objectRect.Y);
    }

    /// <summary>The outline the write created: a new object ID nearest to where it was placed, else the one new outline with its text.</summary>
    private static OutlineInfo? FindCreated(PageXml reread, HashSet<string> before, RectPt expected, string text)
    {
        List<OutlineInfo> candidates = reread.Outlines.Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0 && !before.Contains(o.ObjectId)).ToList();
        OutlineInfo? nearest = candidates
            .Select(o => (Outline: o, Distance: Math.Max(Math.Abs(o.X - expected.X), Math.Abs(o.Y - expected.Y))))
            .Where(c => c.Distance <= MatchTolerancePt)
            .OrderBy(c => c.Distance)
            .Select(c => c.Outline)
            .FirstOrDefault();
        if (nearest is not null)
        {
            return nearest;
        }

        List<OutlineInfo> sameText = candidates.Where(o => string.Equals(o.PlainText, text.Trim(), StringComparison.Ordinal)).ToList();
        return sameText.Count == 1 ? sameText[0] : null;
    }

    private NextQuarterLinkResult NotWritten(string given, string name, string reason)
    {
        _log.Warning("Next quarter link not written to {Previous}: {Reason}", given, reason);
        return new NextQuarterLinkResult { Outcome = NextQuarterLinkOutcome.NotWritten, PreviousNotebook = name, Warning = WarningFor(given, reason) };
    }
}
