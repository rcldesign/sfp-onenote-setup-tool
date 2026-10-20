using System.Globalization;
using System.Xml.Linq;

namespace Sfp.Refresh.Orchestration;

/// <summary>A page as GetHierarchy lists it.</summary>
public sealed record HierarchyPage(string Id, string Name, int Level);

/// <summary>A section as GetHierarchy lists it, with its pages when the scope reached them.</summary>
public sealed record HierarchySection(string Id, string Name, string? Path, IReadOnlyList<HierarchyPage> Pages);

/// <summary>A section group as GetHierarchy lists it (the "Weeks" group of a notebook generated with --group-weeks).</summary>
public sealed record HierarchySectionGroup(string Id, string Name, string? Path);

/// <summary>A notebook as GetHierarchy lists it, with its sections (section groups flattened, in document order) when the scope reached them.</summary>
public sealed record HierarchyNotebook(string Id, string Name, string? Path, IReadOnlyList<HierarchySection> Sections)
{
    /// <summary>The notebook's section groups, nested ones included, in document order; empty when the scope did not reach them.</summary>
    public IReadOnlyList<HierarchySectionGroup> SectionGroups { get; init; } = Array.Empty<HierarchySectionGroup>();

    /// <summary>Every page of every section, in order.</summary>
    public IEnumerable<(HierarchySection Section, HierarchyPage Page)> Pages =>
        Sections.SelectMany(s => s.Pages.Select(p => (s, p)));
}

/// <summary>
/// Reads the XML GetHierarchy returns (one:Notebooks, one:Notebook or one:Section at the root) into records. Sections
/// inside one:SectionGroup elements are flattened in document order so a notebook generated with the Weeks group reads
/// the same as a flat one, and the groups themselves are listed beside them. OneNote's recycle bin (the section group
/// marked isRecycleBin, and any section marked isInRecycleBin) is left out: deleted pages are not the notebook's pages.
/// The namespace is taken from the root so 2010 and 2013 documents both parse.
/// </summary>
public static class HierarchyXmlReader
{
    /// <summary>The notebooks of a one:Notebooks document (or the single notebook of a one:Notebook root).</summary>
    public static IReadOnlyList<HierarchyNotebook> Notebooks(string xml)
    {
        XElement root = Root(xml);
        XNamespace one = root.Name.Namespace;
        return root.Name.LocalName switch
        {
            "Notebooks" => root.Elements(one + "Notebook").Select(n => Notebook(n, one)).ToList(),
            "Notebook" => new[] { Notebook(root, one) },
            _ => throw new FormatException($"Expected a one:Notebooks or one:Notebook root element, got '{root.Name.LocalName}'."),
        };
    }

    /// <summary>The notebook of a one:Notebook document (GetHierarchy with a notebook ID).</summary>
    public static HierarchyNotebook Notebook(string xml)
    {
        XElement root = Root(xml);
        if (root.Name.LocalName != "Notebook")
        {
            throw new FormatException($"Expected a one:Notebook root element, got '{root.Name.LocalName}'.");
        }

        return Notebook(root, root.Name.Namespace);
    }

    /// <summary>The section of a one:Section document (GetHierarchy with a section ID).</summary>
    public static HierarchySection Section(string xml)
    {
        XElement root = Root(xml);
        if (root.Name.LocalName != "Section")
        {
            throw new FormatException($"Expected a one:Section root element, got '{root.Name.LocalName}'.");
        }

        return Section(root, root.Name.Namespace);
    }

    /// <summary>The ID attribute of whatever element is at the root (a page from GetHierarchy with a page ID and hsSelf).</summary>
    public static string RootId(string xml) => (string?)Root(xml).Attribute("ID") ?? "";

    private static XElement Root(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        return XDocument.Parse(xml).Root ?? throw new FormatException("The hierarchy XML has no root element.");
    }

    private static HierarchyNotebook Notebook(XElement element, XNamespace one) => new(
        Attr(element, "ID"),
        Attr(element, "name"),
        (string?)element.Attribute("path"),
        Sections(element, one).ToList())
    {
        SectionGroups = Groups(element, one).ToList(),
    };

    private static IEnumerable<HierarchySection> Sections(XElement parent, XNamespace one)
    {
        foreach (XElement child in parent.Elements())
        {
            if (child.Name.Namespace != one)
            {
                continue;
            }

            if (child.Name.LocalName == "Section" && !Flag(child, "isInRecycleBin"))
            {
                yield return Section(child, one);
            }
            else if (child.Name.LocalName == "SectionGroup" && !Flag(child, "isRecycleBin"))
            {
                foreach (HierarchySection nested in Sections(child, one))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<HierarchySectionGroup> Groups(XElement parent, XNamespace one)
    {
        foreach (XElement group in parent.Elements(one + "SectionGroup").Where(g => !Flag(g, "isRecycleBin")))
        {
            yield return new HierarchySectionGroup(Attr(group, "ID"), Attr(group, "name"), (string?)group.Attribute("path"));
            foreach (HierarchySectionGroup nested in Groups(group, one))
            {
                yield return nested;
            }
        }
    }

    private static bool Flag(XElement element, string attribute) =>
        string.Equals((string?)element.Attribute(attribute), "true", StringComparison.OrdinalIgnoreCase);

    private static HierarchySection Section(XElement element, XNamespace one) => new(
        Attr(element, "ID"),
        Attr(element, "name"),
        (string?)element.Attribute("path"),
        element.Elements(one + "Page").Select(p => new HierarchyPage(Attr(p, "ID"), Attr(p, "name"), Level(p))).ToList());

    private static int Level(XElement page) =>
        int.TryParse((string?)page.Attribute("pageLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ? level : 1;

    private static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? "";
}
