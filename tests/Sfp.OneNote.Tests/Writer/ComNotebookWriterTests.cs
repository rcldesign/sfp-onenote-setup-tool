using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>M3.3 to M3.6: the COM writer's calls, driven against the in-memory OneNote.</summary>
public class ComNotebookWriterTests
{
    private static readonly Manifest M = TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    private static (ComNotebookWriter Writer, FakeOneNoteApp App, CapturingSink Log) Create(PageXmlOptions? options = null)
    {
        FakeOneNoteApp app = new();
        CapturingSink sink = new();
        ComNotebookWriter writer = new(app, options ?? MonJan4.Options(M), sink.CreateLogger());
        return (writer, app, sink);
    }

    [Fact]
    public void Create_notebook_opens_the_folder_path_as_a_notebook_and_reads_its_id_path_and_name_back()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");

            app.CallsOf("OpenHierarchy").Should().Equal(Path.Combine("/out", "SFP Q1 2027") + ":Notebook");
            app.CallsOf("GetHierarchy").Should().Equal(nb.Id + ":Self");
            nb.Id.Should().StartWith("{nb:");
            nb.Path.Should().Be(Path.Combine("/out", "SFP Q1 2027"));
            nb.Name.Should().Be("SFP Q1 2027");
        }
    }

    [Fact]
    public void Create_section_opens_a_one_file_in_the_notebook_in_plan_order_and_writes_its_color()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle quarter = writer.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            SectionHandle week = writer.CreateSection(nb, new SectionSpec("Week 02 (Jan 4 to Jan 10)", "#F0E6D8", Array.Empty<PageSpec>()));
            SectionHandle odd = writer.CreateSection(nb, new SectionSpec("A/B:C?", null, Array.Empty<PageSpec>()));

            app.CallsOf("OpenHierarchy").Skip(1).Should().Equal(
                Path.Combine(nb.Path, "Quarter.one") + ":Section",
                Path.Combine(nb.Path, "Week 02 (Jan 4 to Jan 10).one") + ":Section",
                Path.Combine(nb.Path, "A-B-C-.one") + ":Section");
            app.SectionNames(nb.Id).Should().Equal("Quarter", "Week 02 (Jan 4 to Jan 10)", "A-B-C-");
            quarter.Notebook.Should().BeSameAs(nb);
            quarter.ColorHex.Should().BeNull();
            week.ColorHex.Should().Be("#F0E6D8");
            app.SectionColor(week.Id).Should().Be("#F0E6D8", "the color is applied through UpdateHierarchy at creation");
            app.SectionColor(quarter.Id).Should().BeNull();
            app.CallsOf("UpdateHierarchy").Should().HaveCount(1, "only the colored section needs a hierarchy write at creation");
            odd.Name.Should().Be("A-B-C-");
        }
    }

    [Fact]
    public void A_section_group_is_a_folder_in_the_notebook_and_its_sections_are_opened_inside_that_folder()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, CapturingSink log) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle quarter = writer.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            SectionGroupHandle weeks = writer.CreateSectionGroup(nb, "Weeks");
            SectionHandle week1 = writer.CreateSection(nb, new SectionSpec("Week 01 (Jan 1 to Jan 3)", "#F0E6D8", Array.Empty<PageSpec>()) { GroupName = "Weeks" }, weeks);
            SectionHandle week2 = writer.CreateSection(nb, new SectionSpec("Week 02 (Jan 4 to Jan 10)", "#F0E6D8", Array.Empty<PageSpec>()) { GroupName = "Weeks" }, weeks);
            SectionHandle notes = writer.CreateSection(nb, new SectionSpec("Notes", null, Array.Empty<PageSpec>()), null);
            SectionGroupHandle odd = writer.CreateSectionGroup(nb, "A/B");

            app.CallsOf("OpenHierarchy").Skip(1).Should().Equal(
                Path.Combine(nb.Path, "Quarter.one") + ":Section",
                Path.Combine(nb.Path, "Weeks") + ":Folder",
                Path.Combine(nb.Path, "Weeks", "Week 01 (Jan 1 to Jan 3).one") + ":Section",
                Path.Combine(nb.Path, "Weeks", "Week 02 (Jan 4 to Jan 10).one") + ":Section",
                Path.Combine(nb.Path, "Notes.one") + ":Section",
                Path.Combine(nb.Path, "A-B") + ":Folder");
            weeks.Id.Should().StartWith("{sg:");
            weeks.Name.Should().Be("Weeks");
            weeks.Path.Should().Be(Path.Combine(nb.Path, "Weeks"));
            week1.Group.Should().BeSameAs(weeks);
            week1.Notebook.Should().BeSameAs(nb);
            week1.ColorHex.Should().Be("#F0E6D8");
            app.SectionColor(week1.Id).Should().Be("#F0E6D8", "a grouped section gets its color like any other");
            quarter.Group.Should().BeNull();
            notes.Group.Should().BeNull();
            odd.Name.Should().Be("A-B");
            log.Messages.Should().Contain(m => m.Contains("Section group name", StringComparison.Ordinal));

            app.SectionNames(nb.Id).Should().Equal("Quarter", "Notes");
            app.SectionGroups(nb.Id).Select(g => g.Name).Should().Equal("Weeks", "A-B");
            app.SectionNamesInGroup(weeks.Id).Should().Equal("Week 01 (Jan 1 to Jan 3)", "Week 02 (Jan 4 to Jan 10)");
            XElement tree = XElement.Parse(app.GetHierarchy(nb.Id, HierarchyScope.Sections));
            tree.Elements(One + "Section").Select(s => (string?)s.Attribute("name")).Should().Equal("Quarter", "Notes");
            XElement group = tree.Elements(One + "SectionGroup").First();
            ((string?)group.Attribute("ID")).Should().Be(weeks.Id);
            group.Elements(One + "Section").Select(s => (string?)s.Attribute("ID")).Should().Equal(week1.Id, week2.Id);
        }
    }

    [Fact]
    public void Create_page_uses_the_titled_blank_style_unless_the_title_is_hidden()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle s = writer.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            PageHandle page = writer.CreatePage(s, MonJan4.Page());

            page.Key.Should().Be(PageKey.Day(MonJan4.Date));
            page.Section.Should().BeSameAs(s);
            app.CallsOf("CreateNewPage").Should().Equal(s.Id + ":BlankPageWithTitle");
        }

        (ComNotebookWriter hidden, FakeOneNoteApp app2, _) = Create(MonJan4.Options(M, MonJan4.Generate() with { HideTitle = true }));
        using (app2)
        {
            NotebookHandle nb = hidden.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle s = hidden.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            hidden.CreatePage(s, MonJan4.Page());
            app2.CallsOf("CreateNewPage").Should().Equal(s.Id + ":BlankPageNoTitle");
        }
    }

    [Fact]
    public void Apply_hierarchy_sends_names_order_levels_and_color_in_one_section_element()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookPlan plan = TestPlans.Small();
            SectionSpec weekSpec = plan.Sections[1];
            NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
            SectionHandle week = writer.CreateSection(nb, weekSpec);
            List<(PageHandle Handle, PageSpec Spec)> pages = weekSpec.Pages.Select(p => (writer.CreatePage(week, p), p)).ToList();

            writer.ApplyHierarchy(week, pages);

            app.PagesOf(week.Id).Select(p => (p.Name, p.Level)).Should().Equal(
                ("Weekly Overview", 1), ("Mon Jan 4", 2), ("Tue Jan 5", 2), ("Weekly Reflection", 1));
            app.SectionColor(week.Id).Should().Be("#F0E6D8");
            string hierarchyXml = app.GetHierarchy(week.Id, HierarchyScope.Pages);
            XElement section = XElement.Parse(hierarchyXml);
            section.Elements(One + "Page").Select(p => (string?)p.Attribute("ID")).Should().Equal(pages.Select(p => p.Handle.Id));
        }
    }

    [Fact]
    public void Hyperlinks_come_from_get_hyperlink_to_object_for_pages_and_sections()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle s = writer.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            PageHandle page = writer.CreatePage(s, MonJan4.Page());
            writer.ApplyHierarchy(s, new[] { (page, MonJan4.Page()) });

            string pageLink = writer.HyperlinkTo(page);
            string sectionLink = writer.HyperlinkTo(s);

            OneNoteHyperlink parsedPage = HyperlinkParser.Parse(pageLink);
            parsedPage.PageId.Should().Be(page.Id);
            parsedPage.SectionId.Should().Be(s.Id);
            parsedPage.Title.Should().Be("Mon Jan 4");
            OneNoteHyperlink parsedSection = HyperlinkParser.Parse(sectionLink);
            parsedSection.IsPageLink.Should().BeFalse();
            parsedSection.SectionId.Should().Be(s.Id);
            app.CallsOf("GetHyperlinkToObject").Should().Equal(page.Id, s.Id);
        }
    }

    [Fact]
    public void Write_page_sends_the_page_xml_with_its_anchors_and_skips_the_last_modified_check()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            PageHandle page = CreateMonJan4(writer, app, out _);
            IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M), MonJan4.NextAnchor(M) };

            writer.WritePage(page, MonJan4.Page(), MonJan4.Art(anchors), anchors);

            app.LastExpectedLastModified.Should().Be(DateTime.MinValue);
            string xml = app.PageContent(page.Id)!;
            xml.Should().Be(PageXmlBuilder.Build(page.Id, MonJan4.Page(), MonJan4.Art(anchors), anchors, MonJan4.Options(M)));
            XElement root = XElement.Parse(xml);
            root.Elements(One + "Outline").Should().HaveCount(3, "the sheet plus two anchors");
            root.Element(One + "Meta")!.Attribute("content")!.Value.Should().Be("day:2027-01-04");
            writer.Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_rejected_page_color_or_date_is_resent_once_with_automatic_color_and_no_date_then_kept_for_later_pages()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, CapturingSink log) = Create();
        using (app)
        {
            app.UpdatePageContentFailure = xml => xml.Contains("color=\"#DED4C8\"", StringComparison.Ordinal)
                ? new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InvalidXml, "rejected", xml, null)
                : null;
            PageHandle page = CreateMonJan4(writer, app, out SectionHandle section);
            IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) };

            writer.WritePage(page, MonJan4.Page(), MonJan4.Art(anchors), anchors);

            app.CallsOf("UpdatePageContent").Should().HaveCount(2);
            XElement written = XElement.Parse(app.PageContent(page.Id)!);
            written.Element(One + "PageSettings")!.Attribute("color")!.Value.Should().Be("automatic");
            written.Attribute("dateTime").Should().BeNull();
            writer.Warnings.Should().ContainSingle().Which.Should().Contain("automatic");
            writer.XmlOptions.SetPageDates.Should().BeFalse();
            log.Messages.Should().Contain(m => m.Contains("resending", StringComparison.Ordinal));

            PageHandle second = writer.CreatePage(section, MonJan4.Page("Tue Jan 5") with { Key = PageKey.Day(new DateOnly(2027, 1, 5)) });
            writer.WritePage(second, MonJan4.Page("Tue Jan 5"), MonJan4.Art(anchors), anchors);
            app.CallsOf("UpdatePageContent").Should().HaveCount(3, "the second page is written with the fallback straight away");
            XElement.Parse(app.PageContent(second.Id)!).Element(One + "PageSettings")!.Attribute("color")!.Value.Should().Be("automatic");
        }
    }

    [Fact]
    public void A_rejection_that_survives_the_fallback_propagates_with_the_xml()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            app.UpdatePageContentFailure = xml => new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InvalidXml, "rejected", xml, null);
            PageHandle page = CreateMonJan4(writer, app, out _);
            IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) };

            Action act = () => writer.WritePage(page, MonJan4.Page(), MonJan4.Art(anchors), anchors);

            OneNoteXmlRejectedException ex = act.Should().Throw<OneNoteXmlRejectedException>().Which;
            ex.Xml.Should().Contain("color=\"automatic\"", "the second attempt's XML is the one reported");
            app.CallsOf("UpdatePageContent").Should().HaveCount(2);
        }
    }

    [Fact]
    public void Other_rejections_are_not_retried()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            app.UpdatePageContentFailure = xml => new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InsertingImage, "bad image", xml, null);
            PageHandle page = CreateMonJan4(writer, app, out _);
            IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) };

            Action act = () => writer.WritePage(page, MonJan4.Page(), MonJan4.Art(anchors), anchors);

            act.Should().Throw<OneNoteXmlRejectedException>().Which.HResult.Should().Be(OneNoteHResults.InsertingImage);
            app.CallsOf("UpdatePageContent").Should().HaveCount(1);
        }
    }

    [Fact]
    public void Read_anchor_ids_reads_the_page_back_once_without_binary_data_and_matches_outlines_to_anchors()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            PageHandle page = CreateMonJan4(writer, app, out _);
            IReadOnlyList<ResolvedAnchor> anchors = MonJan4.Anchors(M);
            writer.WritePage(page, MonJan4.Page(), MonJan4.Art(anchors), anchors);

            IReadOnlyDictionary<string, string> ids = writer.ReadAnchorIds(page, anchors);

            app.CallsOf("GetPageContent").Should().Equal(page.Id + ":Basic");
            ids.Keys.Should().BeEquivalentTo("dailyDate", "prev", "next", "objectiveLegend", "standingGoal:1");
            ids.Values.Should().OnlyHaveUniqueItems();
            PageXml readBack = writer.ReadPage(page);
            readBack.Outlines.First(o => o.ObjectId == ids["dailyDate"]).PlainText.Should().Be("01 / 04 / 27");
            readBack.Outlines.First(o => o.ObjectId == ids["next"]).PlainText.Should().Be("Tue Jan 5 ›");
            readBack.SheetOutline.Should().NotBeNull();
            ids.Values.Should().NotContain(readBack.SheetOutline!.ObjectId);
            writer.ReadAnchorIds(page, Array.Empty<ResolvedAnchor>()).Should().BeEmpty();
            app.CallsOf("GetPageContent").Should().HaveCount(2, "an empty anchor list reads nothing");
        }
    }

    [Fact]
    public void Contents_page_is_written_as_a_native_outline_of_links()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
            SectionHandle s = writer.CreateSection(nb, new SectionSpec("Quarter", null, Array.Empty<PageSpec>()));
            PageSpec contentsSpec = TestPlans.Small().Page(PageKey.Contents);
            PageHandle contents = writer.CreatePage(s, contentsSpec);
            IReadOnlyList<ContentsSectionEntry> entries = new[]
            {
                new ContentsSectionEntry("Quarter", "onenote:Quarter.one#section-id={s}&end", new[] { new ContentsPageEntry("Cover", "onenote:#Cover&section-id={s}&page-id={p}&end", 1) }),
            };

            writer.WriteContentsPage(contents, contentsSpec, entries);

            XElement root = XElement.Parse(app.PageContent(contents.Id)!);
            root.Descendants(One + "Image").Should().BeEmpty();
            root.Elements(One + "Outline").Should().ContainSingle();
            root.Descendants(One + "T").Select(t => t.Value).Should().Contain(t => t.Contains(">Cover</a>", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Sync_navigate_and_close_forward_the_ids()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, _) = Create();
        using (app)
        {
            PageHandle page = CreateMonJan4(writer, app, out SectionHandle section);

            writer.Sync(section.Notebook);
            writer.NavigateTo(page);
            writer.CloseNotebook(section.Notebook);

            app.CallsOf("SyncHierarchy").Should().Equal(section.Notebook.Id);
            app.CallsOf("NavigateTo").Should().Equal(page.Id);
            app.CallsOf("CloseNotebook").Should().Equal(section.Notebook.Id);
        }
    }

    [Fact]
    public void Notebook_handle_falls_back_to_the_requested_path_and_name_when_the_hierarchy_cannot_be_read()
    {
        (ComNotebookWriter writer, FakeOneNoteApp app, CapturingSink log) = Create();
        using (app)
        {
            app.GetHierarchyFailure = () => new OneNoteException("GetHierarchy", OneNoteHResults.NotYetSynchronized, "not yet", null);

            NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");

            nb.Path.Should().Be(Path.Combine("/out", "SFP Q1 2027"));
            nb.Name.Should().Be("SFP Q1 2027");
            log.Messages.Should().Contain(m => m.Contains("using the requested path", StringComparison.Ordinal));
        }
    }

    private static PageHandle CreateMonJan4(ComNotebookWriter writer, FakeOneNoteApp app, out SectionHandle section)
    {
        NotebookHandle nb = writer.CreateNotebook("/out", "SFP Q1 2027");
        section = writer.CreateSection(nb, new SectionSpec(TestPlans.WeekSectionName, "#F0E6D8", Array.Empty<PageSpec>()));
        PageHandle page = writer.CreatePage(section, MonJan4.Page());
        writer.ApplyHierarchy(section, new[] { (page, MonJan4.Page()) });
        app.CallsOf("CreateNewPage").Should().HaveCount(1);
        return page;
    }
}
