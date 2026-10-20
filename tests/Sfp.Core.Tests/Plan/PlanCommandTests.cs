using System.CommandLine;
using Sfp.Cli;
using Sfp.Cli.Verbs;
using Sfp.Core.Plan;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Plan;

/// <summary>M1.8: the plan verb prints the tree or the JSON through TextWriters and exits 2 with a message on bad input.</summary>
public class PlanCommandTests
{
    private static (int Code, string Out, string Err) Run(PlanRequest request)
    {
        using StringWriter stdout = new(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" };
        using StringWriter stderr = new(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" };
        int code = PlanCommand.Run(request, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static PlanRequest Request(string start) => new(start, TemplatesDir: TestPaths.TemplatesDir);

    [Fact]
    public void Tree_output_for_Q1_2027_lists_sections_and_indented_pages()
    {
        (int code, string output, string err) = Run(Request("2027-01"));

        code.Should().Be(ExitCodes.Success);
        err.Should().BeEmpty();
        output.Should().StartWith("SFP Q1 2027: Q1 2027, 2027-01-01 to 2027-03-31, 14 weeks, 90 days\n19 sections, 144 pages\n");
        output.Should().Contain("Week 02 (Jan 4 to Jan 10)");
        output.Should().Contain("\n    Mon Jan 4  [day:2027-01-04]  pdf 26  2027-01-04\n");
        output.Should().Contain("\n  Weekly Overview  [week:02:overview]  pdf 25  2027-01-04\n");
        output.Should().EndWith("\n585 links: annualObjective 8, calendarDay 90, calendarReview 3, calendarWeek 15, dailyDate 90, next 115, nextCalendar 2, notesContents 4, objectiveBack 8, overviewDayHeader 90, prev 115, prevCalendar 2, progressScorecard 14, quarterReviewScorecard 1, reflectionNext 13, reflectionQuarterReview 1, scorecardWeek 14\n");
    }

    [Fact]
    public void Json_output_round_trips_and_has_144_pages()
    {
        (int code, string output, string err) = Run(Request("2027-01") with { Json = true });

        code.Should().Be(ExitCodes.Success);
        err.Should().BeEmpty();
        PlanJson parsed = PlanJson.Parse(output);
        parsed.Name.Should().Be("SFP Q1 2027");
        parsed.Pages.Should().HaveCount(144);
        parsed.Sections.Should().HaveCount(19);
        parsed.Links.Should().HaveCount(585);
        (PlanJson.From(parsed.ToPlan()).Serialize() + "\n").Should().Be(output);
    }

    [Fact]
    public void Options_flow_into_the_plan()
    {
        (int code, string output, _) = Run(Request("2027-01") with
        {
            Json = true,
            Name = "Custom name",
            Notes = new[] { "Reading", "Scratch" },
            IncludeOutOfQuarterDays = true,
            GroupWeeks = true,
            NoLinks = true,
        });

        code.Should().Be(ExitCodes.Success);
        PlanJson parsed = PlanJson.Parse(output);
        parsed.Name.Should().Be("Custom name");
        parsed.Pages.Should().HaveCount(144 - 4 + 2 + 8);
        parsed.Pages.Where(p => p.Type == PageType.Notes).Select(p => p.Title).Should().Equal("Reading", "Scratch");
        parsed.Pages.Should().Contain(p => p.Key == "day:2026-12-28");
        parsed.Sections.Where(s => s.Name.StartsWith("Week ", StringComparison.Ordinal)).Should().OnlyContain(s => s.GroupName == "Weeks");
        parsed.Links.Should().BeEmpty();
    }

    [Fact]
    public void Tree_output_reports_no_links_when_links_are_off()
    {
        (int code, string output, _) = Run(Request("2024-04") with { NoLinks = true });

        code.Should().Be(ExitCodes.Success);
        output.Should().StartWith("SFP Q2 2024: Q2 2024, 2024-04-01 to 2024-06-30, 13 weeks, 91 days\n18 sections, 143 pages\n");
        output.Should().EndWith("\n0 links\n");
    }

    [Theory]
    [InlineData("2027-13", "Start month must be 01 to 12; got 13 in '2027-13'.")]
    [InlineData("2027-00", "Start month must be 01 to 12; got 0 in '2027-00'.")]
    [InlineData("1899-01", "Start year must be 1900 to 2199; got 1899 in '1899-01'.")]
    [InlineData("2027", "Start month must be YYYY-MM, for example 2027-01; got '2027'.")]
    [InlineData("Jan 2027", "Start month must be YYYY-MM, for example 2027-01; got 'Jan 2027'.")]
    [InlineData("2027-1-1", "Start month must be YYYY-MM, for example 2027-01; got '2027-1-1'.")]
    public void A_bad_start_month_exits_2_with_a_message(string start, string message)
    {
        (int code, string output, string err) = Run(Request(start));

        code.Should().Be(ExitCodes.ValidationError);
        output.Should().BeEmpty();
        err.Should().Be(message + "\n");
    }

    [Fact]
    public void An_empty_start_exits_2()
    {
        (int code, _, string err) = Run(Request(""));

        code.Should().Be(ExitCodes.ValidationError);
        err.Should().NotBeEmpty();
    }

    [Fact]
    public void The_templates_folder_is_found_above_the_test_output_when_not_given()
    {
        PlanCommand.FindTemplatesDir().Should().NotBeNull().And.Match(d => File.Exists(Path.Combine(d!, "sfp-quarterly.json")));
        PlanCommand.FindTemplatesDir(TestPaths.TemplatesDir).Should().Be(TestPaths.TemplatesDir);
        PlanCommand.FindTemplatesDir(Path.Combine(TestPaths.RepoRoot, "src")).Should().BeNull("an explicit folder without the manifest is not silently replaced");

        (int code, string output, _) = Run(new PlanRequest("2027-01"));

        code.Should().Be(ExitCodes.Success);
        output.Should().Contain("Week 02 (Jan 4 to Jan 10)");
    }

    [Fact]
    public void A_templates_folder_without_the_manifest_exits_2()
    {
        string empty = TestPaths.NewTempDir("plan-templates");
        try
        {
            (int code, string output, string err) = Run(Request("2027-01") with { TemplatesDir = empty });

            code.Should().Be(ExitCodes.ValidationError);
            output.Should().BeEmpty();
            err.Should().Contain("sfp-quarterly.json").And.Contain(empty);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void A_user_manifest_is_merged_over_the_shipped_one()
    {
        string dir = TestPaths.NewTempDir("plan-manifest");
        try
        {
            string user = Path.Combine(dir, "user.json");
            File.WriteAllText(user, "{ \"notebook\": { \"namePattern\": \"Planner {Qn} {YYYY}\" } }");
            string broken = Path.Combine(dir, "broken.json");
            File.WriteAllText(broken, "{ \"notebook\": { \"namePattern\": \"Planner {Bogus}\" } }");

            (int code, string output, _) = Run(Request("2027-01") with { ManifestPath = user });
            (int brokenCode, string brokenOut, string brokenErr) = Run(Request("2027-01") with { ManifestPath = broken });
            (int missingCode, _, string missingErr) = Run(Request("2027-01") with { ManifestPath = Path.Combine(dir, "missing.json") });

            code.Should().Be(ExitCodes.Success);
            output.Should().StartWith("Planner Q1 2027: ");
            brokenCode.Should().Be(ExitCodes.ValidationError);
            brokenOut.Should().BeEmpty();
            brokenErr.Should().Contain("{Bogus}");
            missingCode.Should().Be(ExitCodes.ValidationError);
            missingErr.Should().Contain("missing.json");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_defines_the_plan_command_and_its_options()
    {
        Command command = PlanCommand.Build();

        command.Name.Should().Be("plan");
        command.Options.Select(o => o.Name).Should().BeEquivalentTo(
            "--start", "--name", "--notes", "--include-out-of-quarter-days", "--group-weeks", "--no-links", "--json", "--manifest", "--templates");
        command.Options.Single(o => o.Name == "--start").Required.Should().BeTrue();
        command.Parse(new[] { "--start", "2027-01", "--json", "--notes", "A", "B" }).Errors.Should().BeEmpty();
        command.Parse(Array.Empty<string>()).Errors.Should().NotBeEmpty("--start is required");
    }

    [Fact]
    public void Link_summary_counts_by_anchor_type()
    {
        NotebookPlan plan = PlanFixture.Build(PlanFixture.Q1_2027);

        PlanCommand.LinkSummary(plan).Should().StartWith("585 links: annualObjective 8, calendarDay 90, calendarReview 3, calendarWeek 15, ");
        PlanCommand.LinkSummary(plan with { Links = Array.Empty<LinkSpec>() }).Should().Be("0 links");
    }
}
