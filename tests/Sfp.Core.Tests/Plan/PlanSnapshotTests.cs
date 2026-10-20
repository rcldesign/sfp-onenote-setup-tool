using Sfp.Core.Plan;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Plan;

/// <summary>The full Q1 2027 plan (sections, page keys, titles, levels, PDF pages, dates) as a reviewed snapshot under tests/Sfp.Core.Tests/snapshots.</summary>
public class PlanSnapshotTests
{
    [Fact]
    public async Task Q1_2027_plan_tree_matches_the_snapshot()
    {
        NotebookPlan plan = Build(Q1_2027);

        VerifySettings settings = new();
        settings.UseDirectory("../snapshots");
        await Verify(PlanText.Tree(plan), "txt", settings);
    }

    [Fact]
    public void Tree_lines_follow_the_documented_shape()
    {
        NotebookPlan plan = Build(Q1_2027);
        string tree = PlanText.Tree(plan);

        tree.Should().StartWith("SFP Q1 2027: Q1 2027, 2027-01-01 to 2027-03-31, 14 weeks, 90 days\n19 sections, 144 pages\nQuarter\n  Cover  [cover]  pdf 1  2027-01-01\n");
        tree.Should().Contain("\nWeek 02 (Jan 4 to Jan 10)  color #F0E6D8\n  Weekly Overview  [week:02:overview]  pdf 25  2027-01-04\n    Mon Jan 4  [day:2027-01-04]  pdf 26  2027-01-04\n");
        tree.Should().Contain("\n  Contents  [contents]  pdf none  2027-01-01\n");
        tree.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2 + 19 + 144);
    }

    [Fact]
    public void Tree_shows_the_group_when_weeks_are_grouped()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { GroupWeeks = true });

        PlanText.Tree(plan).Should().Contain("\nWeek 01 (Jan 1 to Jan 3)  color #F0E6D8  group Weeks\n");
    }
}
