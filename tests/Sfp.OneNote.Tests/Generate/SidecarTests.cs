using System.Text.Json;
using System.Text.Json.Nodes;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Writer;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>M3.8: the sidecar built after a run, saved with the store, and valid against tests/Sfp.OneNote.Tests/schema/sfp-state.schema.json.</summary>
public class SidecarTests
{
    private static readonly Manifest M = Support.TestManifest.Load();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 30, 0, TimeSpan.FromHours(-5));

    public static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "schema", "sfp-state.schema.json");

    private static (Sidecar Sidecar, FakeNotebookWriter Writer) Build(string? previous = null)
    {
        NotebookPlan plan = TestPlans.Small();
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook(Path.Combine("/out"), plan.Name);
        Dictionary<string, SectionHandle> sections = plan.Sections.ToDictionary(s => s.Name, s => writer.CreateSection(nb, s), StringComparer.Ordinal);
        Dictionary<PageKey, PageHandle> pages = plan.Pages.ToDictionary(p => p.Key, p => writer.CreatePage(sections[plan.SectionOf(p.Key).Name], p));
        Dictionary<PageKey, IReadOnlyDictionary<string, string>> anchors = new()
        {
            [TestPlans.Monday] = new Dictionary<string, string>(StringComparer.Ordinal) { ["dailyDate"] = "{o1}", ["next"] = "{o2}" },
        };
        GenerateOptions options = new() { Start = new QuarterSpec(2027, 1), PdfPath = "x.pdf", PreviousNotebook = previous };
        GenerateRequest request = new(options, M, TestPaths.TemplatesDir, false, null);

        return (SidecarBuilder.Build(request, plan, nb, sections, pages, anchors, "1.0.0+test", Now), writer);
    }

    [Fact]
    public void Builder_records_the_version_manifest_quarter_notebook_sections_pages_and_anchors()
    {
        (Sidecar sidecar, FakeNotebookWriter writer) = Build("/out/SFP Q4 2026");

        sidecar.SchemaVersion.Should().Be(1);
        sidecar.ToolVersion.Should().Be("1.0.0+test");
        sidecar.ManifestId.Should().Be(M.TemplateId);
        sidecar.Quarter.Should().Be(new QuarterSpec(2027, 1));
        sidecar.NotebookName.Should().Be("SFP Q1 2027");
        sidecar.NotebookPath.Should().Be(writer.Notebook!.Path);
        sidecar.NotebookId.Should().Be(writer.Notebook.Id);
        sidecar.Sections.Keys.Should().Equal("Quarter", TestPlans.WeekSectionName);
        sidecar.Sections[TestPlans.WeekSectionName].Should().Be(writer.Sections[1].Id);
        sidecar.Pages.Should().HaveCount(7);
        sidecar.Pages["day:2027-01-04"].PageId.Should().Be(writer.Pages[TestPlans.Monday].Id);
        sidecar.Pages["day:2027-01-04"].SectionId.Should().Be(writer.Sections[1].Id);
        sidecar.Pages["day:2027-01-04"].Anchors.Should().Equal(new Dictionary<string, string> { ["dailyDate"] = "{o1}", ["next"] = "{o2}" });
        sidecar.Pages["cover"].Anchors.Should().BeEmpty();
        sidecar.Generated.Should().Be(Now);
        sidecar.PreviousNotebook.Should().Be("/out/SFP Q4 2026");
        sidecar.TimeBlocks.Should().Be(TimeBlockWindow.Default, "the window the daily pages were generated with");
        sidecar.PlanOptions.Should().BeEquivalentTo(SidecarPlanOptions.From(new GenerateOptions { Start = new QuarterSpec(2027, 1) }), "the default plan options are recorded too");
        sidecar.LastRefresh.Should().BeNull();
        sidecar.FrozenThrough.Should().BeNull();
    }

    [Fact]
    public void The_plan_shaping_options_of_the_run_are_recorded_validate_and_rebuild_the_same_options()
    {
        NotebookPlan plan = TestPlans.Small();
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
        Dictionary<string, SectionHandle> sections = plan.Sections.ToDictionary(s => s.Name, s => writer.CreateSection(nb, s), StringComparer.Ordinal);
        Dictionary<PageKey, PageHandle> pages = plan.Pages.ToDictionary(p => p.Key, p => writer.CreatePage(sections[plan.SectionOf(p.Key).Name], p));
        GenerateOptions options = new()
        {
            Start = new QuarterSpec(2027, 1),
            IncludeOutOfQuarterDays = true,
            ReflectionDateIsWeekStart = true,
            NotesPages = new[] { "Backlog", "Someday" },
            GroupWeeks = true,
            ColorSectionsByMonth = false,
            Links = false,
            SixthRow = SixthRowMode.Drop,
            DateOrder = DateFieldOrder.DayMonthYear,
            PadDateFields = false,
            Dpi = 300,
            PageScale = 0.8,
        };
        GenerateRequest request = new(options, M, TestPaths.TemplatesDir, false, null);

        Sidecar sidecar = SidecarBuilder.Build(request, plan, nb, sections, pages, new Dictionary<PageKey, IReadOnlyDictionary<string, string>>(), "1.0.0", Now);

        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar-plan-options"), plan.Name));
        store.Save(path, sidecar);
        string json = File.ReadAllText(path);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), json);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement recorded = doc.RootElement.GetProperty("planOptions");
        recorded.GetProperty("includeOutOfQuarterDays").GetBoolean().Should().BeTrue();
        recorded.GetProperty("reflectionDateIsWeekStart").GetBoolean().Should().BeTrue();
        recorded.GetProperty("notesPages").EnumerateArray().Select(e => e.GetString()).Should().Equal("Backlog", "Someday");
        recorded.GetProperty("sixthRow").GetString().Should().Be("drop");
        recorded.GetProperty("dateOrder").GetString().Should().Be("dayMonthYear");
        recorded.TryGetProperty("dpi", out _).Should().BeFalse("only the options the plan builder reads are recorded");
        recorded.TryGetProperty("pageScale", out _).Should().BeFalse();

        GenerateOptions rebuilt = SidecarPlanOptions.ToGenerateOptions(new QuarterSpec(2027, 1), store.Load(path)!.PlanOptions);
        rebuilt.Should().BeEquivalentTo(
            options with { Dpi = new GenerateOptions { Start = options.Start }.Dpi, PageScale = 1.0 },
            "Refresh rebuilds the plan with the options the notebook was generated with");
        new PlanBuilder(new DateEngine()).Build(M, new DateEngine().Build(options.Start), rebuilt).Pages.Select(p => p.Key)
            .Should().Equal(new PlanBuilder(new DateEngine()).Build(M, new DateEngine().Build(options.Start), options).Pages.Select(p => p.Key), "the same pages as the generated plan");

        JsonSchemaLite.Validate(File.ReadAllText(SchemaPath), json.Replace("\"drop\"", "\"sideways\"", StringComparison.Ordinal))
            .Should().Contain(p => p.Contains("sixthRow", StringComparison.Ordinal), "the schema names the sixth-row modes");
        JsonSchemaLite.Validate(File.ReadAllText(SchemaPath), json.Replace("\"includeOutOfQuarterDays\": true", "\"includeOutOfQuarterDays\": \"yes\"", StringComparison.Ordinal))
            .Should().Contain(p => p.Contains("includeOutOfQuarterDays", StringComparison.Ordinal));
    }

    [Fact]
    public void The_time_block_window_of_the_run_is_recorded_validates_and_loads_back()
    {
        NotebookPlan plan = TestPlans.Small();
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
        Dictionary<string, SectionHandle> sections = plan.Sections.ToDictionary(s => s.Name, s => writer.CreateSection(nb, s), StringComparer.Ordinal);
        Dictionary<PageKey, PageHandle> pages = plan.Pages.ToDictionary(p => p.Key, p => writer.CreatePage(sections[plan.SectionOf(p.Key).Name], p));
        GenerateRequest request = new(new GenerateOptions { Start = new QuarterSpec(2027, 1), TimeBlocks = new TimeBlockWindow(8, 18) }, M, TestPaths.TemplatesDir, false, null);

        Sidecar sidecar = SidecarBuilder.Build(request, plan, nb, sections, pages, new Dictionary<PageKey, IReadOnlyDictionary<string, string>>(), "1.0.0", Now);

        sidecar.TimeBlocks.Should().Be(new TimeBlockWindow(8, 18));
        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar-window"), plan.Name));
        store.Save(path, sidecar);
        string json = File.ReadAllText(path);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), json);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement window = doc.RootElement.GetProperty("timeBlocks");
        window.GetProperty("startHour").GetInt32().Should().Be(8);
        window.GetProperty("endHour").GetInt32().Should().Be(18);
        store.Load(path)!.TimeBlocks.Should().Be(new TimeBlockWindow(8, 18));
        JsonSchemaLite.Validate(File.ReadAllText(SchemaPath), json.Replace("\"endHour\": 18", "\"endHour\": 25", StringComparison.Ordinal))
            .Should().Contain(p => p.Contains("endHour", StringComparison.Ordinal), "the schema bounds the hours");
    }

    [Fact]
    public void Saved_sidecar_validates_against_the_json_schema_and_loads_back_equal()
    {
        (Sidecar sidecar, _) = Build();
        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar"), "SFP Q1 2027"));

        store.Save(path, sidecar);

        path.Should().EndWith("SFP Q1 2027.sfp-state.json");
        string json = File.ReadAllText(path);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), json);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("quarter").GetProperty("year").GetInt32().Should().Be(2027);
        doc.RootElement.GetProperty("pages").GetProperty("week:02:overview").GetProperty("pageId").GetString().Should().StartWith("{p:");
        doc.RootElement.TryGetProperty("lastRefresh", out _).Should().BeFalse("nulls are left out");
        Sidecar loaded = store.Load(path)!;
        loaded.Should().BeEquivalentTo(sidecar);
    }

    [Fact]
    public void Schema_checker_rejects_a_sidecar_missing_required_keys_or_with_wrong_types()
    {
        string schema = File.ReadAllText(SchemaPath);

        IReadOnlyList<string> missing = JsonSchemaLite.Validate(schema, "{\"schemaVersion\":1,\"toolVersion\":\"1\"}");
        IReadOnlyList<string> wrongType = JsonSchemaLite.Validate(schema, "{\"schemaVersion\":\"one\",\"toolVersion\":\"1\",\"manifestId\":\"m\",\"quarter\":{\"year\":2027,\"month\":1},\"notebookName\":\"n\",\"notebookPath\":\"p\",\"notebookId\":\"i\",\"sections\":{},\"pages\":{\"bad key\":{\"pageId\":\"\",\"sectionId\":\"s\",\"extra\":1}},\"unknown\":true}");

        missing.Should().Contain(p => p.Contains("manifestId", StringComparison.Ordinal));
        wrongType.Should().Contain(p => p.Contains("schemaVersion", StringComparison.Ordinal) && p.Contains("type", StringComparison.Ordinal));
        wrongType.Should().Contain(p => p.Contains("bad key", StringComparison.Ordinal));
        wrongType.Should().Contain(p => p.Contains("pageId", StringComparison.Ordinal) && p.Contains("shorter", StringComparison.Ordinal));
        wrongType.Should().Contain(p => p.Contains("extra", StringComparison.Ordinal) && p.Contains("not allowed", StringComparison.Ordinal));
        wrongType.Should().Contain(p => p.Contains("unknown", StringComparison.Ordinal) && p.Contains("not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Builder_records_the_section_group_of_grouped_sections_under_the_plan_group_name_and_the_file_validates()
    {
        NotebookPlan small = TestPlans.Small();
        NotebookPlan plan = small with { Sections = new[] { small.Sections[0], small.Sections[1] with { GroupName = "Weeks" } } };
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
        SectionGroupHandle weeks = writer.CreateSectionGroup(nb, "Weeks");
        Dictionary<string, SectionHandle> sections = new(StringComparer.Ordinal)
        {
            [plan.Sections[0].Name] = writer.CreateSection(nb, plan.Sections[0]),
            [plan.Sections[1].Name] = writer.CreateSection(nb, plan.Sections[1], weeks),
        };
        Dictionary<PageKey, PageHandle> pages = plan.Pages.ToDictionary(p => p.Key, p => writer.CreatePage(sections[plan.SectionOf(p.Key).Name], p));
        GenerateRequest request = new(new GenerateOptions { Start = new QuarterSpec(2027, 1), GroupWeeks = true }, M, TestPaths.TemplatesDir, false, null);

        Sidecar sidecar = SidecarBuilder.Build(request, plan, nb, sections, pages, new Dictionary<PageKey, IReadOnlyDictionary<string, string>>(), "1.0.0", Now);

        sidecar.SectionGroups.Should().Equal(new Dictionary<string, string> { ["Weeks"] = weeks.Id });
        sidecar.Sections.Keys.Should().Equal(new[] { "Quarter", TestPlans.WeekSectionName }, "a grouped section is still recorded by name");
        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar-groups"), plan.Name));
        store.Save(path, sidecar);
        string json = File.ReadAllText(path);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), json);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("sectionGroups").GetProperty("Weeks").GetString().Should().Be(weeks.Id);
        store.Load(path)!.SectionGroups.Should().Equal(sidecar.SectionGroups);
        Build().Sidecar.SectionGroups.Should().BeEmpty("a notebook without groups records none");
    }

    [Fact]
    public void A_sidecar_written_before_section_groups_and_the_window_were_recorded_loads_with_none()
    {
        (Sidecar sidecar, _) = Build();
        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar-old"), "SFP Q1 2027"));
        store.Save(path, sidecar);
        JsonObject node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node.Remove("sectionGroups").Should().BeTrue();
        node.Remove("timeBlocks").Should().BeTrue();
        node.Remove("planOptions").Should().BeTrue();
        File.WriteAllText(path, node.ToJsonString());

        Sidecar loaded = store.Load(path)!;

        loaded.SectionGroups.Should().BeEmpty();
        loaded.TimeBlocks.Should().BeNull("Refresh then falls back to the Setup page's window");
        loaded.PlanOptions.Should().BeNull();
        SidecarPlanOptions.ToGenerateOptions(loaded.Quarter, loaded.PlanOptions).Should().BeEquivalentTo(new GenerateOptions { Start = loaded.Quarter }, "an older file keeps the default plan options");
        loaded.Sections.Should().Equal(sidecar.Sections);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), File.ReadAllText(path));
    }

    [Fact]
    public void Confirmed_inked_values_are_saved_by_setup_slot_validate_and_a_bad_slot_id_is_rejected()
    {
        (Sidecar built, _) = Build();
        built.ConfirmedInked.Should().BeEmpty("Generate confirms nothing");
        Sidecar sidecar = built with { ConfirmedInked = new Dictionary<string, string> { ["objective:3"] = "Launch v2", ["habit:12"] = "Read" } };
        SidecarStore store = new();
        string path = store.PathFor(Path.Combine(TestPaths.NewTempDir("sidecar-confirmed"), "SFP Q1 2027"));

        store.Save(path, sidecar);

        string json = File.ReadAllText(path);
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), json);
        store.Load(path)!.ConfirmedInked.Should().Equal(sidecar.ConfirmedInked);
        JsonSchemaLite.Validate(File.ReadAllText(SchemaPath), json.Replace("\"habit:12\"", "\"timeBlockStart\"", StringComparison.Ordinal))
            .Should().Contain(p => p.Contains("timeBlockStart", StringComparison.Ordinal), "only objectives, standing goals, metric names and habits are confirmed");
        JsonSchemaLite.Validate(File.ReadAllText(SchemaPath), json.Replace("\"Launch v2\"", "\"\"", StringComparison.Ordinal))
            .Should().NotBeEmpty("a confirmed value has text");
    }

    [Fact]
    public void Tool_version_is_a_numeric_version()
    {
        ToolVersion.Current.Should().MatchRegex("^[0-9]+\\.[0-9]+");
    }

    [Fact]
    public void Pages_without_a_handle_are_left_out_and_missing_anchor_maps_become_empty()
    {
        NotebookPlan plan = TestPlans.Small();
        FakeNotebookWriter writer = new();
        NotebookHandle nb = writer.CreateNotebook("/out", plan.Name);
        SectionHandle section = writer.CreateSection(nb, plan.Sections[0]);
        PageHandle cover = writer.CreatePage(section, plan.Page(PageKey.Cover));
        GenerateRequest request = new(new GenerateOptions { Start = new QuarterSpec(2027, 1) }, M, TestPaths.TemplatesDir, false, null);

        Sidecar sidecar = SidecarBuilder.Build(
            request,
            plan,
            nb,
            new Dictionary<string, SectionHandle> { ["Quarter"] = section },
            new Dictionary<PageKey, PageHandle> { [PageKey.Cover] = cover },
            new Dictionary<PageKey, IReadOnlyDictionary<string, string>>(),
            "1.0.0",
            Now);

        sidecar.Pages.Keys.Should().Equal("cover");
        sidecar.Pages["cover"].Anchors.Should().BeEmpty();
        sidecar.Sections.Keys.Should().Equal("Quarter");
    }
}
