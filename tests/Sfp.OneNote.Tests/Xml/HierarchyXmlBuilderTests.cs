using System.Xml.Linq;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

public class HierarchyXmlBuilderTests
{
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    /// <summary>A section ID in the shape OneNote writes ({GUID}{1}{B0}).</summary>
    private const string SectionId = "{7A0B1C2D-3E4F-4A5B-8C6D-9E0F1A2B3C4D}{1}{B0}";

    private static readonly IReadOnlyList<(string PageId, string Name, int Level)> Week02 = new[]
    {
        (PageId(1), "Weekly Overview", 1),
        (PageId(2), "Mon Jan 4", 2),
        (PageId(3), "Tue Jan 5", 2),
        (PageId(4), "Wed Jan 6", 2),
        (PageId(5), "Thu Jan 7", 2),
        (PageId(6), "Fri Jan 8", 2),
        (PageId(7), "Sat Jan 9", 2),
        (PageId(8), "Sun Jan 10", 2),
        (PageId(9), "Weekly Reflection", 1),
    };

    /// <summary>The nth page ID of the section, in the shape CreateNewPage returns ({GUID}{1}{E...}).</summary>
    private static string PageId(int n) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "{{8B1C2D3E-4F5A-4B6C-9D7E-0F1A2B3C4D{0:D2}}}{{1}}{{E19512345678901234567890123456789012345678}}", n);

    [Fact]
    public void Week_section_hierarchy_matches_the_prd_appendix_shape()
    {
        string xml = HierarchyXmlBuilder.SectionPages(SectionId, "Week 02 (Jan 4 to Jan 10)", null, Week02);

        XElement section = XElement.Parse(xml);
        section.Name.Should().Be(One + "Section");
        section.Attribute("ID")!.Value.Should().Be(SectionId);
        section.Attribute("name")!.Value.Should().Be("Week 02 (Jan 4 to Jan 10)");
        section.Attribute("color").Should().BeNull();
        section.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName).Should().Equal("ID", "name");

        List<XElement> pages = section.Elements().ToList();
        pages.Should().HaveCount(9).And.OnlyContain(p => p.Name == One + "Page");
        pages.Select(p => (p.Attribute("ID")!.Value, p.Attribute("name")!.Value, int.Parse(p.Attribute("pageLevel")!.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Should().Equal(Week02);
        pages.Should().OnlyContain(p => p.Attributes().Count() == 3, "each page carries exactly ID, name and pageLevel");
        pages.Should().OnlyContain(p => p.IsEmpty);
        SchemaValidator.AssertValid(xml);
    }

    [Fact]
    public void Section_color_is_written_when_given()
    {
        string xml = HierarchyXmlBuilder.SectionPages(SectionId, "Week 05 (Jan 25 to Jan 31)", "#F0E6D8", Week02);

        XElement.Parse(xml).Attribute("color")!.Value.Should().Be("#F0E6D8");
        SchemaValidator.AssertValid(xml);
    }

    [Fact]
    public void Names_with_xml_special_characters_are_escaped()
    {
        string xml = HierarchyXmlBuilder.SectionPages("{s}", "Q&A <week>", null, new[] { ("{p}", "Tom & \"Jerry\"", 1) });

        XElement section = XElement.Parse(xml);
        section.Attribute("name")!.Value.Should().Be("Q&A <week>");
        section.Elements().Single().Attribute("name")!.Value.Should().Be("Tom & \"Jerry\"");
    }

    [Fact]
    public void A_page_level_outside_one_to_three_is_rejected()
    {
        Action act = () => HierarchyXmlBuilder.SectionPages("{s}", "Quarter", null, new[] { ("{p}", "Cover", 4) });

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Cover*level 4*");
    }
}
