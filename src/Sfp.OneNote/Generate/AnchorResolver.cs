using Sfp.Core.Plan;
using Sfp.OneNote.Writer;
using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>
/// Joins a rendered page's anchor placements to the plan's link specs and the hyperlinks gathered in the hyperlink
/// phase. Only link anchors are written at Generate: propagated slots (legend, standing goals, metric names, habits,
/// calendar lines) are created by Refresh when they first get a value, so the sidecar never holds empty objects.
/// </summary>
public static class AnchorResolver
{
    /// <summary>The anchors to write for a page, in placement order.</summary>
    /// <param name="page">The page.</param>
    /// <param name="art">Its rendered sheet with the placements.</param>
    /// <param name="links">The plan's links from this page, keyed by AnchorId.</param>
    /// <param name="pageHrefs">onenote: URL per page key.</param>
    /// <param name="linksEnabled">False writes no anchors at all (the purely locked sheet).</param>
    /// <param name="problems">Receives a line per placement that could not be resolved (no link spec, or no hyperlink for its target); the reserved Next quarter slot is skipped without one.</param>
    public static IReadOnlyList<ResolvedAnchor> Resolve(
        PageSpec page,
        RenderedPage art,
        IReadOnlyDictionary<string, LinkSpec> links,
        IReadOnlyDictionary<PageKey, string> pageHrefs,
        bool linksEnabled,
        ICollection<string>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(pageHrefs);
        if (!linksEnabled)
        {
            return Array.Empty<ResolvedAnchor>();
        }

        List<ResolvedAnchor> resolved = new();
        foreach (AnchorPlacement placement in art.Anchors)
        {
            if (placement.Kind != AnchorKind.Link)
            {
                continue;
            }

            if (!links.TryGetValue(placement.AnchorId, out LinkSpec? link))
            {
                // The quarterly review's Next quarter slot is reserved, not linked: a later Generate with --previous fills it.
                if (!string.Equals(placement.AnchorId, NextQuarterLinkWriter.AnchorId, StringComparison.Ordinal))
                {
                    problems?.Add($"Page {page.Key}: the renderer placed link anchor '{placement.AnchorId}' but the plan has no link for it; left out.");
                }

                continue;
            }

            if (!pageHrefs.TryGetValue(link.To, out string? href) || string.IsNullOrEmpty(href))
            {
                problems?.Add($"Page {page.Key}: link anchor '{placement.AnchorId}' targets page {link.To}, which has no hyperlink; left out.");
                continue;
            }

            resolved.Add(new ResolvedAnchor(placement, href, ToolOwned: true));
        }

        return resolved;
    }

    /// <summary>The plan's links grouped by the page that carries them, each keyed by AnchorId.</summary>
    public static IReadOnlyDictionary<PageKey, IReadOnlyDictionary<string, LinkSpec>> LinksByPage(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Dictionary<PageKey, Dictionary<string, LinkSpec>> byPage = new();
        foreach (LinkSpec link in plan.Links)
        {
            if (!byPage.TryGetValue(link.From, out Dictionary<string, LinkSpec>? page))
            {
                page = new Dictionary<string, LinkSpec>(StringComparer.Ordinal);
                byPage[link.From] = page;
            }

            page[link.AnchorId] = link;
        }

        return byPage.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, LinkSpec>)p.Value);
    }

    /// <summary>An empty link map for a page without links.</summary>
    public static IReadOnlyDictionary<string, LinkSpec> None { get; } = new Dictionary<string, LinkSpec>(StringComparer.Ordinal);
}
