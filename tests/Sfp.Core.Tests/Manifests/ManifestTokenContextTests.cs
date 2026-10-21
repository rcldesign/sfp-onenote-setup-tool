using System.Text.Json.Nodes;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.TestSupport;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Manifests;

/// <summary>
/// Tokens are validated per context at load: a pattern that loads always expands in the plan builder. The notebook name
/// takes {Qn} and {YYYY}; section names add {NN}, {start} and {end} when eachWeek; page titles take the quarter and date
/// tokens, the week tokens inside an eachWeek section, {n} on objective templates and {Note} on notes templates with eachNote.
/// </summary>
public class ManifestTokenContextTests : IDisposable
{
    private const int QuarterSection = 0;
    private const int ObjectivesSection = 1;
    private const int CalendarSection = 2;
    private const int WeekSection = 3;
    private const int NotesSection = 5;

    private readonly string _tempDir = TestPaths.NewTempDir("manifest-tokens");
    private readonly ManifestLoader _loader = new();

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> AllButQuarterTokens => new() { "NN", "start", "end", "n", "Ddd", "Mon", "D", "Month", "Note" };

    public static TheoryData<string> WeekTokens => new() { "NN", "start", "end" };

    public static TheoryData<string> PageOnlyTokens => new() { "n", "Ddd", "Mon", "D", "Month", "Note" };

    [Fact]
    public void The_shipped_manifest_still_loads()
    {
        Action act = () => _loader.LoadShipped(TestPaths.TemplatesDir);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(AllButQuarterTokens))]
    public void The_notebook_name_pattern_takes_only_the_quarter_tokens(string token)
    {
        string path = WriteModified(root => root["notebook"]!["namePattern"] = $"SFP {{Qn}} {{YYYY}} {{{token}}}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*notebook.namePattern: token {{{token}}} in 'SFP {{Qn}} {{YYYY}} {{{token}}}' has no value in the notebook name pattern. Tokens available there: {{YYYY}}, {{Qn}}.");
    }

    [Theory]
    [MemberData(nameof(AllButQuarterTokens))]
    public void A_section_without_eachWeek_takes_only_the_quarter_tokens_in_its_name(string token)
    {
        string path = WriteModified(root => Section(root, QuarterSection)["name"] = $"Quarter {{{token}}}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*notebook.sections[0].name: token {{{token}}}*has no value in the name of a section without eachWeek*");
    }

    [Theory]
    [MemberData(nameof(PageOnlyTokens))]
    public void An_eachWeek_section_name_takes_the_week_tokens_but_no_page_token(string token)
    {
        string path = WriteModified(root => Section(root, WeekSection)["name"] = $"Week {{NN}} {{{token}}}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*notebook.sections[3].name: token {{{token}}}*has no value in the name of a section with eachWeek*");
    }

    [Fact]
    public void Section_names_expand_every_token_their_context_allows()
    {
        string path = WriteModified(root =>
        {
            root["notebook"]!["namePattern"] = "Planner {Qn} {YYYY}";
            Section(root, QuarterSection)["name"] = "Quarter {Qn} {YYYY}";
            Section(root, WeekSection)["name"] = "Week {NN} ({start} to {end}) {Qn} {YYYY}";
        });

        NotebookPlan plan = BuildFrom(_loader.Load(path));

        plan.Name.Should().Be("Planner Q1 2027");
        plan.Sections[0].Name.Should().Be("Quarter Q1 2027");
        plan.Sections[4].Name.Should().Be("Week 02 (Jan 4 to Jan 10) Q1 2027");
    }

    [Theory]
    [MemberData(nameof(WeekTokens))]
    public void Week_tokens_in_a_page_title_need_an_eachWeek_section(string token)
    {
        string path = WriteModified(root => Page(root, QuarterSection, 0)["title"] = $"Cover {{{token}}}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*notebook.sections[0].pages[0].title: token {{{token}}} in 'Cover {{{token}}}' has no value on a 'cover' page outside an eachWeek section. Tokens available there: *");
    }

    [Fact]
    public void Week_tokens_in_a_page_title_inside_an_eachWeek_section_expand()
    {
        string path = WriteModified(root => Page(root, WeekSection, 0)["title"] = "Overview {NN} ({start} to {end})");

        NotebookPlan plan = BuildFrom(_loader.Load(path));

        plan.Page(PageKey.WeekOverview(2)).Title.Should().Be("Overview 02 (Jan 4 to Jan 10)");
    }

    [Theory]
    [InlineData(QuarterSection, 0, "cover", "outside")]
    [InlineData(CalendarSection, 0, "calendar", "outside")]
    [InlineData(WeekSection, 1, "day", "inside")]
    public void The_objective_number_is_only_on_objective_templates(int section, int page, string type, string where)
    {
        string path = WriteModified(root => Page(root, section, page)["title"] = "Title {n}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*notebook.sections[{section}].pages[{page}].title: token {{n}}*has no value on a '{type}' page {where} an eachWeek section*");
    }

    [Fact]
    public void The_note_name_is_only_on_notes_templates_with_eachNote()
    {
        string coverWithEachNote = WriteModified(root =>
        {
            Page(root, QuarterSection, 0)["title"] = "Cover {Note}";
            Page(root, QuarterSection, 0)["eachNote"] = true;
        });
        string notesWithoutEachNote = WriteModified(root => Page(root, NotesSection, 0).AsObject().Remove("eachNote"));
        string dayPage = WriteModified(root => Page(root, WeekSection, 1)["title"] = "{Ddd} {Note}");

        _loader.Invoking(l => l.Load(coverWithEachNote)).Should().Throw<ManifestException>()
            .WithMessage("*notebook.sections[0].pages[0].title: token {Note}*has no value on a 'cover' page with eachNote outside an eachWeek section*");
        _loader.Invoking(l => l.Load(notesWithoutEachNote)).Should().Throw<ManifestException>()
            .WithMessage("*notebook.sections[5].pages[0].title: token {Note}*has no value on a 'notes' page outside an eachWeek section*");
        _loader.Invoking(l => l.Load(dayPage)).Should().Throw<ManifestException>()
            .WithMessage("*notebook.sections[3].pages[1].title: token {Note}*");
    }

    [Fact]
    public void Quarter_and_date_tokens_are_available_on_every_page()
    {
        string path = WriteModified(root =>
        {
            Page(root, QuarterSection, 0)["title"] = "Cover {Qn} {YYYY} {Ddd} {Mon} {D} {Month}";
            Page(root, ObjectivesSection, 3)["title"] = "Objective {n} {Qn} {Month}";
        });

        NotebookPlan plan = BuildFrom(_loader.Load(path));

        plan.Page(PageKey.Cover).Title.Should().Be("Cover Q1 2027 Fri Jan 1 January");
        plan.Page(PageKey.Objective(3)).Title.Should().Be("Objective 3 Q1 January");
    }

    [Fact]
    public void Unknown_tokens_are_still_reported_as_unknown_before_the_context_check()
    {
        string path = WriteModified(root => Page(root, QuarterSection, 0)["title"] = "Cover {Foo}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*notebook.sections[0].pages[0].title: Unknown token {Foo} in pattern 'Cover {Foo}'*");
    }

    [Fact]
    public void Every_token_a_title_may_use_has_a_value_in_the_plan_builder()
    {
        // For every page template of the shipped manifest, in its own section context, a title made of every token
        // the load check allows must expand: the check never admits a pattern that fails at Build.
        Manifest shipped = Shipped;
        foreach (SectionTemplate section in shipped.Notebook.Sections)
        {
            foreach (PageTemplate template in section.Pages)
            {
                IReadOnlyList<string> allowed = ManifestChecks.TitleTokens(template, section.EachWeek);
                string title = string.Join(" ", allowed.Select(t => "{" + t + "}"));
                SectionTemplate only = section with { Pages = new[] { template with { Title = title } } };
                Manifest single = shipped with { Notebook = shipped.Notebook with { Sections = new[] { only } } };
                ManifestChecks.Run(single, "single");

                NotebookPlan plan = BuildFrom(single);

                plan.Pages.Should().NotBeEmpty().And.OnlyContain(p => !p.Title.Contains('{', StringComparison.Ordinal), $"{template.Type} expands {title}");
            }
        }
    }

    [Fact]
    public void The_context_token_lists_are_in_canonical_order()
    {
        ManifestChecks.NamePatternTokens.Should().Equal("YYYY", "Qn");
        ManifestChecks.SectionNameTokens(eachWeek: false).Should().Equal("YYYY", "Qn");
        ManifestChecks.SectionNameTokens(eachWeek: true).Should().Equal("NN", "start", "end", "YYYY", "Qn");
        ManifestChecks.TitleTokens(new PageTemplate { Type = "cover", Level = 1 }, inEachWeekSection: false)
            .Should().Equal("Ddd", "Mon", "D", "Month", "YYYY", "Qn");
        ManifestChecks.TitleTokens(new PageTemplate { Type = "objective", Level = 2, Each = true }, inEachWeekSection: false)
            .Should().Equal("n", "Ddd", "Mon", "D", "Month", "YYYY", "Qn");
        ManifestChecks.TitleTokens(new PageTemplate { Type = "notes", Level = 1, EachNote = true }, inEachWeekSection: false)
            .Should().Equal("Ddd", "Mon", "D", "Month", "YYYY", "Qn", "Note");
        ManifestChecks.TitleTokens(new PageTemplate { Type = "day", Level = 2, Each = true }, inEachWeekSection: true)
            .Should().Equal("NN", "start", "end", "Ddd", "Mon", "D", "Month", "YYYY", "Qn");
    }

    private static NotebookPlan BuildFrom(Manifest manifest) =>
        new PlanBuilder(Engine).Build(manifest, Engine.Build(Q1_2027), new GenerateOptions { Start = Q1_2027, Links = false });

    private static JsonNode Section(JsonNode root, int section) => root["notebook"]!["sections"]![section]!;

    private static JsonObject Page(JsonNode root, int section, int page) => Section(root, section)["pages"]![page]!.AsObject();

    private string WriteModified(Action<JsonNode> mutate)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(TestPaths.ManifestPath))!;
        mutate(root);
        string path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }
}
