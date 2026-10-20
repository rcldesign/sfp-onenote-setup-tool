using System.Globalization;
using Serilog;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;

namespace Sfp.Refresh.Orchestration;

/// <summary>What the recovery produced.</summary>
/// <param name="Sidecar">The sidecar to use for the run (the one given, or the rebuilt one).</param>
/// <param name="Rebuilt">True when the sidecar was missing or stale and was rebuilt from the notebook.</param>
/// <param name="Pages">The pages of the notebook as read during recovery, keyed by page key, so the run can reuse them.</param>
/// <param name="Warnings">The one warning the PRD asks for when a rebuild happened, else empty.</param>
public sealed record RecoveryResult(Sidecar Sidecar, bool Rebuilt, IReadOnlyDictionary<string, PageXml> Pages, IReadOnlyList<string> Warnings);

/// <summary>
/// The PRD recovery path: when the sidecar is missing, or any page or section ID in it no longer resolves in the
/// notebook's hierarchy, the whole map is rebuilt by walking GetHierarchy(notebook, pages) and reading every page's
/// sfp.key meta. Tool-owned anchors are re-found by matching the outlines on each page to the known slot and link
/// rectangles of its page type (position within a tolerance, never a loose overlap, so a user object is never adopted).
/// One warning is reported for the summary.
/// </summary>
public sealed class SidecarRecovery
{
    /// <summary>How far (points, each axis) an outline may sit from where the tool would have written it and still be adopted.</summary>
    public const double MatchTolerancePt = 4.0;

    /// <summary>The warning line reported once when a rebuild happened.</summary>
    public const string RebuiltWarning = "The sidecar was missing or stale and was rebuilt from the notebook's page keys; tool-owned objects were re-found by position.";

    private readonly IOneNoteApp app;
    private readonly Manifest manifest;
    private readonly IDateEngine dates;
    private readonly IPlanBuilder plans;
    private readonly ILogger log;

    public SidecarRecovery(IOneNoteApp app, Manifest manifest, IDateEngine dates, IPlanBuilder plans, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(log);
        this.app = app;
        this.manifest = manifest;
        this.dates = dates;
        this.plans = plans;
        this.log = log;
    }

    /// <summary>
    /// Returns the sidecar as given when every ID in it resolves, else rebuilds it. <paramref name="toolVersion"/> is
    /// recorded when there was no previous sidecar to copy it from.
    /// </summary>
    public RecoveryResult Recover(NotebookHandle notebook, Sidecar? sidecar, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(toolVersion);
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(app.GetHierarchy(notebook.Id, HierarchyScope.Pages));
        if (sidecar is not null && IsConsistent(sidecar, tree))
        {
            return new RecoveryResult(sidecar, false, new Dictionary<string, PageXml>(StringComparer.Ordinal), Array.Empty<string>());
        }

        log.Warning(sidecar is null ? "No sidecar for {Name}; rebuilding it from the notebook" : "Sidecar for {Name} holds IDs that no longer resolve; rebuilding it from the notebook", notebook.Name);
        (Sidecar rebuilt, IReadOnlyDictionary<string, PageXml> pages) = Rebuild(notebook, tree, sidecar, toolVersion);
        return new RecoveryResult(rebuilt, true, pages, new[] { RebuiltWarning });
    }

    /// <summary>True when the notebook ID, every section ID and every page ID the sidecar records are present in the hierarchy.</summary>
    public static bool IsConsistent(Sidecar sidecar, HierarchyNotebook tree)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(tree);
        if (!string.Equals(sidecar.NotebookId, tree.Id, StringComparison.Ordinal))
        {
            return false;
        }

        HashSet<string> sectionIds = tree.Sections.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> pageIds = tree.Pages.Select(p => p.Page.Id).ToHashSet(StringComparer.Ordinal);
        return sidecar.Sections.Values.All(sectionIds.Contains)
            && sidecar.Pages.Values.All(p => pageIds.Contains(p.PageId) && sectionIds.Contains(p.SectionId));
    }

    /// <summary>Walks every page of the hierarchy, reads its sfp.key, and rebuilds the page, section and anchor maps.</summary>
    public (Sidecar Sidecar, IReadOnlyDictionary<string, PageXml> Pages) Rebuild(NotebookHandle notebook, HierarchyNotebook tree, Sidecar? previous, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(toolVersion);

        Dictionary<string, PageXml> pages = new(StringComparer.Ordinal);
        Dictionary<string, (HierarchySection Section, HierarchyPage Page)> located = new(StringComparer.Ordinal);
        foreach ((HierarchySection section, HierarchyPage page) in tree.Pages)
        {
            PageXml? xml = TryRead(page);
            string? key = xml?.SfpKey;
            if (xml is null || string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!located.TryAdd(key, (section, page)))
            {
                log.Warning("Page key {Key} appears on more than one page ({First} and {Second}); the first is kept", key, located[key].Page.Id, page.Id);
                continue;
            }

            pages[key] = xml;
        }

        QuarterSpec quarterSpec = previous?.Quarter ?? QuarterFromKeys(pages.Keys)
            ?? throw new InvalidOperationException($"Notebook '{notebook.Name}' holds no page with an sfp.key meta; it was not generated by this tool.");
        Quarter quarter = dates.Build(quarterSpec);
        NotebookPlan plan = plans.Build(manifest, quarter, new GenerateOptions { Start = quarterSpec });
        SlotCatalog catalog = new(manifest, dates, quarter, WindowFromSetup(pages));

        Dictionary<string, string> sections = new(StringComparer.Ordinal);
        foreach (HierarchySection section in tree.Sections)
        {
            sections.TryAdd(section.Name, section.Id);
        }

        Dictionary<string, SidecarPage> records = new(StringComparer.Ordinal);
        foreach ((string key, (HierarchySection section, HierarchyPage page)) in located)
        {
            PageSpec? spec = plan.FindPage(new PageKey(key));
            IReadOnlyDictionary<string, string> anchors = spec is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : MatchAnchors(spec, pages[key], catalog, plan, manifest.OneNote);
            records[key] = new SidecarPage { PageId = page.Id, SectionId = section.Id, Anchors = anchors };
        }

        log.Information("Sidecar rebuilt for {Name}: {Pages} pages, {Sections} sections, {Anchors} anchors re-found", notebook.Name, records.Count, sections.Count, records.Values.Sum(r => r.Anchors.Count));
        Sidecar rebuilt = new()
        {
            ToolVersion = previous?.ToolVersion ?? toolVersion,
            ManifestId = previous?.ManifestId ?? manifest.TemplateId,
            Quarter = quarterSpec,
            NotebookName = notebook.Name,
            NotebookPath = string.IsNullOrWhiteSpace(tree.Path) ? notebook.Path : tree.Path,
            NotebookId = tree.Id,
            Sections = sections,
            Pages = records,
            Generated = previous?.Generated,
            LastRefresh = previous?.LastRefresh,
            FrozenThrough = previous?.FrozenThrough,
            PreviousNotebook = previous?.PreviousNotebook,
        };
        return (rebuilt, pages);
    }

    /// <summary>
    /// Matches the page's known anchors (its link specs, then its propagation slots, then its calendar slots) to the
    /// outlines on the page: an outline is adopted when its position is within <see cref="MatchTolerancePt"/> of where
    /// the tool writes that anchor (sheet origin, scale and the manifest padding applied); each outline is used once.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MatchAnchors(PageSpec page, PageXml xml, SlotCatalog catalog, NotebookPlan plan, OneNoteSpec oneNote)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(oneNote);

        List<OutlineInfo> candidates = xml.Outlines.Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0).ToList();
        Dictionary<string, string> ids = new(StringComparer.Ordinal);
        double scale = SlotInspector.SheetScale(xml, catalog.Manifest.PageWidthPt);
        OutlineInfo? sheet = xml.SheetOutline;
        double ox = (sheet?.X ?? 0) + oneNote.AnchorPaddingPt.X;
        double oy = (sheet?.Y ?? 0) + oneNote.AnchorPaddingPt.Y;

        IEnumerable<(string Id, SlotGeometry? Geometry)> known = plan.LinksFrom(page.Key).Select(l => (l.AnchorId, catalog.Link(page, l.AnchorId)))
            .Concat(catalog.PropagatedSlotsOn(page).Select(id => (id, catalog.Slot(page, id))))
            .Concat(catalog.CalendarSlotsOn(page).Select(id => (id, catalog.Slot(page, id))));
        foreach ((string id, SlotGeometry? geometry) in known)
        {
            if (geometry is null || candidates.Count == 0)
            {
                continue;
            }

            double x = ox + (geometry.Rect.X * scale);
            double y = oy + (geometry.Rect.Y * scale);
            OutlineInfo? best = null;
            double bestDistance = double.MaxValue;
            foreach (OutlineInfo outline in candidates)
            {
                double distance = Math.Max(Math.Abs(outline.X - x), Math.Abs(outline.Y - y));
                if (distance <= MatchTolerancePt && distance < bestDistance)
                {
                    best = outline;
                    bestDistance = distance;
                }
            }

            if (best is not null)
            {
                ids[id] = best.ObjectId;
                candidates.Remove(best);
            }
        }

        return ids;
    }

    /// <summary>The quarter from the page keys: the earliest "calendar:YYYY-MM", else the month of the earliest "day:YYYY-MM-DD".</summary>
    public static QuarterSpec? QuarterFromKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        QuarterSpec? best = null;
        DateOnly? firstDay = null;
        foreach (string key in keys)
        {
            if (key.StartsWith("calendar:", StringComparison.Ordinal)
                && DateOnly.TryParseExact(key["calendar:".Length..] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly month))
            {
                if (best is null || month < new DateOnly(best.Year, best.Month, 1))
                {
                    best = new QuarterSpec(month.Year, month.Month);
                }
            }
            else if (key.StartsWith("day:", StringComparison.Ordinal)
                && DateOnly.TryParseExact(key["day:".Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                && (firstDay is null || day < firstDay))
            {
                firstDay = day;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return firstDay is DateOnly d ? new QuarterSpec(d.Year, d.Month) : null;
    }

    /// <summary>The time-block window typed on the Setup page when it is valid, else the default (the window the pages were generated with is not stored).</summary>
    private TimeBlockWindow WindowFromSetup(IReadOnlyDictionary<string, PageXml> pages)
    {
        if (pages.TryGetValue(PageKey.Setup.Value, out PageXml? setup))
        {
            SetupValues values = new SetupReader().Read(setup, manifest);
            if (values.TimeBlockWindow is (int start, int end) && new TimeBlockWindow(start, end).IsValid)
            {
                return new TimeBlockWindow(start, end);
            }
        }

        return TimeBlockWindow.Default;
    }

    private PageXml? TryRead(HierarchyPage page)
    {
        try
        {
            return PageXmlParser.Parse(app.GetPageContent(page.Id, PageInfo.Basic));
        }
        catch (OneNoteUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is OneNoteException or FormatException or System.Xml.XmlException)
        {
            log.Warning(ex, "Page {Name} ({Id}) could not be read during recovery and is skipped", page.Name, page.Id);
            return null;
        }
    }
}
