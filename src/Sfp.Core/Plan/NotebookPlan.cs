using Sfp.Core.Dates;

namespace Sfp.Core.Plan;

/// <summary>The complete, writer-independent description of one notebook: name, quarter, sections, pages and links.</summary>
/// <param name="Name">Notebook name, for example "SFP Q1 2027".</param>
/// <param name="Quarter">The quarter the notebook covers.</param>
/// <param name="Sections">Sections in tab order.</param>
/// <param name="Links">Every link anchor in the notebook (page links plus navigation).</param>
public sealed record NotebookPlan(string Name, Quarter Quarter, IReadOnlyList<SectionSpec> Sections, IReadOnlyList<LinkSpec> Links)
{
    /// <summary>All pages in notebook order.</summary>
    public IEnumerable<PageSpec> Pages => Sections.SelectMany(s => s.Pages);

    /// <summary>Finds a page by key, or null.</summary>
    public PageSpec? FindPage(PageKey key) => Pages.FirstOrDefault(p => p.Key == key);

    /// <summary>Finds a page by key, or throws.</summary>
    public PageSpec Page(PageKey key) => FindPage(key) ?? throw new KeyNotFoundException($"No page with key '{key}' in plan '{Name}'.");

    /// <summary>The section that holds a page.</summary>
    public SectionSpec SectionOf(PageKey key) =>
        Sections.FirstOrDefault(s => s.Pages.Any(p => p.Key == key)) ?? throw new KeyNotFoundException($"No section holds page '{key}'.");

    /// <summary>Links whose anchor sits on the given page.</summary>
    public IEnumerable<LinkSpec> LinksFrom(PageKey key) => Links.Where(l => l.From == key);
}
