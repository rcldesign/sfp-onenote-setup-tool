using System.Runtime.InteropServices;
using System.Xml.Linq;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Writer;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>M3.9: the Generate run end to end over the fake writer, the fake renderer and the hand-built plan.</summary>
public class GenerateOrchestratorTests
{
    private static readonly Manifest M = Support.TestManifest.Load();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 30, 0, TimeSpan.FromHours(-5));
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    private static string? Name(XElement element) => (string?)element.Attribute("name");

    private static string Text(XElement oe) => PageXmlParser.PlainText(oe.Element(One + "T")!.Value);

    /// <summary>A fixed plan builder standing in for M1.6.</summary>
    private sealed class FixedPlanBuilder : IPlanBuilder
    {
        private readonly Func<NotebookPlan> _plan;

        public FixedPlanBuilder(Func<NotebookPlan>? plan = null)
        {
            _plan = plan ?? TestPlans.Small;
        }

        public NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o) => _plan();
    }

    private sealed class Harness
    {
        public Harness()
        {
            Root = TestPaths.NewTempDir("generate");
            OutputFolder = Path.Combine(Root, "notebooks");
            LogDir = Path.Combine(Root, "logs");
            Directory.CreateDirectory(LogDir);
            Writer = new FakeNotebookWriter { CreateFolderOnDisk = true };
            WriterFactory = () => Writer;
        }

        public string Root { get; }

        public string OutputFolder { get; }

        public string LogDir { get; }

        public FakeNotebookWriter Writer { get; }

        public Func<INotebookWriter> WriterFactory { get; set; }

        public FakePageRenderer Renderer { get; } = new();

        public IPlanBuilder Plans { get; set; } = new FixedPlanBuilder();

        public FakePreflight Preflight { get; set; } = new();

        public CapturingSink Log { get; } = new();

        public ProgressLog Progress { get; } = new();

        public int Parallelism { get; set; } = 4;

        public int Capacity { get; set; } = 2;

        /// <summary>The link writer for --previous, or null to compose none.</summary>
        public Func<INextQuarterLinkWriter>? NextQuarterLinks { get; set; }

        public GenerateOptions Options(bool links = true) => new()
        {
            Start = new QuarterSpec(2027, 1),
            PdfPath = Path.Combine(Root, "SFP_No_Dates.pdf"),
            OutputFolder = OutputFolder,
            Links = links,
        };

        public GenerateRequest Request(GenerateOptions? options = null, string? renderDir = null) =>
            new(options ?? Options(), M, TestPaths.TemplatesDir, renderDir is not null, renderDir);

        public GenerateOrchestrator Build() => new(
            Preflight,
            new DateEngine(),
            Plans,
            Renderer,
            WriterFactory,
            new SidecarStore(),
            new GenerateOrchestratorOptions
            {
                FontFamily = "TeX Gyre Bonum",
                TimeZone = MonJan4.FixedZone,
                ToolVersion = "1.0.0+test",
                LogPath = Path.Combine(LogDir, "20261020-093000-generate.log"),
                MaxDegreeOfParallelism = Parallelism,
                RenderBufferCapacity = Capacity,
                Now = () => Now,
            },
            Log.CreateLogger(),
            NextQuarterLinks);

        public GenerateResult Run(GenerateRequest? request = null, CancellationToken ct = default) => Build().Run(request ?? Request(), Progress, ct);
    }

    [Fact]
    public void A_full_run_follows_the_prd_call_sequence_and_reports_success()
    {
        Harness h = new();

        GenerateResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        result.Phase.Should().Be(GeneratePhase.Done);
        result.NotebookName.Should().Be("SFP Q1 2027");
        result.OutputPath.Should().Be(Path.Combine(h.OutputFolder, "SFP Q1 2027"));
        result.PagesRendered.Should().Be(6);
        result.PagesWritten.Should().Be(7, "six sheets plus the native Contents page");
        result.OutputBytes.Should().BeGreaterThan(0);
        result.SidecarPath.Should().Be(new SidecarStore().PathFor(result.OutputPath!));
        File.Exists(result.SidecarPath).Should().BeTrue();
        result.LogPath.Should().EndWith("generate.log");
        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);

        IReadOnlyList<string> calls = h.Writer.Calls;
        int lastCreatePage = calls.ToList().FindLastIndex(c => c.StartsWith("CreatePage:", StringComparison.Ordinal));
        int firstHierarchy = calls.ToList().FindIndex(c => c.StartsWith("ApplyHierarchy:", StringComparison.Ordinal));
        int lastHierarchy = calls.ToList().FindLastIndex(c => c.StartsWith("ApplyHierarchy:", StringComparison.Ordinal));
        int firstLink = calls.ToList().FindIndex(c => c.StartsWith("HyperlinkTo", StringComparison.Ordinal));
        int lastLink = calls.ToList().FindLastIndex(c => c.StartsWith("HyperlinkTo", StringComparison.Ordinal));
        int firstWrite = calls.ToList().FindIndex(c => c.StartsWith("WritePage:", StringComparison.Ordinal));
        calls[0].Should().Be("CreateNotebook:SFP Q1 2027");
        calls.Skip(1).Take(2).Should().Equal("CreateSection:Quarter", "CreateSection:" + TestPlans.WeekSectionName);
        calls.Where(c => c.StartsWith("CreatePage:", StringComparison.Ordinal)).Should().HaveCount(7, "every page is created before any content is written");
        lastCreatePage.Should().BeLessThan(firstHierarchy, "every page is created before the hierarchy");
        lastHierarchy.Should().BeLessThan(firstLink, "hierarchy before hyperlinks");
        lastLink.Should().BeLessThan(firstWrite, "hyperlinks before content");
        calls.Where(c => c.StartsWith("HyperlinkTo:", StringComparison.Ordinal)).Should().HaveCount(7);
        calls.Where(c => c.StartsWith("HyperlinkToSection:", StringComparison.Ordinal)).Should().HaveCount(2);
        calls.Where(c => c.StartsWith("WritePage:", StringComparison.Ordinal)).Should().Equal(
            "WritePage:cover", "WritePage:setup", "WritePage:week:02:overview", "WritePage:day:2027-01-04", "WritePage:day:2027-01-05", "WritePage:week:02:reflection");
        calls.Where(c => c.StartsWith("ReadAnchorIds:", StringComparison.Ordinal)).Should().Equal(
            "ReadAnchorIds:week:02:overview", "ReadAnchorIds:day:2027-01-04", "ReadAnchorIds:day:2027-01-05", "ReadAnchorIds:week:02:reflection");
        calls.TakeLast(3).Should().Equal("WriteContentsPage:contents", "Sync", "NavigateTo:cover");
        calls.ToList().IndexOf("WriteContentsPage:contents").Should().BeGreaterThan(calls.ToList().FindLastIndex(c => c.StartsWith("WritePage:", StringComparison.Ordinal)));

        h.Writer.Written[TestPlans.Monday].Anchors.Select(a => a.Placement.AnchorId).Should().Equal("dailyDate", "next");
        h.Writer.Written[TestPlans.Monday].Anchors.Should().OnlyContain(a => a.Placement.Kind == AnchorKind.Link, "propagated slots are created by Refresh");
        h.Writer.Written[TestPlans.Monday].Anchors[0].Href.Should().Contain("page-id=" + h.Writer.Pages[TestPlans.Overview].Id);
        h.Writer.Written[PageKey.Cover].Anchors.Should().BeEmpty();
        h.Writer.Hierarchies[TestPlans.WeekSectionName].Select(p => p.Spec.Title).Should().Equal("Weekly Overview", "Mon Jan 4", "Tue Jan 5", "Weekly Reflection");
        h.Writer.Contents!.Value.Sections.Select(s => s.Name).Should().Equal("Quarter", TestPlans.WeekSectionName);
        h.Writer.Contents.Value.Sections[1].Pages.Select(p => p.Title).Should().Equal("Weekly Overview", "Mon Jan 4", "Tue Jan 5", "Weekly Reflection");
        h.Renderer.Rendered.Should().HaveCount(6).And.NotContain(PageKey.Contents);
        h.Preflight.Request.Should().NotBeNull();
    }

    [Fact]
    public void Progress_walks_the_phases_in_order_with_page_counters()
    {
        Harness h = new();

        h.Run();

        h.Progress.Phases.Should().Equal(
            GeneratePhase.CheckingOneNote, GeneratePhase.Validating, GeneratePhase.CreatingNotebook, GeneratePhase.WritingPages, GeneratePhase.Rendering,
            GeneratePhase.BuildingContents, GeneratePhase.Finishing, GeneratePhase.Done);
        h.Progress.Reports.Where(r => r.Phase == GeneratePhase.WritingPages && r.Current > 0).Select(r => (r.Current, r.Total, r.Detail))
            .Should().Equal((1, 6, "Cover"), (2, 6, "Setup"), (3, 6, "Weekly Overview"), (4, 6, "Mon Jan 4"), (5, 6, "Tue Jan 5"), (6, 6, "Weekly Reflection"));
        h.Progress.Reports.Where(r => r.Phase == GeneratePhase.Rendering && r.Current > 0).Select(r => r.Current).OrderBy(c => c).Should().Equal(1, 2, 3, 4, 5, 6);
        h.Progress.Reports.Where(r => r.Phase == GeneratePhase.CreatingNotebook && r.Current > 0).Should().HaveCount(7);
    }

    [Fact]
    public void The_sidecar_holds_the_ids_of_every_section_page_and_link_anchor_and_validates_against_the_schema()
    {
        Harness h = new();

        GenerateResult result = h.Run();

        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        sidecar.ToolVersion.Should().Be("1.0.0+test");
        sidecar.ManifestId.Should().Be(M.TemplateId);
        sidecar.Quarter.Should().Be(new QuarterSpec(2027, 1));
        sidecar.NotebookName.Should().Be("SFP Q1 2027");
        sidecar.NotebookPath.Should().Be(result.OutputPath);
        sidecar.NotebookId.Should().Be(h.Writer.Notebook!.Id);
        sidecar.Sections.Should().Equal(new Dictionary<string, string> { ["Quarter"] = h.Writer.Sections[0].Id, [TestPlans.WeekSectionName] = h.Writer.Sections[1].Id });
        sidecar.Pages.Keys.Should().Equal("cover", "contents", "setup", "week:02:overview", "day:2027-01-04", "day:2027-01-05", "week:02:reflection");
        sidecar.Pages["day:2027-01-04"].PageId.Should().Be(h.Writer.Pages[TestPlans.Monday].Id);
        sidecar.Pages["day:2027-01-04"].SectionId.Should().Be(h.Writer.Sections[1].Id);
        sidecar.Pages["day:2027-01-04"].Anchors.Keys.Should().Equal("dailyDate", "next");
        sidecar.Pages["day:2027-01-04"].Anchors.Should().NotContainKey("objectiveLegend", "no empty propagated objects are recorded");
        sidecar.Pages["contents"].Anchors.Should().BeEmpty();
        sidecar.Generated.Should().Be(Now);
        sidecar.TimeBlocks.Should().Be(TimeBlockWindow.Default);
        JsonSchemaLite.AssertValid(File.ReadAllText(SidecarTests.SchemaPath), File.ReadAllText(result.SidecarPath!));

        Harness windowed = new();
        GenerateResult custom = windowed.Run(windowed.Request(windowed.Options() with { TimeBlocks = new TimeBlockWindow(8, 18) }));
        new SidecarStore().Load(custom.SidecarPath!)!.TimeBlocks.Should().Be(new TimeBlockWindow(8, 18), "the run's window is what the daily pages show");
    }

    [Fact]
    public void Cancelling_after_the_second_page_stops_there_closes_the_notebook_and_deletes_its_folder()
    {
        Harness h = new() { Parallelism = 1, Capacity = 1 };
        using CancellationTokenSource cts = new();
        h.Progress.OnReport = r =>
        {
            if (r.Phase == GeneratePhase.WritingPages && r.Current == 2)
            {
                cts.Cancel();
            }
        };

        GenerateResult result = h.Run(ct: cts.Token);

        result.Cancelled.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.Phase.Should().Be(GeneratePhase.Cancelled);
        h.Writer.Written.Should().HaveCount(2);
        h.Writer.Calls.Last().Should().Be("CloseNotebook");
        Directory.Exists(Path.Combine(h.OutputFolder, "SFP Q1 2027")).Should().BeFalse("the partial notebook is deleted so a retry does not hit the collision rule");
        File.Exists(new SidecarStore().PathFor(Path.Combine(h.OutputFolder, "SFP Q1 2027"))).Should().BeFalse();
        h.Progress.Reports.Last().Phase.Should().Be(GeneratePhase.Cancelled);
    }

    [Fact]
    public void Cancelling_while_pages_are_created_also_deletes_the_folder()
    {
        Harness h = new();
        using CancellationTokenSource cts = new();
        h.Progress.OnReport = r =>
        {
            if (r.Phase == GeneratePhase.CreatingNotebook && r.Current == 3)
            {
                cts.Cancel();
            }
        };

        GenerateResult result = h.Run(ct: cts.Token);

        result.Cancelled.Should().BeTrue();
        h.Writer.Written.Should().BeEmpty();
        h.Renderer.Rendered.Should().BeEmpty();
        Directory.Exists(Path.Combine(h.OutputFolder, "SFP Q1 2027")).Should().BeFalse();
    }

    [Fact]
    public void A_render_failure_maps_to_exit_4_names_the_page_and_writes_nothing_to_onenote()
    {
        Harness h = new();
        h.Renderer.Failure = page => page.Key == PageKey.Cover ? new InvalidOperationException("PDFium could not decode the page") : null;

        GenerateResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(4);
        result.Phase.Should().Be(GeneratePhase.Rendering);
        result.FailedPage.Should().Be("Cover");
        result.Error.Should().Contain("Rendering pages").And.Contain("Cover").And.Contain("PDF page 1").And.Contain("PDFium could not decode the page");
        h.Writer.Written.Should().BeEmpty("nothing is written to OneNote");
        h.Writer.Calls.Should().NotContain(c => c.StartsWith("WritePage:", StringComparison.Ordinal));
        h.Writer.Calls.Should().NotContain("Sync");
        result.OutputPath.Should().Be(Path.Combine(h.OutputFolder, "SFP Q1 2027"));
        Directory.Exists(result.OutputPath).Should().BeTrue("the partial notebook is kept for inspection");
        h.Progress.Reports.Last().Phase.Should().Be(GeneratePhase.Failed);
        h.Log.Messages.Should().Contain(m => m.Contains("Generate failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rejected_page_xml_maps_to_exit_5_names_the_page_and_saves_the_xml_beside_the_log()
    {
        Harness h = new() { Parallelism = 1, Capacity = 1 };
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        app.UpdatePageContentFailure = xml => xml.Contains("content=\"day:2027-01-04\"", StringComparison.Ordinal)
            ? new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InsertingImage, "OneNote rejected the XML passed to UpdatePageContent: hrInsertingImage (0x80042007)", xml, null)
            : null;
        PageXmlOptions xml = PageXmlOptions.From(M, h.Options(), "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(5);
        result.Phase.Should().Be(GeneratePhase.WritingPages);
        result.FailedPage.Should().Be("Mon Jan 4");
        result.Error.Should().Contain("Writing pages").And.Contain("Mon Jan 4").And.Contain("0x80042007");
        string saved = Path.Combine(h.LogDir, "day-2027-01-04.xml");
        result.Error.Should().Contain(saved);
        File.Exists(saved).Should().BeTrue();
        File.ReadAllText(saved).Should().Contain("content=\"day:2027-01-04\"");
        app.WrittenPages.Should().HaveCount(3, "cover, setup and the overview were written before the rejection");
        app.CallsOf("SyncHierarchy").Should().BeEmpty();
        app.CallsOf("CloseNotebook").Should().BeEmpty("the partial notebook is kept");
        result.OutputPath.Should().Be(Path.Combine(h.OutputFolder, "SFP Q1 2027"));
    }

    [Fact]
    public void A_full_run_over_the_com_writer_and_the_in_memory_onenote_records_real_anchor_object_ids()
    {
        Harness h = new();
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        PageXmlOptions xml = PageXmlOptions.From(M, h.Options(), "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        app.WrittenPages.Should().HaveCount(7);
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        sidecar.Pages["day:2027-01-04"].Anchors.Keys.Should().Equal("dailyDate", "next");
        sidecar.Pages["day:2027-01-04"].Anchors.Values.Should().OnlyContain(id => id.Contains(":o", StringComparison.Ordinal), "object ids come from the page read back");
        app.CallsOf("NavigateTo").Should().Equal(sidecar.Pages["cover"].PageId);
        app.CallsOf("SyncHierarchy").Should().Equal(sidecar.NotebookId);
        app.SectionNames(sidecar.NotebookId).Should().Equal("Quarter", TestPlans.WeekSectionName);
        app.PagesOf(sidecar.Sections[TestPlans.WeekSectionName]).Select(p => (p.Name, p.Level)).Should().Equal(("Weekly Overview", 1), ("Mon Jan 4", 2), ("Tue Jan 5", 2), ("Weekly Reflection", 1));
    }

    [Fact]
    public void The_blank_objective_details_parent_is_written_as_a_frame_and_onenote_receives_no_image_data_for_it()
    {
        Harness h = new() { Plans = new PlanBuilder(new DateEngine()) };
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        PageXmlOptions xml = PageXmlOptions.From(M, h.Options(), "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.Warnings.Should().BeEmpty();
        h.Renderer.Rendered.Should().Contain(PageKey.ObjectiveDetails, "the parent page goes through the render pipeline like every sheet");
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        string parentId = sidecar.Pages[PageKey.ObjectiveDetails.Value].PageId;
        XElement parent = XElement.Parse(app.PageContent(parentId)!);
        parent.Descendants(One + "Image").Should().BeEmpty("the renderer gave the page no image");
        parent.Descendants(One + "Data").Should().BeEmpty();
        parent.Elements(One + "Outline").Should().BeEmpty();
        parent.Element(One + "Meta")!.Attribute("content")!.Value.Should().Be("objectives");
        app.PagesOf(sidecar.Sections["Objectives"]).Select(p => (p.Name, p.Level)).Skip(2).Take(2)
            .Should().Equal(("Objective Details", 1), ("Objective 1", 2));
        app.WrittenPages.Should().HaveCount(144);
        app.WrittenPages.Values.Count(page => page.Contains("<one:Data>", StringComparison.Ordinal)).Should().Be(142, "every page but Contents and the blank parent carries its sheet");
    }

    [Fact]
    public void Group_weeks_creates_the_weeks_group_once_before_its_first_section_and_records_it_in_the_sidecar()
    {
        Harness h = new() { Plans = new PlanBuilder(new DateEngine()) };

        GenerateResult result = h.Run(h.Request(h.Options() with { GroupWeeks = true }));

        result.Success.Should().BeTrue(result.Error);
        List<string> creates = h.Writer.Calls.Where(c => c.StartsWith("CreateSection", StringComparison.Ordinal)).ToList();
        creates.Should().ContainSingle(c => c == "CreateSectionGroup:" + PlanBuilder.WeeksGroupName);
        int group = creates.IndexOf("CreateSectionGroup:" + PlanBuilder.WeeksGroupName);
        creates[group - 1].Should().Be("CreateSection:Calendar");
        creates[group + 1].Should().Be("CreateSection:Week 01 (Jan 1 to Jan 3)", "the group is created where its first section goes");
        h.Writer.Groups.Should().ContainSingle().Which.Name.Should().Be("Weeks");
        h.Writer.Sections.Where(s => s.Group is not null).Should().HaveCount(14).And.OnlyContain(s => s.Group == h.Writer.Groups[0]);
        h.Writer.Sections.Where(s => s.Group is null).Select(s => s.Name).Should().Equal("Quarter", "Objectives", "Calendar", "Reviews", "Notes");
        h.Writer.Contents!.Value.Sections.Count(s => s.GroupName == "Weeks").Should().Be(14);
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        sidecar.SectionGroups.Should().Equal(new Dictionary<string, string> { ["Weeks"] = h.Writer.Groups[0].Id });
        sidecar.Sections.Should().HaveCount(19);
        JsonSchemaLite.AssertValid(File.ReadAllText(SidecarTests.SchemaPath), File.ReadAllText(result.SidecarPath!));
    }

    [Fact]
    public void With_group_weeks_the_onenote_hierarchy_shows_one_weeks_folder_holding_the_14_week_sections_in_order()
    {
        Harness h = new() { Plans = new PlanBuilder(new DateEngine()) };
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        GenerateOptions options = h.Options() with { GroupWeeks = true };
        PageXmlOptions xml = PageXmlOptions.From(M, options, "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());
        IReadOnlyList<string> weekNames = new PlanBuilder(new DateEngine()).Build(M, TestPlans.Q1, options).Sections
            .Where(s => s.GroupName == PlanBuilder.WeeksGroupName).Select(s => s.Name).ToList();

        GenerateResult result = h.Run(h.Request(options));

        result.Success.Should().BeTrue(result.Error);
        result.Warnings.Should().BeEmpty();
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        XElement tree = XElement.Parse(app.GetHierarchy(sidecar.NotebookId, HierarchyScope.Sections));
        tree.Elements(One + "Section").Select(Name).Should().Equal("Quarter", "Objectives", "Calendar", "Reviews", "Notes");
        XElement weeks = tree.Elements(One + "SectionGroup").Should().ContainSingle().Subject;
        Name(weeks).Should().Be("Weeks");
        weekNames.Should().HaveCount(14);
        weeks.Elements(One + "Section").Select(Name).Should().Equal(weekNames);
        weekNames[0].Should().Be("Week 01 (Jan 1 to Jan 3)");
        weekNames[13].Should().Be("Week 14 (Mar 29 to Mar 31)");
        Directory.Exists(Path.Combine(result.OutputPath!, "Weeks")).Should().BeTrue("the group is a folder inside the notebook folder");
        File.Exists(Path.Combine(result.OutputPath!, "Weeks", weekNames[0] + ".one")).Should().BeTrue();
        sidecar.SectionGroups.Should().Equal(new Dictionary<string, string> { ["Weeks"] = (string)weeks.Attribute("ID")! });
        sidecar.Sections[weekNames[0]].Should().Be((string?)weeks.Elements(One + "Section").First().Attribute("ID"));
        app.PagesOf(sidecar.Sections[weekNames[1]]).Select(p => p.Name).Should().Equal(
            "Weekly Overview", "Mon Jan 4", "Tue Jan 5", "Wed Jan 6", "Thu Jan 7", "Fri Jan 8", "Sat Jan 9", "Sun Jan 10", "Weekly Reflection");

        XElement contents = XElement.Parse(app.PageContent(sidecar.Pages[PageKey.Contents.Value].PageId)!).Element(One + "Outline")!;
        List<XElement> top = contents.Element(One + "OEChildren")!.Elements(One + "OE").ToList();
        top.Select(Text).Should().Equal("Quarter", "Objectives", "Calendar", "Weeks", "Reviews", "Notes");
        top[3].Element(One + "OEChildren")!.Elements(One + "OE").Select(Text).Should().Equal(weekNames, "the Contents page lists the group as a heading above its sections");
    }

    [Fact]
    public void Without_group_weeks_every_section_stays_at_the_top_level_and_no_group_is_recorded()
    {
        Harness h = new() { Plans = new PlanBuilder(new DateEngine()) };
        using FakeOneNoteApp app = new();
        PageXmlOptions xml = PageXmlOptions.From(M, h.Options(), "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        XElement tree = XElement.Parse(app.GetHierarchy(sidecar.NotebookId, HierarchyScope.Sections));
        tree.Elements(One + "SectionGroup").Should().BeEmpty();
        tree.Elements(One + "Section").Should().HaveCount(19);
        app.CallsOf("OpenHierarchy").Should().NotContain(c => c.EndsWith(":Folder", StringComparison.Ordinal));
        sidecar.SectionGroups.Should().BeEmpty();
        XElement contents = XElement.Parse(app.PageContent(sidecar.Pages[PageKey.Contents.Value].PageId)!).Element(One + "Outline")!;
        contents.Element(One + "OEChildren")!.Elements(One + "OE").Should().HaveCount(19);
    }

    [Fact]
    public void With_previous_the_previous_quarterly_review_links_to_the_new_cover_after_the_sidecar_and_before_the_sync()
    {
        Harness h = new();
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        PreviousNotebook previous = PreviousNotebook.Create(app, h.Root, M);
        GenerateOptions options = h.Options() with { PreviousNotebook = previous.Folder };
        PageXmlOptions xml = PageXmlOptions.From(M, options, "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());
        h.NextQuarterLinks = () => new NextQuarterLinkWriter(app, new SidecarStore(), M, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run(h.Request(options));

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        result.Warnings.Should().BeEmpty();
        result.NextQuarterLink.Should().Be("Next quarter link added to the quarterly review of SFP Q4 2026.");
        Sidecar sidecar = new SidecarStore().Load(result.SidecarPath!)!;
        sidecar.PreviousNotebook.Should().Be(previous.Folder);
        string linkId = previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors[NextQuarterLinkWriter.AnchorId];
        OutlineInfo link = previous.Read(app).Outlines.Single(o => o.ObjectId == linkId);
        link.PlainText.Should().Be("Next quarter ›");
        string href = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(link.TextRuns.Single().Html, "href=\"([^\"]+)\"").Groups[1].Value);
        HyperlinkParser.Parse(href).PageId.Should().Be(sidecar.Pages[PageKey.Cover.Value].PageId, "the link opens the new notebook's cover");

        List<(string Call, string Detail)> calls = app.Calls.ToList();
        int linkWrite = calls.FindLastIndex(c => c.Call == "UpdatePageContent" && c.Detail == "Page:" + previous.PageId);
        linkWrite.Should().BeGreaterThan(calls.FindIndex(c => c.Call == "UpdatePageContent" && c.Detail == "Page:" + sidecar.Pages[PageKey.Contents.Value].PageId), "the link is written after the new notebook");
        linkWrite.Should().BeLessThan(calls.FindIndex(c => c.Call == "SyncHierarchy"), "before SyncHierarchy");
        h.Progress.Reports.Should().Contain(r => r.Phase == GeneratePhase.Finishing && r.Detail == "Next quarter link on " + previous.Folder);
    }

    [Fact]
    public void A_previous_folder_that_does_not_exist_is_a_warning_and_the_run_still_succeeds()
    {
        Harness h = new();
        using FakeOneNoteApp app = new();
        string missing = Path.Combine(h.Root, "nowhere", "SFP Q4 2026");
        GenerateOptions options = h.Options() with { PreviousNotebook = missing };
        PageXmlOptions xml = PageXmlOptions.From(M, options, "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());
        h.NextQuarterLinks = () => new NextQuarterLinkWriter(app, new SidecarStore(), M, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run(h.Request(options));

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        result.Phase.Should().Be(GeneratePhase.Done);
        result.NextQuarterLink.Should().BeNull();
        result.Warnings.Should().ContainSingle().Which.Should().StartWith("The Next quarter link was not written to the previous notebook " + missing + ":");
        app.CallsOf("SyncHierarchy").Should().ContainSingle();
        app.CallsOf("NavigateTo").Should().ContainSingle();
    }

    [Fact]
    public void A_link_step_that_throws_or_is_not_composed_is_a_warning_never_a_failure()
    {
        Harness throwing = new() { NextQuarterLinks = () => throw new InvalidOperationException("the COM session is gone") };
        GenerateResult result = throwing.Run(throwing.Request(throwing.Options() with { PreviousNotebook = "/notebooks/SFP Q4 2026" }));

        result.Success.Should().BeTrue(result.Error);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("/notebooks/SFP Q4 2026").And.Contain("the COM session is gone");
        throwing.Writer.Calls.Should().Contain("Sync");

        Harness none = new();
        GenerateResult without = none.Run(none.Request(none.Options() with { PreviousNotebook = "/notebooks/SFP Q4 2026" }));

        without.Success.Should().BeTrue(without.Error);
        without.Warnings.Should().ContainSingle().Which.Should().Contain("composes no writer");

        Harness unused = new() { NextQuarterLinks = () => throw new InvalidOperationException("must not be called without --previous") };
        GenerateResult plain = unused.Run();
        plain.Success.Should().BeTrue(plain.Error);
        plain.Warnings.Should().BeEmpty();
        plain.NextQuarterLink.Should().BeNull();
    }

    [Fact]
    public void Render_only_writes_one_png_per_page_and_a_pages_json_index_without_touching_onenote()
    {
        Harness h = new();
        string dir = Path.Combine(h.Root, "png");
        h.WriterFactory = () => throw new InvalidOperationException("render-only must not create a writer");

        GenerateResult result = h.Run(h.Request(h.Options() with { Dpi = 144 }, dir));

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        result.OutputPath.Should().Be(dir);
        result.PagesRendered.Should().Be(6);
        result.PagesWritten.Should().Be(0);
        result.OutputBytes.Should().BeGreaterThan(0);
        Directory.GetFiles(dir, "*.png").Select(Path.GetFileName).Should().BeEquivalentTo(
            "cover.png", "setup.png", "week-02-overview.png", "day-2027-01-04.png", "day-2027-01-05.png", "week-02-reflection.png");
        File.ReadAllBytes(Path.Combine(dir, "cover.png")).Should().Equal(MonJan4.Png);
        PageIndex index = RenderOnlyOutput.ReadIndex(Path.Combine(dir, RenderOnlyOutput.IndexFileName));
        index.Notebook.Should().Be("SFP Q1 2027");
        index.Quarter.Should().Be("2027-01");
        index.Dpi.Should().Be(144);
        index.Count.Should().Be(6);
        index.Pages.Select(p => p.Key).Should().Equal("cover", "setup", "week:02:overview", "day:2027-01-04", "day:2027-01-05", "week:02:reflection");
        PageIndexEntry monday = index.Pages.Single(p => p.Key == "day:2027-01-04");
        monday.File.Should().Be("day-2027-01-04.png");
        monday.Title.Should().Be("Mon Jan 4");
        monday.Section.Should().Be(TestPlans.WeekSectionName);
        monday.Level.Should().Be(2);
        monday.PdfPage.Should().Be(26);
        monday.Width.Should().Be(2224);
        monday.Height.Should().Be(1540);
        monday.Anchors.Select(a => a.AnchorId).Should().Equal("dailyDate", "next", "objectiveLegend");
        monday.Anchors.Last().Kind.Should().Be(AnchorKind.Propagated);
        h.Progress.Phases.Should().Equal(GeneratePhase.CheckingOneNote, GeneratePhase.Validating, GeneratePhase.Rendering, GeneratePhase.Finishing, GeneratePhase.Done);
        h.Renderer.Threads.Should().NotBeEmpty();
    }

    [Fact]
    public void A_failed_preflight_returns_its_first_exit_code_and_every_message_without_touching_the_writer()
    {
        Harness h = new()
        {
            Preflight = new FakePreflight(new PreflightResult(
                new[] { (GenerateText.SignInMessage, 3), ("Expected 132 pages, found 130", 2) },
                new[] { "Font 'Bookman Old Style' is not installed" })),
        };
        h.WriterFactory = () => throw new InvalidOperationException("must not be called");

        GenerateResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        result.Phase.Should().Be(GeneratePhase.CheckingOneNote);
        result.Error.Should().Contain(GenerateText.SignInMessage).And.Contain("Expected 132 pages, found 130");
        result.Warnings.Should().Equal("Font 'Bookman Old Style' is not installed");
        h.Renderer.Rendered.Should().BeEmpty();

        h = new Harness { Preflight = new FakePreflight(new PreflightResult(new[] { ("Expected 132 pages, found 130", 2) }, Array.Empty<string>())) };
        h.Run().Phase.Should().Be(GeneratePhase.Validating);
    }

    [Fact]
    public void Warnings_from_the_preflight_and_the_writer_reach_the_result()
    {
        Harness h = new() { Preflight = new FakePreflight(new PreflightResult(Array.Empty<(string, int)>(), new[] { "The output folder is under OneDrive" })) };
        h.Writer.AddWarning("OneNote refused the page color");

        GenerateResult result = h.Run();

        result.Success.Should().BeTrue();
        result.Warnings.Should().Equal("The output folder is under OneDrive", "OneNote refused the page color");
    }

    [Fact]
    public void Links_off_writes_every_sheet_without_anchors()
    {
        Harness h = new();

        GenerateResult result = h.Run(h.Request(h.Options(links: false)));

        result.Success.Should().BeTrue();
        h.Writer.Written.Values.Should().OnlyContain(w => w.Anchors.Count == 0);
        h.Writer.Calls.Should().NotContain(c => c.StartsWith("ReadAnchorIds:", StringComparison.Ordinal));
        new SidecarStore().Load(result.SidecarPath!)!.Pages.Values.Should().OnlyContain(p => p.Anchors.Count == 0);
    }

    [Fact]
    public void A_plan_builder_that_is_not_there_yet_fails_validation_with_exit_2()
    {
        Harness h = new() { Plans = new FixedPlanBuilder(() => throw new NotImplementedException("The plan builder (M1.6) has not been merged yet.")) };

        GenerateResult result = h.Run();

        result.ExitCode.Should().Be(2);
        result.Phase.Should().Be(GeneratePhase.Validating);
        result.Error.Should().Contain("M1.6");
    }

    [Fact]
    public void A_onenote_failure_while_creating_the_notebook_maps_to_exit_5_and_an_unavailable_onenote_to_exit_3()
    {
        Harness busy = new();
        busy.Writer.CreateNotebookFailure = new OneNoteBusyException("OpenHierarchy", OneNoteHResults.RpcCallRejected, null);
        GenerateResult busyResult = busy.Run();
        busyResult.ExitCode.Should().Be(5);
        busyResult.Phase.Should().Be(GeneratePhase.CreatingNotebook);
        busyResult.Error.Should().Contain("Creating notebook").And.Contain("0x80010001");

        Harness gone = new();
        gone.Writer.CreateNotebookFailure = new OneNoteUnavailableException("OpenHierarchy", OneNoteHResults.RpcServerUnavailable, "OneNote went away", null);
        GenerateResult goneResult = gone.Run();
        goneResult.ExitCode.Should().Be(3);
        goneResult.Error.Should().Contain("OneNote went away");
    }

    [Fact]
    public void Onenote_going_away_while_pages_are_written_exits_3_saying_it_stopped_not_that_it_is_missing()
    {
        Harness h = new() { Parallelism = 1, Capacity = 1 };
        using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
        app.UpdatePageContentFailure = xml => xml.Contains("content=\"day:2027-01-04\"", StringComparison.Ordinal)
            ? OneNoteHResults.Map("UpdatePageContent", new COMException("The object invoked has disconnected from its clients.", OneNoteHResults.RpcDisconnected), xml)
            : null;
        PageXmlOptions xml = PageXmlOptions.From(M, h.Options(), "TeX Gyre Bonum", MonJan4.FixedZone);
        h.WriterFactory = () => new ComNotebookWriter(app, xml, h.Log.CreateLogger());

        GenerateResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        result.Phase.Should().Be(GeneratePhase.WritingPages);
        result.FailedPage.Should().Be("Mon Jan 4");
        result.Error.Should().Contain(OneNoteUnavailableException.DisconnectedMessage).And.Contain("RPC_E_DISCONNECTED (0x80010108)");
        result.Error.Should().NotContain("is required", "OneNote is installed; it went away during the run");
    }

    [Fact]
    public void An_unexpected_write_failure_maps_to_exit_5_with_the_page_named()
    {
        Harness h = new() { Parallelism = 1, Capacity = 1 };
        h.Writer.WritePageFailure = key => key == TestPlans.Tuesday ? new IOException("There is not enough space on the disk.") : null;

        GenerateResult result = h.Run();

        result.ExitCode.Should().Be(5);
        result.Phase.Should().Be(GeneratePhase.WritingPages);
        result.FailedPage.Should().Be("Tue Jan 5");
        result.Error.Should().Contain("not enough space");
        h.Writer.Written.Should().HaveCount(4);
    }

    [Fact]
    public void Phase_labels_and_folder_size_helpers()
    {
        GenerateOrchestrator.PhaseLabel(GeneratePhase.WritingPages).Should().Be("Writing pages");
        GenerateOrchestrator.PhaseLabel(GeneratePhase.CheckingOneNote).Should().Be("Checking OneNote");
        string dir = TestPaths.NewTempDir("size");
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[10]);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "sub", "b.bin"), new byte[5]);
        GenerateOrchestrator.FolderSize(dir).Should().Be(15);
        GenerateOrchestrator.FolderSize(Path.Combine(dir, "missing")).Should().BeNull();
    }
}
