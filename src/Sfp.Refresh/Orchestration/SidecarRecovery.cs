using System.Globalization;
using Serilog;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Writer;
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
/// The PRD recovery path: when the sidecar is missing, or any page, section or section group ID in it no longer
/// resolves in the notebook's hierarchy, the whole map is rebuilt by walking GetHierarchy(notebook, pages) and reading
/// every page's sfp.key meta (section groups are recorded from the hierarchy by name). Tool-owned anchors are re-found
/// by matching the outlines on each page to the known slot and link rectangles of its page type: the position within a
/// tolerance (never a loose overlap) and the tool's signature on the outline (<see cref="AnchorSignature"/>), so a user
/// object is never adopted. One warning is reported for the summary.
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
    /// Returns the sidecar as given when every ID in it resolves, else rebuilds it. A consistent sidecar that lists pages
    /// its rebuild could not read is completed instead (<see cref="Complete"/>). <paramref name="toolVersion"/> is recorded
    /// when there was no previous sidecar to copy it from.
    /// </summary>
    public RecoveryResult Recover(NotebookHandle notebook, Sidecar? sidecar, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(toolVersion);
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(app.GetHierarchy(notebook.Id, HierarchyScope.Pages));
        if (sidecar is not null && IsConsistent(sidecar, tree))
        {
            if (sidecar.UnreadPages.Count > 0)
            {
                (Sidecar completed, IReadOnlyDictionary<string, PageXml> read) = Complete(notebook, tree, sidecar);
                return new RecoveryResult(completed, false, read, completed.UnreadPages.Count > 0 ? new[] { UnreadWarning(completed.UnreadPages.Count) } : Array.Empty<string>());
            }

            return new RecoveryResult(sidecar, false, new Dictionary<string, PageXml>(StringComparer.Ordinal), Array.Empty<string>());
        }

        log.Warning(sidecar is null ? "No sidecar for {Name}; rebuilding it from the notebook" : "Sidecar for {Name} holds IDs that no longer resolve; rebuilding it from the notebook", notebook.Name);
        (Sidecar rebuilt, IReadOnlyDictionary<string, PageXml> pages) = Rebuild(notebook, tree, sidecar, toolVersion);
        return new RecoveryResult(rebuilt, true, pages, rebuilt.UnreadPages.Count > 0 ? new[] { RebuiltWarning, UnreadWarning(rebuilt.UnreadPages.Count) } : new[] { RebuiltWarning });
    }

    /// <summary>"3 page(s) of the notebook could not be read; Refresh reads them again on the next run."</summary>
    public static string UnreadWarning(int count) =>
        string.Format(CultureInfo.InvariantCulture, "{0} page(s) of the notebook could not be read; Refresh reads them again on the next run.", count);

    /// <summary>
    /// The completion step for a sidecar whose rebuild could not read some pages: every page of the hierarchy the sidecar
    /// does not record (those pages among them) is read; each that carries a page key the sidecar lacks is added with its
    /// anchors re-found as in a rebuild, and the pages that still cannot be read stay listed for the next run.
    /// </summary>
    public (Sidecar Sidecar, IReadOnlyDictionary<string, PageXml> Pages) Complete(NotebookHandle notebook, HierarchyNotebook tree, Sidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(sidecar);
        HashSet<string> recorded = sidecar.Pages.Values.Select(p => p.PageId).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, PageXml> pages = new(StringComparer.Ordinal);
        Dictionary<string, (HierarchySection Section, HierarchyPage Page)> located = new(StringComparer.Ordinal);
        List<string> unread = new();
        foreach ((HierarchySection section, HierarchyPage page) in tree.Pages.Where(p => !recorded.Contains(p.Page.Id)))
        {
            PageXml? xml = TryRead(page);
            if (xml is null)
            {
                unread.Add(page.Id);
                continue;
            }

            string? key = xml.SfpKey;
            if (string.IsNullOrEmpty(key) || sidecar.Pages.ContainsKey(key) || !located.TryAdd(key, (section, page)))
            {
                continue;
            }

            pages[key] = xml;
        }

        Quarter quarter = dates.Build(sidecar.Quarter);
        NotebookPlan plan = plans.Build(manifest, quarter, SidecarPlanOptions.ToGenerateOptions(sidecar.Quarter, sidecar.PlanOptions));
        SlotCatalog catalog = new(manifest, dates, quarter, WindowFor(sidecar, pages));
        Dictionary<string, SidecarPage> records = new(sidecar.Pages, StringComparer.Ordinal);
        foreach ((string key, (HierarchySection section, HierarchyPage page)) in located)
        {
            PageSpec? spec = plan.FindPage(new PageKey(key));
            IReadOnlyDictionary<string, string> anchors = spec is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : MatchAnchors(spec, pages[key], catalog, plan, manifest.OneNote);
            records[key] = new SidecarPage { PageId = page.Id, SectionId = section.Id, Anchors = anchors };
        }

        log.Information("Sidecar for {Name} completed: {Added} pages added, {Unread} still unread", notebook.Name, located.Count, unread.Count);
        return (sidecar with { Pages = records, UnreadPages = unread }, pages);
    }

    /// <summary>
    /// True when the notebook ID, every section group ID, every section ID and every page ID the sidecar records are
    /// present in the hierarchy. The pages a rebuild could not read (<see cref="Sidecar.UnreadPages"/>) are not checked:
    /// they call for the completion step, not a rebuild.
    /// </summary>
    public static bool IsConsistent(Sidecar sidecar, HierarchyNotebook tree)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(tree);
        if (!string.Equals(sidecar.NotebookId, tree.Id, StringComparison.Ordinal))
        {
            return false;
        }

        HashSet<string> groupIds = tree.SectionGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> sectionIds = tree.Sections.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> pageIds = tree.Pages.Select(p => p.Page.Id).ToHashSet(StringComparer.Ordinal);
        return sidecar.SectionGroups.Values.All(groupIds.Contains)
            && sidecar.Sections.Values.All(sectionIds.Contains)
            && sidecar.Pages.Values.All(p => pageIds.Contains(p.PageId) && sectionIds.Contains(p.SectionId));
    }

    /// <summary>
    /// Walks every page of the hierarchy, reads its sfp.key, and rebuilds the page, section and anchor maps, listing the
    /// pages that could not be read (<see cref="Sidecar.UnreadPages"/>) for the next run's completion step; the history
    /// of a stale sidecar (stamps, frozen days, previous notebook, plan options, confirmed inked values, Time Blocks
    /// window) is kept, and its plan
    /// options shape the plan the pages are matched against.
    /// </summary>
    public (Sidecar Sidecar, IReadOnlyDictionary<string, PageXml> Pages) Rebuild(NotebookHandle notebook, HierarchyNotebook tree, Sidecar? previous, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(toolVersion);

        Dictionary<string, PageXml> pages = new(StringComparer.Ordinal);
        Dictionary<string, (HierarchySection Section, HierarchyPage Page)> located = new(StringComparer.Ordinal);
        List<string> unread = new();
        foreach ((HierarchySection section, HierarchyPage page) in tree.Pages)
        {
            PageXml? xml = TryRead(page);
            if (xml is null)
            {
                unread.Add(page.Id);
                continue;
            }

            string? key = xml.SfpKey;
            if (string.IsNullOrEmpty(key))
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
        NotebookPlan plan = plans.Build(manifest, quarter, SidecarPlanOptions.ToGenerateOptions(quarterSpec, previous?.PlanOptions));
        SlotCatalog catalog = new(manifest, dates, quarter, WindowFor(previous, pages));

        Dictionary<string, string> sections = new(StringComparer.Ordinal);
        foreach (HierarchySection section in tree.Sections)
        {
            sections.TryAdd(section.Name, section.Id);
        }

        Dictionary<string, string> groups = new(StringComparer.Ordinal);
        foreach (HierarchySectionGroup group in tree.SectionGroups)
        {
            groups.TryAdd(group.Name, group.Id);
        }

        Dictionary<string, SidecarPage> records = new(StringComparer.Ordinal);
        foreach ((string key, (HierarchySection section, HierarchyPage page)) in located)
        {
            PageSpec? spec = plan.FindPage(new PageKey(key));
            IReadOnlyDictionary<string, string> anchors = spec is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : MatchAnchors(spec, pages[key], catalog, plan, manifest.OneNote);
            records[key] = new SidecarPage { PageId = page.Id, SectionId = section.Id, Anchors = anchors, AnchorTexts = CarriedTexts(previous, key, anchors) };
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
            SectionGroups = groups,
            Pages = records,
            Generated = previous?.Generated,
            LastRefresh = previous?.LastRefresh,
            FrozenThrough = previous?.FrozenThrough,
            PreviousNotebook = previous?.PreviousNotebook,
            PlanOptions = previous?.PlanOptions,
            ConfirmedInked = previous?.ConfirmedInked ?? new Dictionary<string, string>(StringComparer.Ordinal),
            UnreadPages = unread,
            TimeBlocks = previous?.TimeBlocks,
        };
        return (rebuilt, pages);
    }

    /// <summary>
    /// Matches the page's known anchors (its link specs, the Next quarter link on the quarterly review, then its
    /// propagation slots, then its calendar slots) to the outlines on the page. An outline is adopted only when its
    /// position is within <see cref="MatchTolerancePt"/> of where the tool writes that anchor (sheet origin, scale and the
    /// manifest padding applied) and it carries the tool's signature (<see cref="AnchorSignature"/>: the fixed width of the
    /// slot, the manifest typeface in the tool's color, an onenote: link for a link anchor). Anything else, such as a note
    /// the user typed at a slot's origin, stays unowned and therefore blocks the slot. Each outline is used once.
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

        List<LinkSpec> links = plan.LinksFrom(page.Key).ToList();
        IEnumerable<(string Id, SlotGeometry? Geometry, bool IsLink)> known = links.Select(l => (l.AnchorId, catalog.Link(page, l.AnchorId), true))
            .Concat(page.Key == PageKey.QuarterlyReview && !links.Any(l => l.AnchorId == NextQuarterLinkWriter.AnchorId)
                ? new[] { (NextQuarterLinkWriter.AnchorId, catalog.Link(page, NextQuarterLinkWriter.AnchorId), true) }
                : Array.Empty<(string, SlotGeometry?, bool)>())
            .Concat(catalog.PropagatedSlotsOn(page).Select(id => (id, catalog.Slot(page, id), false)))
            .Concat(catalog.CalendarSlotsOn(page).Select(id => (id, catalog.Slot(page, id), false)));
        foreach ((string id, SlotGeometry? geometry, bool isLink) in known)
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
                if (distance <= MatchTolerancePt && distance < bestDistance && AnchorSignature.Matches(outline, geometry, isLink, scale, catalog.Manifest))
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

    /// <summary>
    /// The texts the tool last wrote, carried from a stale sidecar for the slots whose outline was re-found: the slot is
    /// the same and so is what the tool wrote there, whatever the object IDs became. Nothing is guessed for a slot the
    /// stale sidecar did not record (Refresh then skips the edited check for it until it writes the slot again).
    /// </summary>
    private static Dictionary<string, string> CarriedTexts(Sidecar? previous, string key, IReadOnlyDictionary<string, string> anchors)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        if (previous is not null && previous.Pages.TryGetValue(key, out SidecarPage? stale))
        {
            foreach ((string slotId, string text) in stale.AnchorTexts.Where(t => anchors.ContainsKey(t.Key)))
            {
                texts[slotId] = text;
            }
        }

        return texts;
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

    /// <summary>
    /// The window the daily pages were generated with, for re-finding the calendar rows: the one the stale sidecar
    /// records, else the one typed on the Setup page when valid, else the default.
    /// </summary>
    private TimeBlockWindow WindowFor(Sidecar? previous, IReadOnlyDictionary<string, PageXml> pages)
    {
        if (previous?.TimeBlocks is { IsValid: true } recorded)
        {
            return recorded;
        }

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
