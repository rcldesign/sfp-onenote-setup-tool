using System.Xml;
using Sfp.Core.Manifests;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>M4.1: the parser on the hand-written Setup page fixture and on the builder's own output.</summary>
public class PageXmlParserTests
{
    private static readonly Manifest M = TestManifest.Load();

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "setup-page.xml");

    private static PageXml Setup() => PageXmlParser.Parse(File.ReadAllText(FixturePath));

    [Fact]
    public void Fixture_validates_against_the_schema_subset()
    {
        SchemaValidator.AssertValid(File.ReadAllText(FixturePath));
    }

    [Fact]
    public void Reads_page_id_title_and_the_sfp_key_meta()
    {
        PageXml page = Setup();

        page.PageId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{1}{E19512345678901234567890123456789012345678}");
        page.Title.Should().Be("Setup");
        page.SfpKey.Should().Be("setup");
        page.Meta.Should().ContainKey("sfp.key");
        page.Document.Root.Should().NotBeNull();
    }

    [Fact]
    public void Finds_the_sheet_outline_and_the_background_image()
    {
        PageXml page = Setup();

        OutlineInfo sheet = page.SheetOutline!;
        sheet.IsBackgroundImage.Should().BeTrue();
        sheet.X.Should().Be(0);
        sheet.Y.Should().Be(36);
        sheet.Z.Should().Be(0);
        sheet.Width.Should().Be(1112);
        sheet.Height.Should().Be(770);
        sheet.ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{15}{B0}");
        sheet.TextRuns.Should().BeEmpty();

        ImageInfo image = page.Images.Should().ContainSingle().Which;
        image.IsBackground.Should().BeTrue();
        image.Width.Should().Be(1112);
        image.Height.Should().Be(770);
        image.Hyperlink.Should().BeNull();
        image.ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{17}{B0}");
    }

    [Fact]
    public void Finds_the_typed_run_by_its_slot_rectangle()
    {
        PageXml page = Setup();

        OutlineInfo outline = page.OutlinesIn(M.Generated.Setup.Objectives[0]).Should().ContainSingle().Which;
        outline.PlainText.Should().Be("Ship the connector");
        outline.InkWords.Should().BeEmpty();
        outline.Z.Should().Be(1);
        outline.Rect.Should().Be(new RectPt(64.8, 128.4, 214.2, 13.0));
        TextRun run = outline.TextRuns.Should().ContainSingle().Which;
        run.ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{21}{B0}", "the run is identified by its enclosing OE");
        run.Html.Should().Be("Ship the connector");
    }

    [Fact]
    public void Finds_the_inked_words_by_their_slot_rectangle_with_the_enclosing_outline()
    {
        PageXml page = Setup();

        OutlineInfo outline = page.OutlinesIn(M.Generated.Setup.Objectives[1]).Should().ContainSingle().Which;
        outline.TextRuns.Should().BeEmpty();
        outline.InkWords.Should().HaveCount(2);
        outline.InkWords[0].RecognizedText.Should().Be("Hire");
        outline.InkWords[1].RecognizedText.Should().Be("two PMs");
        outline.InkWords[0].ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{32}{B0}");
        outline.InkWords[0].Bounds.Should().BeNull("an ink word carries no position of its own; the outline does");
        outline.InkText.Should().Be("Hire two PMs");
        outline.Rect.Should().Be(new RectPt(61.2, 153.7, 148.6, 18.9));
        outline.ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{30}{B0}");
    }

    [Fact]
    public void Strips_tags_and_decodes_entities_in_typed_text_but_keeps_the_html()
    {
        PageXml page = Setup();

        OutlineInfo outline = page.OutlinesIn(M.Generated.Setup.MetricNames[0]).Should().ContainSingle().Which;
        outline.PlainText.Should().Be("Sales & Marketing calls");
        outline.TextRuns[0].Html.Should().Contain("<span style='font-weight:bold'>Sales &amp; Marketing</span>");
    }

    [Fact]
    public void Empty_slots_find_nothing_and_the_sheet_is_never_returned()
    {
        PageXml page = Setup();

        page.OutlinesIn(M.Generated.Setup.Objectives[2]).Should().BeEmpty();
        page.OutlinesIn(M.Generated.Setup.StandingGoals[0]).Should().BeEmpty();
        page.OutlinesIn(new RectPt(0, 0, 1112, 770)).Should().HaveCount(3, "every user outline overlaps the whole sheet; the sheet itself is excluded");
        page.Outlines.Should().HaveCount(4);
    }

    [Fact]
    public void Reads_page_level_ink_drawings_with_their_bounds()
    {
        PageXml page = Setup();

        InkDrawingInfo drawing = page.InkDrawings().Should().ContainSingle().Which;
        drawing.ObjectId.Should().Be("{3B1C5A77-9D2E-4F60-8A41-6C0E2B9D5F13}{40}{B0}");
        drawing.RecognizedText.Should().BeEmpty();
        drawing.Bounds.Should().Be(new RectPt(300, 330, 40, 18));
        drawing.OutlineObjectId.Should().BeNull();
        page.InkDrawingsIn(new RectPt(290, 285, 60, 30)).Should().ContainSingle();
        page.InkDrawingsIn(M.Generated.Setup.Objectives[0]).Should().BeEmpty();
    }

    [Fact]
    public void An_ink_drawing_inside_an_outline_borrows_the_outline_rectangle()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{p}">
              <one:Outline objectID="{o}">
                <one:Position x="10" y="20" z="0"/>
                <one:Size width="100" height="30"/>
                <one:OEChildren><one:OE objectID="{oe}"><one:InkDrawing objectID="{d}" recognizedText="hello"/></one:OE></one:OEChildren>
              </one:Outline>
            </one:Page>
            """;

        InkDrawingInfo drawing = PageXmlParser.Parse(xml).InkDrawings().Should().ContainSingle().Which;

        drawing.Bounds.Should().Be(new RectPt(10, 20, 100, 30));
        drawing.OutlineObjectId.Should().Be("{o}");
        drawing.RecognizedText.Should().Be("hello");
    }

    [Fact]
    public void Ink_words_nested_in_an_ink_paragraph_are_found_too()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{p}">
              <one:Outline objectID="{o}">
                <one:Position x="10" y="20"/>
                <one:Size width="100" height="30"/>
                <one:OEChildren><one:OE objectID="{oe}"><one:InkParagraph><one:InkWord objectID="{w}" recognizedText="nested"/></one:InkParagraph></one:OE></one:OEChildren>
              </one:Outline>
            </one:Page>
            """;

        PageXml page = PageXmlParser.Parse(xml);

        page.Outlines.Single().InkText.Should().Be("nested");
        page.Outlines.Single().Z.Should().Be(0, "a missing z reads as 0");
    }

    [Fact]
    public void Parses_what_the_builder_writes_so_anchors_can_be_found_again_by_rectangle()
    {
        PageXml page = PageXmlParser.Parse(MonJan4.Build(M));

        page.PageId.Should().Be(MonJan4.PageId);
        page.Title.Should().Be("Mon Jan 4");
        page.SfpKey.Should().Be("day:2027-01-04");
        page.SheetOutline!.Y.Should().Be(M.OneNote.TitleOffsetPt);
        page.Images.Single().IsBackground.Should().BeTrue();

        OutlineInfo date = page.OutlinesIn(M.Links.DailyDate.Rect).Should().ContainSingle().Which;
        date.PlainText.Should().Be("01 / 04 / 27");
        date.TextRuns[0].Html.Should().StartWith("<a href=\"onenote:");
        date.Z.Should().Be(1);
        page.OutlinesIn(M.Slots.StandingGoals.Rows[0]).Should().ContainSingle().Which.PlainText.Should().Be("Walk 30 min");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("<span style='font-weight:bold'>bold</span> and <i>italic</i>", "bold and italic")]
    [InlineData("line one<br>line two<br/>line three", "line one\nline two\nline three")]
    [InlineData("a&nbsp;b &amp; c &lt;d&gt;", "a b & c <d>")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Plain_text_strips_tags_keeps_line_breaks_and_decodes_entities(string? html, string expected)
    {
        PageXmlParser.PlainText(html).Should().Be(expected);
    }

    [Fact]
    public void Missing_position_and_size_read_as_zero_and_a_missing_title_reads_as_null()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Outline><one:OEChildren><one:OE><one:T><![CDATA[x]]></one:T></one:OE></one:OEChildren></one:Outline>
            </one:Page>
            """;

        PageXml page = PageXmlParser.Parse(xml);

        page.PageId.Should().BeEmpty();
        page.Title.Should().BeNull();
        page.SfpKey.Should().BeNull();
        page.SheetOutline.Should().BeNull();
        OutlineInfo outline = page.Outlines.Single();
        outline.Rect.Should().Be(new RectPt(0, 0, 0, 0));
        outline.TextRuns.Single().ObjectId.Should().BeEmpty();
        page.OutlinesIn(new RectPt(0, 0, 10, 10)).Should().BeEmpty("a zero-size rectangle overlaps nothing");
    }

    [Fact]
    public void Numbers_are_parsed_invariantly_and_the_2010_namespace_is_accepted()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2010/onenote" ID="{p}">
              <one:Outline><one:Position x="36.0" y="86.4000015258789" z="2"/><one:Size width="72.5" height="11"/></one:Outline>
            </one:Page>
            """;

        OutlineInfo outline = PageXmlParser.Parse(xml).Outlines.Single();

        outline.X.Should().Be(36);
        outline.Y.Should().BeApproximately(86.4, 1e-5);
        outline.Z.Should().Be(2);
        outline.Width.Should().Be(72.5);
    }

    [Fact]
    public void Malformed_xml_and_a_wrong_root_are_reported()
    {
        Action malformed = () => PageXmlParser.Parse("<one:Page xmlns:one=\"x\"><one:Outline></one:Page>");
        malformed.Should().Throw<XmlException>();

        Action wrongRoot = () => PageXmlParser.Parse("<one:Section xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"/>");
        wrongRoot.Should().Throw<FormatException>().WithMessage("*one:Page*Section*");
    }
}
