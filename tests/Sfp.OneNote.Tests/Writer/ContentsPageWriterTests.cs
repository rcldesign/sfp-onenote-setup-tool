using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>M3.7: the Contents entries built from the plan and the gathered hyperlinks.</summary>
public class ContentsPageWriterTests
{
    private static readonly Manifest M = TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    private static (IReadOnlyDictionary<string, string> Sections, IReadOnlyDictionary<PageKey, string> Pages) Hrefs(NotebookPlan plan)
    {
        Dictionary<string, string> sections = plan.Sections.ToDictionary(s => s.Name, s => "onenote:" + Uri.EscapeDataString(s.Name) + ".one#section-id={s}&end", StringComparer.Ordinal);
        Dictionary<PageKey, string> pages = plan.Pages.ToDictionary(p => p.Key, p => "onenote:#" + Uri.EscapeDataString(p.Title) + "&section-id={s}&page-id={" + p.Key.Value + "}&end");
        return (sections, pages);
    }

    [Fact]
    public void Entries_follow_the_plan_order_with_levels_and_leave_the_contents_page_out_of_its_own_list()
    {
        NotebookPlan plan = TestPlans.Small();
        (IReadOnlyDictionary<string, string> sectionHrefs, IReadOnlyDictionary<PageKey, string> pageHrefs) = Hrefs(plan);

        IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, sectionHrefs, pageHrefs);

        entries.Select(e => e.Name).Should().Equal("Quarter", TestPlans.WeekSectionName);
        entries[0].Href.Should().Be(sectionHrefs["Quarter"]);
        entries[0].Pages.Select(p => p.Title).Should().Equal("Cover", "Setup");
        entries[1].Pages.Select(p => (p.Title, p.Level)).Should().Equal(
            ("Weekly Overview", 1), ("Mon Jan 4", 2), ("Tue Jan 5", 2), ("Weekly Reflection", 1));
        entries[1].Pages[1].Href.Should().Be(pageHrefs[TestPlans.Monday]);
    }

    [Fact]
    public void Entries_carry_the_section_group_of_each_section()
    {
        NotebookPlan small = TestPlans.Small();
        NotebookPlan plan = small with { Sections = new[] { small.Sections[0], small.Sections[1] with { GroupName = "Weeks" } } };
        (IReadOnlyDictionary<string, string> sectionHrefs, IReadOnlyDictionary<PageKey, string> pageHrefs) = Hrefs(plan);

        IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, sectionHrefs, pageHrefs);

        entries.Select(e => e.GroupName).Should().Equal(null, "Weeks");
        ContentsPageWriter.Entries(small, sectionHrefs, pageHrefs).Should().OnlyContain(e => e.GroupName == null);
    }

    [Fact]
    public void Missing_hrefs_become_empty_so_the_builder_writes_plain_text_instead_of_a_broken_link()
    {
        NotebookPlan plan = TestPlans.Small();

        IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<PageKey, string>());

        entries.Should().AllSatisfy(e => e.Href.Should().BeEmpty());
        entries.SelectMany(e => e.Pages).Should().AllSatisfy(p => p.Href.Should().BeEmpty());
    }

    [Fact]
    public void Entries_nest_day_pages_under_the_overview_in_the_page_xml_and_the_page_validates()
    {
        NotebookPlan plan = TestPlans.Small();
        (IReadOnlyDictionary<string, string> sectionHrefs, IReadOnlyDictionary<PageKey, string> pageHrefs) = Hrefs(plan);
        IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, sectionHrefs, pageHrefs);

        string xml = ContentsXmlBuilder.Page(MonJan4.ContentsPageId, plan.Page(PageKey.Contents), entries, MonJan4.Options(M));

        SchemaValidator.AssertValid(xml);
        XElement outline = XElement.Parse(xml).Element(One + "Outline")!;
        List<XElement> sections = outline.Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        sections.Should().HaveCount(2);
        List<XElement> weekPages = sections[1].Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        weekPages.Select(Text).Should().Equal("Weekly Overview", "Weekly Reflection");
        weekPages[0].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal("Mon Jan 4", "Tue Jan 5");
    }

    [Fact]
    public void Write_hands_the_entries_to_the_writer()
    {
        NotebookPlan plan = TestPlans.Small();
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
        SectionHandle section = writer.CreateSection(nb, plan.Sections[0]);
        PageSpec spec = plan.Page(PageKey.Contents);
        PageHandle contents = writer.CreatePage(section, spec);
        (IReadOnlyDictionary<string, string> sectionHrefs, IReadOnlyDictionary<PageKey, string> pageHrefs) = Hrefs(plan);
        IReadOnlyList<ContentsSectionEntry> entries = ContentsPageWriter.Entries(plan, sectionHrefs, pageHrefs);

        ContentsPageWriter.Write(writer, contents, spec, entries);

        writer.Contents.Should().NotBeNull();
        writer.Contents!.Value.Page.Should().BeSameAs(contents);
        writer.Contents.Value.Sections.Should().BeSameAs(entries);
        writer.Calls.Last().Should().Be("WriteContentsPage:contents");
    }

    private static string Text(XElement oe) => PageXmlParser.PlainText(oe.Element(One + "T")!.Value);
}
