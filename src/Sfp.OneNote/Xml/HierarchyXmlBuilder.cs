using System.Globalization;
using System.Xml.Linq;

namespace Sfp.OneNote.Xml;

/// <summary>Builds the one:Section XML that UpdateHierarchy takes to name, order, level and color a section's pages.</summary>
public static class HierarchyXmlBuilder
{
    /// <summary>
    /// The PRD appendix shape: one one:Section with ID, name and optional color, holding one one:Page per entry in order,
    /// each with its ID, name and pageLevel.
    /// </summary>
    /// <param name="sectionId">The section's OneNote ID.</param>
    /// <param name="sectionName">Tab name.</param>
    /// <param name="colorHex">Section color as #RRGGBB, or null to leave OneNote's default.</param>
    /// <param name="pages">Pages in display order with the IDs CreateNewPage returned.</param>
    /// <param name="schemaNamespace">The manifest's schema namespace.</param>
    public static string SectionPages(
        string sectionId,
        string sectionName,
        string? colorHex,
        IReadOnlyList<(string PageId, string Name, int Level)> pages,
        string schemaNamespace = OneNoteXml.Namespace2013)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        XNamespace one = schemaNamespace;
        XElement section = new(
            one + "Section",
            new XAttribute(XNamespace.Xmlns + OneNoteXml.Prefix, one.NamespaceName),
            new XAttribute("ID", sectionId),
            new XAttribute("name", sectionName));
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            section.Add(new XAttribute("color", colorHex));
        }

        foreach ((string pageId, string name, int level) in pages)
        {
            if (level is < 1 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(pages), level, $"Page '{name}' has level {level}; OneNote allows 1 to 3.");
            }

            section.Add(new XElement(
                one + "Page",
                new XAttribute("ID", pageId),
                new XAttribute("name", name),
                new XAttribute("pageLevel", level.ToString(CultureInfo.InvariantCulture))));
        }

        return OneNoteXml.Serialize(section);
    }
}
