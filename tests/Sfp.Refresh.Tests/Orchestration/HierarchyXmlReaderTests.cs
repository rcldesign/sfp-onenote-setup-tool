using Sfp.Refresh.Orchestration;

namespace Sfp.Refresh.Tests.Orchestration;

public class HierarchyXmlReaderTests
{
    private const string Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    [Fact]
    public void Reads_notebooks_sections_section_groups_and_pages_in_document_order()
    {
        string xml = $$"""
            <one:Notebooks xmlns:one="{{Ns}}">
              <one:Notebook name="Personal" ID="{nb1}" path="C:\Personal"/>
              <one:Notebook name="SFP Q1 2027" ID="{nb2}" path="C:\Notebooks\SFP Q1 2027">
                <one:Section name="Quarter" ID="{s1}" path="C:\Notebooks\SFP Q1 2027\Quarter.one">
                  <one:Page ID="{p1}" name="Cover" pageLevel="1"/>
                  <one:Page ID="{p2}" name="Contents" pageLevel="1"/>
                </one:Section>
                <one:SectionGroup name="Weeks" ID="{g1}">
                  <one:Section name="Week 01 (Jan 1 to Jan 3)" ID="{s2}" path="x">
                    <one:Page ID="{p3}" name="Weekly Overview" pageLevel="1"/>
                    <one:Page ID="{p4}" name="Fri Jan 1" pageLevel="2"/>
                  </one:Section>
                </one:SectionGroup>
                <one:Section name="Notes" ID="{s3}" path="y"/>
              </one:Notebook>
            </one:Notebooks>
            """;

        IReadOnlyList<HierarchyNotebook> notebooks = HierarchyXmlReader.Notebooks(xml);

        notebooks.Should().HaveCount(2);
        notebooks[0].Should().BeEquivalentTo(new HierarchyNotebook("{nb1}", "Personal", "C:\\Personal", Array.Empty<HierarchySection>()));
        HierarchyNotebook sfp = notebooks[1];
        sfp.Sections.Select(s => s.Name).Should().Equal("Quarter", "Week 01 (Jan 1 to Jan 3)", "Notes");
        sfp.Sections[0].Pages.Should().Equal(new HierarchyPage("{p1}", "Cover", 1), new HierarchyPage("{p2}", "Contents", 1));
        sfp.Sections[1].Pages[1].Should().Be(new HierarchyPage("{p4}", "Fri Jan 1", 2));
        sfp.Pages.Should().HaveCount(4);
        sfp.Pages.Last().Section.Id.Should().Be("{s2}");
    }

    [Fact]
    public void A_single_notebook_or_section_root_is_accepted_and_other_roots_are_not()
    {
        string notebook = $"<one:Notebook xmlns:one=\"{Ns}\" name=\"N\" ID=\"{{nb}}\"><one:Section name=\"S\" ID=\"{{s}}\"/></one:Notebook>";
        string section = $"<one:Section xmlns:one=\"{Ns}\" name=\"S\" ID=\"{{s}}\"><one:Page ID=\"{{p}}\" name=\"P\"/></one:Section>";
        string page = $"<one:Page xmlns:one=\"{Ns}\" ID=\"{{p}}\" name=\"P\"/>";

        HierarchyXmlReader.Notebooks(notebook).Should().ContainSingle().Which.Sections.Should().ContainSingle();
        HierarchyXmlReader.Notebook(notebook).Id.Should().Be("{nb}");
        HierarchyXmlReader.Section(section).Pages.Should().ContainSingle().Which.Level.Should().Be(1, "a missing pageLevel reads as 1");
        HierarchyXmlReader.RootId(page).Should().Be("{p}");

        Action wrong = () => HierarchyXmlReader.Notebook(section);
        wrong.Should().Throw<FormatException>().WithMessage("*one:Notebook*Section*");
        Action wrongList = () => HierarchyXmlReader.Notebooks(page);
        wrongList.Should().Throw<FormatException>();
    }
}
