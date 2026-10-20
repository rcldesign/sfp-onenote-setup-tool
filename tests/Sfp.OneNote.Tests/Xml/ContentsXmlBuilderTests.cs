using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

public class ContentsXmlBuilderTests
{
    private static readonly Manifest M = TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    private static IReadOnlyList<ContentsSectionEntry> Sections() => new[]
    {
        new ContentsSectionEntry("Quarter", "onenote:Quarter.one#section-id={s1}&end", new[]
        {
            new ContentsPageEntry("Cover", "onenote:#Cover&section-id={s1}&page-id={p1}&end", 1),
            new ContentsPageEntry("Contents", "onenote:#Contents&section-id={s1}&page-id={p2}&end", 1),
            new ContentsPageEntry("Setup", "onenote:#Setup&section-id={s1}&page-id={p3}&end", 1),
        }),
        new ContentsSectionEntry("Objectives", "onenote:Objectives.one#section-id={s2}&end", new[]
        {
            new ContentsPageEntry("Annual Objectives", "onenote:#a1&section-id={s2}&page-id={p4}&end", 1),
            new ContentsPageEntry("Annual Objectives, continued", "onenote:#a2&section-id={s2}&page-id={p5}&end", 2),
            new ContentsPageEntry("Objective Details", "onenote:#od&section-id={s2}&page-id={p6}&end", 1),
            new ContentsPageEntry("Objective 1", "onenote:#o1&section-id={s2}&page-id={p7}&end", 2),
            new ContentsPageEntry("Objective 2", "onenote:#o2&section-id={s2}&page-id={p8}&end", 2),
        }),
        new ContentsSectionEntry("Week 02 (Jan 4 to Jan 10)", "onenote:Week%2002.one#section-id={s3}&end", new[]
        {
            new ContentsPageEntry("Weekly Overview", "onenote:#wo&section-id={s3}&page-id={p9}&end", 1),
            new ContentsPageEntry("Mon Jan 4", "onenote:#Mon%20Jan%204&section-id={s3}&page-id={p10}&end", 2),
            new ContentsPageEntry("Tue Jan 5", "onenote:#Tue%20Jan%205&section-id={s3}&page-id={p11}&end", 2),
            new ContentsPageEntry("Weekly Reflection", "onenote:#wr&section-id={s3}&page-id={p12}&end", 1),
        }),
        new ContentsSectionEntry("Notes", "onenote:Notes.one#section-id={s4}&end", Array.Empty<ContentsPageEntry>()),
    };

    [Fact]
    public void Outline_sits_at_the_manifest_contents_position_with_one_link_per_section()
    {
        XElement outline = ContentsXmlBuilder.OutlineElement(Sections(), MonJan4.Options(M));

        XElement position = outline.Element(One + "Position")!;
        position.Attribute("x")!.Value.Should().Be(OneNoteXml.Pt(M.OneNote.ContentsOutline.X));
        position.Attribute("y")!.Value.Should().Be(OneNoteXml.Pt(M.OneNote.ContentsOutline.Y));
        position.Attribute("z")!.Value.Should().Be("0");
        outline.Element(One + "Size").Should().BeNull("OneNote sizes the native outline itself");

        List<XElement> sectionOes = outline.Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        sectionOes.Should().HaveCount(4);
        sectionOes[0].Element(One + "T")!.Value.Should().Be("<a href=\"onenote:Quarter.one#section-id={s1}&amp;end\">Quarter</a>");
        sectionOes[3].Element(One + "OEChildren").Should().BeNull("a section without pages has no nested list");
    }

    [Fact]
    public void Page_links_nest_under_their_section_and_level_two_pages_nest_under_the_level_one_page_before_them()
    {
        XElement outline = ContentsXmlBuilder.OutlineElement(Sections(), MonJan4.Options(M));
        List<XElement> sectionOes = outline.Element(One + "OEChildren")!.Elements(One + "OE").ToList();

        List<XElement> quarterPages = sectionOes[0].Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        quarterPages.Select(Text).Should().Equal("Cover", "Contents", "Setup");
        quarterPages.Should().OnlyContain(p => p.Element(One + "OEChildren") == null);

        List<XElement> objectivePages = sectionOes[1].Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        objectivePages.Select(Text).Should().Equal("Annual Objectives", "Objective Details");
        objectivePages[0].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Annual Objectives, continued");
        objectivePages[1].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Objective 1", "Objective 2");

        List<XElement> weekPages = sectionOes[2].Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        weekPages.Select(Text).Should().Equal("Weekly Overview", "Weekly Reflection");
        weekPages[0].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Mon Jan 4", "Tue Jan 5");
        weekPages[0].Element(One + "OEChildren")!.Elements(One + "OE").First().Element(One + "T")!.Value
            .Should().Be("<a href=\"onenote:#Mon%20Jan%204&amp;section-id={s3}&amp;page-id={p10}&amp;end\">Mon Jan 4</a>");
    }

    [Fact]
    public void Contents_page_is_a_native_page_with_the_frame_and_no_sheet_image_and_validates()
    {
        PageSpec contents = new(PageKey.Contents, PageType.Contents, "Contents", 1, null, new DateOnly(2027, 1, 1), new Dictionary<string, string>(StringComparer.Ordinal));

        string xml = ContentsXmlBuilder.Page("{contents}", contents, Sections(), MonJan4.Options(M));

        SchemaValidator.AssertValid(xml);
        XElement page = XElement.Parse(xml);
        page.Attribute("ID")!.Value.Should().Be("{contents}");
        page.Element(One + "Meta")!.Attribute("content")!.Value.Should().Be("contents");
        page.Element(One + "Title")!.Value.Should().Be("Contents");
        page.Descendants(One + "Image").Should().BeEmpty();
        page.Elements(One + "Outline").Should().ContainSingle();
        SchemaValidator.AssertValid(ContentsXmlBuilder.Outline(Sections(), MonJan4.Options(M)));
    }

    [Fact]
    public void Grouped_sections_are_listed_under_a_plain_text_heading_with_the_group_name_where_the_first_of_them_would_be()
    {
        ContentsPageEntry overview = new("Weekly Overview", "onenote:#wo&section-id={s}&page-id={p}&end", 1);
        IReadOnlyList<ContentsSectionEntry> sections = new[]
        {
            new ContentsSectionEntry("Calendar", "onenote:Calendar.one#section-id={c}&end", Array.Empty<ContentsPageEntry>()),
            new ContentsSectionEntry("Week 01 (Jan 1 to Jan 3)", "onenote:Week%2001.one#section-id={w1}&end", new[] { overview }) { GroupName = "Weeks" },
            new ContentsSectionEntry("Week 02 (Jan 4 to Jan 10)", "onenote:Week%2002.one#section-id={w2}&end", new[] { overview }) { GroupName = "Weeks" },
            new ContentsSectionEntry("Reviews", "onenote:Reviews.one#section-id={r}&end", Array.Empty<ContentsPageEntry>()),
            new ContentsSectionEntry("Week 03 (Jan 11 to Jan 17)", "onenote:Week%2003.one#section-id={w3}&end", Array.Empty<ContentsPageEntry>()) { GroupName = "Weeks" },
        };

        XElement outline = ContentsXmlBuilder.OutlineElement(sections, MonJan4.Options(M));

        List<XElement> top = outline.Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        top.Select(Text).Should().Equal("Calendar", "Weeks", "Reviews");
        XElement heading = top[1];
        heading.Element(One + "T")!.Value.Should().Be("Weeks", "the group heading is plain text, not a link");
        List<XElement> grouped = heading.Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        grouped.Select(Text).Should().Equal("Week 01 (Jan 1 to Jan 3)", "Week 02 (Jan 4 to Jan 10)", "Week 03 (Jan 11 to Jan 17)");
        grouped[0].Element(One + "T")!.Value.Should().StartWith("<a href=\"onenote:Week%2001.one");
        grouped[0].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Weekly Overview");
        PageSpec contents = new(PageKey.Contents, PageType.Contents, "Contents", 1, null, new DateOnly(2027, 1, 1), new Dictionary<string, string>(StringComparer.Ordinal));
        SchemaValidator.AssertValid(ContentsXmlBuilder.Page("{contents}", contents, sections, MonJan4.Options(M)));
    }

    [Fact]
    public void A_level_two_page_with_no_parent_before_it_is_listed_at_the_top_level()
    {
        IReadOnlyList<ContentsSectionEntry> sections = new[]
        {
            new ContentsSectionEntry("Odd", "onenote:Odd.one#section-id={s}&end", new[] { new ContentsPageEntry("Orphan", "onenote:#o&section-id={s}&page-id={p}&end", 2) }),
        };

        XElement outline = ContentsXmlBuilder.OutlineElement(sections, MonJan4.Options(M));

        outline.Element(One + "OEChildren")!.Element(One + "OE")!.Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Orphan");
    }

    private static string Text(XElement oe) => PageXmlParser.PlainText(oe.Element(One + "T")!.Value);
}
