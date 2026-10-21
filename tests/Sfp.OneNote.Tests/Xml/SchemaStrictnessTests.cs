using Sfp.OneNote.Tests.Support;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>Proves the validator is strict enough to catch what OneNote would reject: unknown attributes and misordered elements.</summary>
public class SchemaStrictnessTests
{
    private const string Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    /// <summary>A page ID in the shape OneNote writes, so a test fails only for what it is about.</summary>
    private const string PageId = "{1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C5D}{1}{E19561234567890123456789012345678901234567}";

    [Fact]
    public void An_unknown_attribute_fails_validation()
    {
        IReadOnlyList<string> problems = SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\" ID=\"{PageId}\" bogus=\"1\"/>");

        problems.Should().ContainSingle().Which.Should().Contain("bogus");
    }

    [Fact]
    public void A_placeholder_object_id_fails_the_official_object_id_pattern()
    {
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\" ID=\"{PageId}\"/>").Should().BeEmpty();
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\" ID=\"{{p}}\"/>").Should().ContainSingle().Which.Should().Contain("ObjectID");
        SchemaValidator.Problems($"<one:Section xmlns:one=\"{Ns}\" ID=\"{{7A0B1C2D-3E4F-4A5B-8C6D-9E0F1A2B3C4D}}{{1}}{{B0}}\" name=\"Notes\"/>").Should().BeEmpty("a section ID has the same shape");
    }

    [Fact]
    public void A_title_before_the_page_settings_fails_validation()
    {
        string xml = $"<one:Page xmlns:one=\"{Ns}\" ID=\"{PageId}\"><one:Title><one:OE><one:T><![CDATA[x]]></one:T></one:OE></one:Title><one:PageSettings RTL=\"false\"/></one:Page>";

        SchemaValidator.Problems(xml).Should().Contain(p => p.Contains("'PageSettings'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_element_and_a_bad_alignment_value_fail_validation()
    {
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\"><one:Outline><one:Nope/></one:Outline></one:Page>").Should().NotBeEmpty();
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\"><one:Outline><one:OEChildren><one:OE alignment=\"middle\"/></one:OEChildren></one:Outline></one:Page>").Should().NotBeEmpty();
    }

    [Fact]
    public void A_custom_page_size_is_orientation_then_dimensions_then_margins_all_required()
    {
        static IReadOnlyList<string> PageSize(string content) => SchemaValidator.Problems(
            $"<one:Page xmlns:one=\"{Ns}\" ID=\"{PageId}\"><one:PageSettings RTL=\"false\" color=\"automatic\"><one:PageSize>{content}</one:PageSize></one:PageSettings></one:Page>");
        const string orientation = "<one:Orientation landscape=\"true\"/>";
        const string dimensions = "<one:Dimensions width=\"1112\" height=\"770\"/>";
        const string margins = "<one:Margins top=\"0\" bottom=\"0\" left=\"0\" right=\"0\"/>";

        PageSize(orientation + dimensions + margins).Should().BeEmpty();
        PageSize("<one:Automatic/>").Should().BeEmpty();
        PageSize(orientation + dimensions).Should().ContainSingle().Which.Should().Contain("Margins", "the margins are required");
        PageSize(dimensions + margins).Should().NotBeEmpty("the orientation is required");
        PageSize(orientation + margins).Should().NotBeEmpty("the dimensions are required");
        PageSize(dimensions + orientation + margins).Should().NotBeEmpty("the orientation comes first");
        PageSize("<one:Automatic/>" + orientation + dimensions + margins).Should().NotBeEmpty("Automatic and a custom size are a choice");
        PageSize(orientation + "<one:Dimensions><one:Width value=\"1112\"/></one:Dimensions>" + margins).Should().NotBeEmpty("Dimensions has no child elements");
        PageSize(orientation + "<one:Dimensions width=\"-1\" height=\"770\"/>" + margins).Should().NotBeEmpty("a size is a positive decimal");
    }

    [Fact]
    public void A_page_in_another_namespace_is_reported_rather_than_waved_through()
    {
        SchemaValidator.Problems("<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2010/onenote\"/>").Should().NotBeEmpty();
    }
}
