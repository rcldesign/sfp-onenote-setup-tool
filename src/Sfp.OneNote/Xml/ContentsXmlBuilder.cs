using System.Net;
using System.Xml.Linq;
using Sfp.Core.Plan;

namespace Sfp.OneNote.Xml;

/// <summary>A page line on the Contents outline.</summary>
/// <param name="Title">Visible text, the page title.</param>
/// <param name="Href">onenote: URL from GetHyperlinkToObject.</param>
/// <param name="Level">OneNote page level; level 2 pages nest under the level 1 page before them.</param>
public sealed record ContentsPageEntry(string Title, string Href, int Level);

/// <summary>A section line on the Contents outline with its pages.</summary>
/// <param name="Name">Visible text, the section name.</param>
/// <param name="Href">onenote: URL to the section.</param>
/// <param name="Pages">The section's pages in order.</param>
public sealed record ContentsSectionEntry(string Name, string Href, IReadOnlyList<ContentsPageEntry> Pages);

/// <summary>
/// Builds the native Contents page: one outline at the manifest contentsOutline position holding a link per section,
/// its page links nested under it, and week subpages nested one level deeper, per the PRD appendix.
/// </summary>
public static class ContentsXmlBuilder
{
    /// <summary>The one:Outline element.</summary>
    public static XElement OutlineElement(IReadOnlyList<ContentsSectionEntry> sections, PageXmlOptions options)
    {
        XNamespace one = options.OneNote.SchemaNamespace;
        XElement children = new(one + "OEChildren");
        foreach (ContentsSectionEntry section in sections)
        {
            XElement sectionOe = LinkOe(one, section.Name, section.Href);
            if (section.Pages.Count > 0)
            {
                XElement pageChildren = new(one + "OEChildren");
                XElement? lastTopLevel = null;
                foreach (ContentsPageEntry page in section.Pages)
                {
                    XElement pageOe = LinkOe(one, page.Title, page.Href);
                    if (page.Level > 1 && lastTopLevel is not null)
                    {
                        XElement? nested = lastTopLevel.Element(one + "OEChildren");
                        if (nested is null)
                        {
                            nested = new XElement(one + "OEChildren");
                            lastTopLevel.Add(nested);
                        }

                        nested.Add(pageOe);
                    }
                    else
                    {
                        pageChildren.Add(pageOe);
                        lastTopLevel = pageOe;
                    }
                }

                sectionOe.Add(pageChildren);
            }

            children.Add(sectionOe);
        }

        return new XElement(
            one + "Outline",
            OneNoteXml.Position(one, options.OneNote.ContentsOutline.X, options.OneNote.ContentsOutline.Y, 0),
            children);
    }

    /// <summary>The outline serialized on its own (with the namespace declared on it).</summary>
    public static string Outline(IReadOnlyList<ContentsSectionEntry> sections, PageXmlOptions options)
    {
        XElement outline = OutlineElement(sections, options);
        outline.Add(new XAttribute(XNamespace.Xmlns + OneNoteXml.Prefix, options.OneNote.SchemaNamespace));
        return OneNoteXml.Serialize(outline);
    }

    /// <summary>The whole Contents page: the standard frame (key meta, settings, title) plus the outline, no sheet image.</summary>
    public static string Page(string pageId, PageSpec page, IReadOnlyList<ContentsSectionEntry> sections, PageXmlOptions options)
    {
        XElement root = PageXmlBuilder.Frame(pageId, page, options);
        root.Add(OutlineElement(sections, options));
        return OneNoteXml.Serialize(root);
    }

    private static XElement LinkOe(XNamespace one, string text, string href)
    {
        string html = string.IsNullOrEmpty(href)
            ? WebUtility.HtmlEncode(text)
            : string.Concat("<a href=\"", WebUtility.HtmlEncode(href), "\">", WebUtility.HtmlEncode(text), "</a>");
        return new XElement(one + "OE", new XElement(one + "T", new XCData(html)));
    }
}
