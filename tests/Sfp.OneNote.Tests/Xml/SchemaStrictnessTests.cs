using Sfp.OneNote.Tests.Support;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>Proves the validator is strict enough to catch what OneNote would reject: unknown attributes and misordered elements.</summary>
public class SchemaStrictnessTests
{
    private const string Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    [Fact]
    public void An_unknown_attribute_fails_validation()
    {
        IReadOnlyList<string> problems = SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\" ID=\"{{p}}\" bogus=\"1\"/>");

        problems.Should().ContainSingle().Which.Should().Contain("bogus");
    }

    [Fact]
    public void A_title_before_the_page_settings_fails_validation()
    {
        string xml = $"<one:Page xmlns:one=\"{Ns}\" ID=\"{{p}}\"><one:Title><one:OE><one:T><![CDATA[x]]></one:T></one:OE></one:Title><one:PageSettings RTL=\"false\"/></one:Page>";

        SchemaValidator.Problems(xml).Should().NotBeEmpty();
    }

    [Fact]
    public void An_unknown_element_and_a_bad_alignment_value_fail_validation()
    {
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\"><one:Outline><one:Nope/></one:Outline></one:Page>").Should().NotBeEmpty();
        SchemaValidator.Problems($"<one:Page xmlns:one=\"{Ns}\"><one:Outline><one:OEChildren><one:OE alignment=\"middle\"/></one:OEChildren></one:Outline></one:Page>").Should().NotBeEmpty();
    }

    [Fact]
    public void Page_size_dimensions_take_width_and_height_attributes_after_the_orientation()
    {
        static string Page(string pageSize) =>
            $"<one:Page xmlns:one=\"{Ns}\" ID=\"{{p}}\"><one:PageSettings RTL=\"false\" color=\"automatic\"><one:PageSize>{pageSize}</one:PageSize></one:PageSettings></one:Page>";

        SchemaValidator.Problems(Page("<one:Orientation landscape=\"true\"/><one:Dimensions width=\"15.44\" height=\"10.69\"/>")).Should().BeEmpty();
        SchemaValidator.Problems(Page("<one:Dimensions width=\"15.44\" height=\"10.69\"/>")).Should().BeEmpty("the orientation is optional");
        SchemaValidator.Problems(Page("<one:Dimensions><one:Width value=\"1112\"/><one:Height value=\"770\"/></one:Dimensions>")).Should().NotBeEmpty("Dimensions has no child elements");
        SchemaValidator.Problems(Page("<one:Dimensions width=\"15.44\" height=\"10.69\"/><one:Orientation landscape=\"true\"/>")).Should().NotBeEmpty("the orientation comes first");
        SchemaValidator.Problems(Page("<one:Dimensions width=\"15.44\"/>")).Should().NotBeEmpty("both sizes are required");
    }

    [Fact]
    public void A_page_in_another_namespace_is_reported_rather_than_waved_through()
    {
        SchemaValidator.Problems("<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2010/onenote\"/>").Should().NotBeEmpty();
    }
}
