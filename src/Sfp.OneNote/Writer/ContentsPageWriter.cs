using Sfp.Core.Plan;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Writer;

/// <summary>
/// Turns the plan plus the hyperlinks gathered in the hyperlink phase into the Contents page entries: sections in tab
/// order, each with its pages in order and their levels, so <see cref="ContentsXmlBuilder"/> can nest day pages under
/// the overview before them as the PRD appendix shows. The Contents page itself is left out of its own list.
/// </summary>
public static class ContentsPageWriter
{
    /// <summary>The entries for the plan.</summary>
    /// <param name="plan">The notebook plan.</param>
    /// <param name="sectionHrefs">onenote: URL per section name.</param>
    /// <param name="pageHrefs">onenote: URL per page key.</param>
    public static IReadOnlyList<ContentsSectionEntry> Entries(
        NotebookPlan plan,
        IReadOnlyDictionary<string, string> sectionHrefs,
        IReadOnlyDictionary<PageKey, string> pageHrefs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sectionHrefs);
        ArgumentNullException.ThrowIfNull(pageHrefs);
        List<ContentsSectionEntry> sections = new(plan.Sections.Count);
        foreach (SectionSpec section in plan.Sections)
        {
            List<ContentsPageEntry> pages = new(section.Pages.Count);
            foreach (PageSpec page in section.Pages)
            {
                if (page.Type == PageType.Contents)
                {
                    continue;
                }

                pages.Add(new ContentsPageEntry(page.Title, Href(pageHrefs, page.Key), page.Level));
            }

            string sectionHref = sectionHrefs.TryGetValue(section.Name, out string? href) ? href : "";
            sections.Add(new ContentsSectionEntry(section.Name, sectionHref, pages));
        }

        return sections;
    }

    /// <summary>Writes the Contents page through the writer.</summary>
    public static void Write(INotebookWriter writer, PageHandle contents, PageSpec spec, IReadOnlyList<ContentsSectionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteContentsPage(contents, spec, entries);
    }

    private static string Href(IReadOnlyDictionary<PageKey, string> pageHrefs, PageKey key) =>
        pageHrefs.TryGetValue(key, out string? href) ? href : "";
}
