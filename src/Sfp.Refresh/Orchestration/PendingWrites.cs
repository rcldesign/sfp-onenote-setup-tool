using Serilog;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// The journal of the outlines a Refresh run is about to write (<see cref="SidecarPage.PendingTexts"/>). A run can write
/// an outline and fail to record it: the re-read that learns a new outline's object ID fails, the page write fails after
/// OneNote applied it, or the sidecar cannot be saved at the end. The journal, saved before the first write, leaves the
/// next run what it needs to take those outlines back, where they would otherwise sit unrecorded in their slots and block
/// them as user objects for good.
/// </summary>
public static class PendingWrites
{
    /// <summary>
    /// The sidecar with the text of every Create and Update among <paramref name="writes"/> journaled on its page, and
    /// whether anything was journaled (the run then saves it before its first write). Removes need no entry.
    /// </summary>
    public static (Sidecar Sidecar, bool Journaled) Journal(Sidecar sidecar, IEnumerable<PlannedChange> writes)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(writes);
        Dictionary<string, Dictionary<string, string>> byPage = new(StringComparer.Ordinal);
        foreach (PlannedChange change in writes.Where(c => c.Kind != ChangeKind.Remove))
        {
            if (!sidecar.Pages.TryGetValue(change.Page.Value, out SidecarPage? record))
            {
                continue;
            }

            if (!byPage.TryGetValue(change.Page.Value, out Dictionary<string, string>? pending))
            {
                pending = record.PendingTexts is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(record.PendingTexts, StringComparer.Ordinal);
                byPage[change.Page.Value] = pending;
            }

            pending[change.SlotId] = change.NewText;
        }

        if (byPage.Count == 0)
        {
            return (sidecar, false);
        }

        Dictionary<string, SidecarPage> pages = sidecar.Pages.ToDictionary(
            p => p.Key,
            p => byPage.TryGetValue(p.Key, out Dictionary<string, string>? pending) ? p.Value with { PendingTexts = pending } : p.Value,
            StringComparer.Ordinal);
        return (sidecar with { Pages = pages }, true);
    }

    /// <summary>
    /// Settles what an earlier run journaled and did not record. For a slot with a recorded outline, the journaled text
    /// becomes the tool's text when the outline holds it (the update went through). For a slot without one, an outline the
    /// tool does not own is taken back as the tool's when it sits where the tool writes that slot (within
    /// <see cref="SidecarRecovery.MatchTolerancePt"/>), carries the tool's signature (<see cref="AnchorSignature"/>) and
    /// holds the journaled text (runs of whitespace compare equal); anything else stays unowned, so a user note is never
    /// taken. The entries of every page that could be read are then dropped; a page that cannot be read keeps its entries.
    /// </summary>
    /// <param name="sidecar">The run's sidecar.</param>
    /// <param name="plan">The notebook plan, to find each page.</param>
    /// <param name="catalog">Slot geometry for the run's quarter and Time Blocks window.</param>
    /// <param name="read">Reads a page by key, null when it cannot be read.</param>
    /// <param name="log">Run log.</param>
    public static Sidecar Resolve(Sidecar sidecar, NotebookPlan plan, SlotCatalog catalog, Func<PageKey, PageXml?> read, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);
        Dictionary<string, SidecarPage>? pages = null;
        foreach ((string key, SidecarPage record) in sidecar.Pages.Where(p => p.Value.PendingTexts is { Count: > 0 }))
        {
            PageKey pageKey = new(key);
            PageSpec? spec = plan.FindPage(pageKey);
            PageXml? xml = spec is null ? null : read(pageKey);
            if (spec is not null && xml is null)
            {
                continue;
            }

            Dictionary<string, string> anchors = new(record.Anchors, StringComparer.Ordinal);
            Dictionary<string, string> texts = new(record.AnchorTexts, StringComparer.Ordinal);
            if (spec is not null && xml is not null)
            {
                foreach ((string slotId, string text) in record.PendingTexts!)
                {
                    Settle(spec, xml, catalog, slotId, text, anchors, texts, log);
                }
            }

            pages ??= new Dictionary<string, SidecarPage>(sidecar.Pages, StringComparer.Ordinal);
            pages[key] = record with { Anchors = anchors, AnchorTexts = texts, PendingTexts = null };
        }

        return pages is null ? sidecar : sidecar with { Pages = pages };
    }

    private static void Settle(PageSpec spec, PageXml xml, SlotCatalog catalog, string slotId, string text, Dictionary<string, string> anchors, Dictionary<string, string> texts, ILogger log)
    {
        if (anchors.TryGetValue(slotId, out string? ownedId))
        {
            OutlineInfo? current = xml.Outlines.FirstOrDefault(o => string.Equals(o.ObjectId, ownedId, StringComparison.Ordinal));
            if (current is not null && !Propagator.IsEdited(current.PlainText, text))
            {
                texts[slotId] = text;
                log.Information("Page {Page}: the text an earlier run wrote in {Slot} is recorded", spec.Key.Value, slotId);
            }

            return;
        }

        SlotGeometry? geometry = catalog.Slot(spec, slotId);
        if (geometry is null)
        {
            return;
        }

        Manifest manifest = catalog.Manifest;
        double scale = SlotInspector.SheetScale(xml, manifest.PageWidthPt);
        OutlineInfo? sheet = xml.SheetOutline;
        double x = (sheet?.X ?? 0) + manifest.OneNote.AnchorPaddingPt.X + (geometry.Rect.X * scale);
        double y = (sheet?.Y ?? 0) + manifest.OneNote.AnchorPaddingPt.Y + (geometry.Rect.Y * scale);
        HashSet<string> owned = anchors.Values.ToHashSet(StringComparer.Ordinal);
        OutlineInfo? best = null;
        double bestDistance = double.MaxValue;
        foreach (OutlineInfo outline in xml.Outlines.Where(o => !o.IsBackgroundImage && o.ObjectId.Length > 0 && !owned.Contains(o.ObjectId)))
        {
            double distance = Math.Max(Math.Abs(outline.X - x), Math.Abs(outline.Y - y));
            if (distance <= SidecarRecovery.MatchTolerancePt && distance < bestDistance
                && !Propagator.IsEdited(outline.PlainText, text)
                && AnchorSignature.Matches(outline, geometry, false, scale, manifest))
            {
                best = outline;
                bestDistance = distance;
            }
        }

        if (best is not null)
        {
            anchors[slotId] = best.ObjectId;
            texts[slotId] = text;
            log.Information("Page {Page}: the outline an earlier run wrote in {Slot} and could not record was taken back ({ObjectId})", spec.Key.Value, slotId, best.ObjectId);
        }
    }
}
