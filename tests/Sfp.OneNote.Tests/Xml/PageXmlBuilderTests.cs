using System.Globalization;
using System.Xml.Linq;
using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Xml;

public class PageXmlBuilderTests
{
    private static readonly Manifest M = TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    /// <summary>The blank Objective Details parent page, as the plan builder makes it.</summary>
    private static PageSpec ObjectiveDetails() =>
        new(PageKey.ObjectiveDetails, PageType.Blank, "Objective Details", 1, null, new DateOnly(2027, 1, 1), new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>What the renderer returns for a page without an image: zero bytes, 0 x 0, no anchors.</summary>
    private static RenderedPage NoImage() => new(PageKey.ObjectiveDetails, Array.Empty<byte>(), 0, 0, Array.Empty<AnchorPlacement>());

    [Fact]
    public void Mon_Jan_4_page_validates_against_the_2013_schema()
    {
        SchemaValidator.AssertValid(MonJan4.Build(M));
    }

    [Fact]
    public void Page_frame_carries_id_lang_date_time_key_meta_settings_and_title_in_schema_order()
    {
        XElement page = XElement.Parse(MonJan4.Build(M));

        page.Name.Should().Be(One + "Page");
        page.Attribute("ID")!.Value.Should().Be(MonJan4.PageId);
        page.Attribute("lang")!.Value.Should().Be("en-US");
        page.Attribute("dateTime")!.Value.Should().Be("2027-01-04T06:00:00-05:00", "the planner date at 06:00 in the fixed zone, ISO 8601 with offset");
        page.Attribute("lastModifiedTime").Should().BeNull();
        page.Elements().Select(e => e.Name.LocalName).Take(4).Should().Equal("Meta", "PageSettings", "Title", "Outline");

        XElement meta = page.Element(One + "Meta")!;
        meta.Attribute("name")!.Value.Should().Be("sfp.key");
        meta.Attribute("content")!.Value.Should().Be("day:2027-01-04");

        XElement settings = page.Element(One + "PageSettings")!;
        settings.Attribute("RTL")!.Value.Should().Be("false");
        settings.Attribute("color")!.Value.Should().Be(M.OneNote.PageColor);
        settings.Element(One + "PageSize")!.Element(One + "Automatic").Should().NotBeNull();
        settings.Element(One + "RuleLines")!.Attribute("visible")!.Value.Should().Be("false");

        page.Element(One + "Title")!.Element(One + "OE")!.Element(One + "T")!.Value.Should().Be("Mon Jan 4");
    }

    [Fact]
    public void Sheet_outline_sits_at_the_title_offset_with_the_placed_size_and_the_png_as_a_locked_background()
    {
        XElement page = XElement.Parse(MonJan4.Build(M));

        XElement sheet = page.Elements(One + "Outline").First();
        XElement position = sheet.Element(One + "Position")!;
        position.Attribute("x")!.Value.Should().Be("0");
        position.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(M.OneNote.TitleOffsetPt));
        position.Attribute("z")!.Value.Should().Be("0");
        XElement size = sheet.Element(One + "Size")!;
        size.Attribute("width")!.Value.Should().Be(OneNoteXml.Pt(M.PageWidthPt));
        size.Attribute("height")!.Value.Should().Be(OneNoteXml.Pt(M.PageHeightPt));
        size.Attribute("isSetByUser")!.Value.Should().Be("true");

        XElement image = sheet.Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "Image")!;
        image.Attribute("format")!.Value.Should().Be("png");
        image.Attribute("backgroundImage")!.Value.Should().Be("true");
        image.Element(One + "Size")!.Attribute("width")!.Value.Should().Be(OneNoteXml.Pt(M.PageWidthPt));
        image.Element(One + "Data")!.Value.Should().Be(Convert.ToBase64String(MonJan4.Png));
    }

    [Fact]
    public void A_page_without_an_image_is_written_as_the_frame_alone_with_no_image_and_validates()
    {
        string xml = PageXmlBuilder.Build("{objectives}", ObjectiveDetails(), NoImage(), Array.Empty<ResolvedAnchor>(), MonJan4.Options(M));

        SchemaValidator.AssertValid(xml);
        XElement page = XElement.Parse(xml);
        page.Elements().Select(e => e.Name.LocalName).Should().Equal("Meta", "PageSettings", "Title");
        page.Descendants(One + "Image").Should().BeEmpty("OneNote refuses an image without data");
        page.Descendants(One + "Data").Should().BeEmpty();
        page.Descendants(One + "Outline").Should().BeEmpty("there is no sheet to place");
        page.Element(One + "Meta")!.Attribute("content")!.Value.Should().Be("objectives", "the page key still lets Refresh find the page");
        page.Element(One + "Title")!.Element(One + "OE")!.Element(One + "T")!.Value.Should().Be("Objective Details");
        page.Attribute("dateTime")!.Value.Should().Be("2027-01-01T06:00:00-05:00");
    }

    [Fact]
    public void A_page_without_an_image_keeps_its_anchors_where_a_sheet_page_puts_them()
    {
        IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) };
        RenderedPage art = NoImage() with { Anchors = anchors.Select(a => a.Placement).ToList() };

        XElement frameOnly = XElement.Parse(PageXmlBuilder.Build("{objectives}", ObjectiveDetails(), art, anchors, MonJan4.Options(M)));
        XElement withSheet = XElement.Parse(MonJan4.Build(M));

        frameOnly.Descendants(One + "Image").Should().BeEmpty();
        XElement anchor = frameOnly.Elements(One + "Outline").Should().ContainSingle().Subject;
        anchor.Element(One + "Position")!.ToString().Should().Be(withSheet.Elements(One + "Outline").ElementAt(1).Element(One + "Position")!.ToString());
        SchemaValidator.AssertValid(frameOnly.ToString());
    }

    [Fact]
    public void A_page_without_an_image_and_a_hidden_title_is_the_meta_and_page_settings_alone()
    {
        PageXmlOptions hidden = MonJan4.Options(M, MonJan4.Generate() with { HideTitle = true });

        string xml = PageXmlBuilder.Build("{objectives}", ObjectiveDetails(), NoImage(), Array.Empty<ResolvedAnchor>(), hidden);

        XElement.Parse(xml).Elements().Select(e => e.Name.LocalName).Should().Equal("Meta", "PageSettings");
        SchemaValidator.AssertValid(xml);
    }

    [Fact]
    public void A_sheet_outline_is_refused_for_art_without_image_bytes()
    {
        PageXmlBuilder.HasSheet(NoImage()).Should().BeFalse();
        PageXmlBuilder.HasSheet(MonJan4.Art(Array.Empty<ResolvedAnchor>())).Should().BeTrue();

        Action act = () => PageXmlBuilder.SheetOutline(NoImage(), MonJan4.Options(M));

        act.Should().Throw<ArgumentException>().WithMessage("*objectives*no image*");
    }

    [Fact]
    public void Hide_title_places_the_sheet_at_y_zero_omits_the_title_and_drops_the_offset_from_anchors()
    {
        PageXmlOptions options = MonJan4.Options(M, MonJan4.Generate() with { HideTitle = true });
        string xml = MonJan4.Build(M, options);
        XElement page = XElement.Parse(xml);

        page.Element(One + "Title").Should().BeNull();
        List<XElement> outlines = page.Elements(One + "Outline").ToList();
        outlines[0].Element(One + "Position")!.Attribute("y")!.Value.Should().Be("0");
        double expectedDateY = M.Links.DailyDate.Y + M.OneNote.AnchorPaddingPt.Y;
        outlines[1].Element(One + "Position")!.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(expectedDateY));
        SchemaValidator.AssertValid(xml);
    }

    [Fact]
    public void Link_anchor_outline_has_offset_position_explicit_size_alignment_and_a_styled_href()
    {
        XElement page = XElement.Parse(MonJan4.Build(M));
        XElement date = page.Elements(One + "Outline").ElementAt(1);

        XElement position = date.Element(One + "Position")!;
        position.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.Rect.X + M.OneNote.AnchorPaddingPt.X));
        position.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.Y + M.OneNote.TitleOffsetPt + M.OneNote.AnchorPaddingPt.Y));
        position.Attribute("z")!.Value.Should().Be("1");
        XElement size = date.Element(One + "Size")!;
        size.Attribute("width")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.W));
        size.Attribute("height")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.H));
        size.Attribute("isSetByUser")!.Value.Should().Be("true");

        XElement oe = date.Element(One + "OEChildren")!.Element(One + "OE")!;
        oe.Attribute("alignment")!.Value.Should().Be("right");
        string html = oe.Element(One + "T")!.Value;
        html.Should().Be(
            "<a href=\"" + MonJan4.OverviewHref.Replace("&", "&amp;", StringComparison.Ordinal) + "\">"
            + "<span style='font-family:Bookman Old Style;font-size:15.0pt;color:#1E1E20;text-decoration:none'>01 / 04 / 27</span></a>");
    }

    [Fact]
    public void Prev_and_next_anchors_are_left_aligned_ten_point_links()
    {
        XElement page = XElement.Parse(MonJan4.Build(M));
        List<XElement> outlines = page.Elements(One + "Outline").ToList();

        XElement prev = outlines[2];
        prev.Element(One + "Position")!.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt(M.Links.Prev.X + M.OneNote.AnchorPaddingPt.X));
        prev.Element(One + "OEChildren")!.Element(One + "OE")!.Attribute("alignment")!.Value.Should().Be("left");
        prev.Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value
            .Should().Contain("font-size:10.0pt").And.Contain("‹ Sun Jan 3").And.Contain("Sun%20Jan%203");
        XElement next = outlines[3];
        next.Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value.Should().Contain("Tue Jan 5 ›");
    }

    [Fact]
    public void Anchor_underline_option_leaves_the_text_decoration_to_onenote()
    {
        PageXmlOptions options = MonJan4.Options(M) with { OneNote = M.OneNote with { AnchorUnderline = true } };

        string html = PageXmlBuilder.AnchorHtml(MonJan4.DateAnchor(M), options);

        html.Should().NotContain("text-decoration");
        html.Should().Contain("color:#1E1E20");
    }

    [Fact]
    public void Propagated_anchor_has_no_link_and_its_color_is_the_ink_blended_toward_the_background()
    {
        XElement page = XElement.Parse(MonJan4.Build(M));
        List<XElement> outlines = page.Elements(One + "Outline").ToList();

        string legend = outlines[4].Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value;
        legend.Should().NotContain("<a ");
        legend.Should().Be("<span style='font-family:Bookman Old Style;font-size:8.0pt;color:#1E1E20'>O1 Ship the connector  O2 Hire two PMs</span>", "opacity 1 keeps the ink");

        string goal = outlines[5].Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value;
        goal.Should().Be("<span style='font-family:Bookman Old Style;font-size:10.0pt;color:#6B6763'>Walk 30 min</span>", "ink at 60% over #DED4C8 is #6B6763");
        outlines[5].Element(One + "Position")!.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(M.Slots.StandingGoals.Rows[0].Y + M.OneNote.TitleOffsetPt + M.OneNote.AnchorPaddingPt.Y));
    }

    [Fact]
    public void A_link_anchor_without_an_href_is_written_as_plain_styled_text()
    {
        ResolvedAnchor unresolved = MonJan4.DateAnchor(M) with { Href = "" };

        string html = PageXmlBuilder.AnchorHtml(unresolved, MonJan4.Options(M));

        html.Should().StartWith("<span").And.NotContain("<a ");
    }

    [Fact]
    public void Page_scale_shrinks_the_sheet_the_anchors_and_their_font_sizes_together()
    {
        PageXmlOptions options = MonJan4.Options(M, MonJan4.Generate() with { PageScale = 0.5 });
        XElement page = XElement.Parse(MonJan4.Build(M, options));
        List<XElement> outlines = page.Elements(One + "Outline").ToList();

        outlines[0].Element(One + "Size")!.Attribute("width")!.Value.Should().Be(OneNoteXml.Pt(M.PageWidthPt / 2));
        outlines[0].Element(One + "Size")!.Attribute("height")!.Value.Should().Be(OneNoteXml.Pt(M.PageHeightPt / 2));
        XElement date = outlines[1];
        date.Element(One + "Position")!.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt((M.Links.DailyDate.Rect.X / 2) + M.OneNote.AnchorPaddingPt.X));
        date.Element(One + "Size")!.Attribute("width")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.W / 2));
        date.Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value.Should().Contain("font-size:7.5pt");
    }

    [Fact]
    public void Custom_page_size_writes_the_manifest_page_dimensions()
    {
        PageXmlOptions options = MonJan4.Options(M, MonJan4.Generate() with { PageSize = PageSizeMode.Custom });
        string xml = MonJan4.Build(M, options);
        XElement page = XElement.Parse(xml);

        XElement dimensions = page.Element(One + "PageSettings")!.Element(One + "PageSize")!.Element(One + "Dimensions")!;
        dimensions.Element(One + "Width")!.Attribute("value")!.Value.Should().Be(OneNoteXml.Pt(M.PageWidthPt));
        dimensions.Element(One + "Height")!.Attribute("value")!.Value.Should().Be(OneNoteXml.Pt(M.PageHeightPt));
        SchemaValidator.AssertValid(xml);
    }

    [Fact]
    public void Page_dates_off_or_a_page_without_a_date_omits_date_time()
    {
        PageXmlOptions noDates = MonJan4.Options(M, MonJan4.Generate() with { SetPageDates = false });
        XElement.Parse(MonJan4.Build(M, noDates)).Attribute("dateTime").Should().BeNull();

        PageSpec undated = MonJan4.Page() with { Date = null };
        string xml = PageXmlBuilder.Build(MonJan4.PageId, undated, MonJan4.Art(Array.Empty<ResolvedAnchor>()), Array.Empty<ResolvedAnchor>(), MonJan4.Options(M));
        XElement.Parse(xml).Attribute("dateTime").Should().BeNull();
    }

    [Fact]
    public void Page_date_time_follows_the_zone_offset_on_that_date()
    {
        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test UTC+2", TimeSpan.FromHours(2), "Test UTC+2", "Test UTC+2");
        PageXmlOptions options = MonJan4.Options(M) with { TimeZone = plusTwo, OneNote = M.OneNote with { PageDateTimeHour = 7 } };

        PageXmlBuilder.PageDateTime(new DateOnly(2027, 3, 31), options).Should().Be("2027-03-31T07:00:00+02:00");
    }

    [Theory]
    [InlineData("R&D", "R&amp;D")]
    [InlineData("<Ideas>", "&lt;Ideas&gt;")]
    [InlineData("Q&A ]]> <notes>", "Q&amp;A ]]&gt; &lt;notes&gt;")]
    [InlineData("Mon Jan 4", "Mon Jan 4")]
    public void The_title_is_html_escaped_inside_its_cdata_and_reads_back_as_typed(string title, string html)
    {
        string xml = PageXmlBuilder.Build(MonJan4.PageId, MonJan4.Page(title), MonJan4.Art(Array.Empty<ResolvedAnchor>()), Array.Empty<ResolvedAnchor>(), MonJan4.Options(M));

        SchemaValidator.AssertValid(xml);
        string raw = XElement.Parse(xml).Element(One + "Title")!.Element(One + "OE")!.Element(One + "T")!.Value;
        raw.Should().Be(html, "one:T holds HTML, so the title is escaped like anchor text");
        PageXmlParser.PlainText(raw).Should().Be(title);
        PageXmlParser.Parse(xml).Title.Should().Be(title);
    }

    [Fact]
    public void Title_and_anchor_text_with_ampersand_and_cdata_terminator_survive_a_round_trip_and_validate()
    {
        const string title = "Q&A ]]> <notes>";
        IReadOnlyList<ResolvedAnchor> anchors = new[]
        {
            MonJan4.DateAnchor(M) with { Placement = MonJan4.DateAnchor(M).Placement with { Text = "a & b ]]> c" } },
        };
        string xml = PageXmlBuilder.Build(MonJan4.PageId, MonJan4.Page(title), MonJan4.Art(anchors), anchors, MonJan4.Options(M));

        SchemaValidator.AssertValid(xml);
        XElement page = XElement.Parse(xml);
        PageXmlParser.PlainText(page.Element(One + "Title")!.Element(One + "OE")!.Element(One + "T")!.Value).Should().Be(title);
        string html = page.Elements(One + "Outline").ElementAt(1).Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "T")!.Value;
        html.Should().Contain(">a &amp; b ]]&gt; c</span>", "anchor text is HTML-escaped inside the CDATA");
        html.Should().Contain("&amp;section-id=", "the href's ampersands are HTML-escaped");
        PageXmlParser.PlainText(html).Should().Be("a & b ]]> c");
    }

    [Fact]
    public void Manifest_anchor_padding_offsets_every_anchor_position()
    {
        PageXmlOptions options = MonJan4.Options(M) with { OneNote = M.OneNote with { AnchorPaddingPt = new PointPt(1.5, -2) } };

        XElement outline = PageXmlBuilder.AnchorOutline(MonJan4.DateAnchor(M), options);

        outline.Element(One + "Position")!.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.Rect.X + 1.5));
        outline.Element(One + "Position")!.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(M.Links.DailyDate.Y + M.OneNote.TitleOffsetPt - 2));
    }

    [Fact]
    public void Numbers_are_written_with_a_period_and_at_most_three_decimals_whatever_the_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            AnchorPlacement p = new("calendarDay:2027-01-04", 28.8f, 121.2f, 30.3f, 14f, "4", TextAlign.Center, 10f);
            XElement outline = PageXmlBuilder.AnchorOutline(new ResolvedAnchor(p, MonJan4.OverviewHref, true), MonJan4.Options(M));

            outline.Element(One + "Position")!.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt(28.8 + M.OneNote.AnchorPaddingPt.X));
            outline.Element(One + "Size")!.Attribute("width")!.Value.Should().Be("30.3");
            outline.Element(One + "OEChildren")!.Element(One + "OE")!.Attribute("alignment")!.Value.Should().Be("center");
            OneNoteXml.Pt(1112.0 * 0.72).Should().Be("800.64");
            OneNoteXml.Pt(-0.0).Should().Be("0");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Options_from_manifest_and_generate_options_pick_up_every_switch()
    {
        GenerateOptions generate = MonJan4.Generate() with { HideTitle = true, PageScale = 0.72, PageSize = PageSizeMode.Custom, SetPageDates = false };

        PageXmlOptions options = PageXmlOptions.From(M, generate, "TeX Gyre Bonum", MonJan4.FixedZone);

        options.HideTitle.Should().BeTrue();
        options.TitleOffsetPt.Should().Be(0);
        options.PageScale.Should().Be(0.72);
        options.PageSize.Should().Be(PageSizeMode.Custom);
        options.SetPageDates.Should().BeFalse();
        options.FontFamily.Should().Be("TeX Gyre Bonum");
        options.InkHex.Should().Be(M.Colors.Ink);
        options.BackgroundHex.Should().Be(M.Colors.Background);
        options.SheetWidthPt.Should().Be(M.PageWidthPt);
        options.PlacedWidthPt.Should().BeApproximately(M.PageWidthPt * 0.72, 1e-9);
        options.OneNote.Should().BeSameAs(M.OneNote);
    }
}
