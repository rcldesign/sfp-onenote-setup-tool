using Sfp.Core.Plan;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote;

/// <summary>An open notebook.</summary>
/// <param name="Id">OneNote object ID.</param>
/// <param name="Path">Folder path of the notebook on disk (or its OneDrive URL once moved).</param>
/// <param name="Name">Display name.</param>
public sealed record NotebookHandle(string Id, string Path, string Name);

/// <summary>A section inside a notebook.</summary>
public sealed record SectionHandle(string Id, string Name, NotebookHandle Notebook)
{
    /// <summary>Section tab color as #RRGGBB from the plan, or null for OneNote's default. Written with the hierarchy.</summary>
    public string? ColorHex { get; init; }
}

/// <summary>A page inside a section.</summary>
public sealed record PageHandle(string Id, PageKey Key, SectionHandle Section);

/// <summary>An anchor placement with its hyperlink resolved.</summary>
/// <param name="Placement">Where it goes and what it says.</param>
/// <param name="Href">onenote: URL from GetHyperlinkToObject, or empty for a propagated slot with no link.</param>
/// <param name="ToolOwned">Always true for objects the tool writes; recorded in the sidecar.</param>
public sealed record ResolvedAnchor(AnchorPlacement Placement, string Href, bool ToolOwned);

/// <summary>
/// The only seam that touches OneNote. One production implementation (COM) and one fake for tests. The orchestrator calls
/// the methods in the PRD's order: create notebook, create sections, create every page, apply hierarchy, fetch hyperlinks,
/// then write pages, then sync.
/// </summary>
public interface INotebookWriter
{
    NotebookHandle CreateNotebook(string folder, string name);

    SectionHandle CreateSection(NotebookHandle nb, SectionSpec s);

    PageHandle CreatePage(SectionHandle s, PageSpec p);

    void ApplyHierarchy(SectionHandle s, IReadOnlyList<(PageHandle Handle, PageSpec Spec)> pages);

    string HyperlinkTo(PageHandle p);

    /// <summary>Hyperlink to a section (used by the Contents page).</summary>
    string HyperlinkTo(SectionHandle s);

    void WritePage(PageHandle p, PageSpec spec, RenderedPage art, IReadOnlyList<ResolvedAnchor> anchors);

    PageXml ReadPage(PageHandle p);

    void Sync(NotebookHandle nb);

    /// <summary>
    /// Reads the page back once after <see cref="WritePage"/> and returns the OneNote object ID of the outline written for
    /// each anchor, keyed by its AnchorId, so the sidecar can record them. Anchors whose outline cannot be found are left out.
    /// </summary>
    IReadOnlyDictionary<string, string> ReadAnchorIds(PageHandle p, IReadOnlyList<ResolvedAnchor> anchors);

    /// <summary>Writes the native Contents page: the frame plus one outline of links grouped by section.</summary>
    void WriteContentsPage(PageHandle p, PageSpec spec, IReadOnlyList<ContentsSectionEntry> sections);

    /// <summary>Opens OneNote on the page (the cover at the end of Generate).</summary>
    void NavigateTo(PageHandle p);

    /// <summary>Closes the notebook in OneNote so its folder can be deleted (the cancel path).</summary>
    void CloseNotebook(NotebookHandle nb);

    /// <summary>Non-fatal notes recorded while writing (a page color or page date OneNote refused and the writer dropped).</summary>
    IReadOnlyList<string> Warnings { get; }
}
