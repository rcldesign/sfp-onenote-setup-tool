using System.Globalization;
using System.Text;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Render.Tests;

/// <summary>M2.9: every page's anchor placements, snapshotted for Mon Jan 4 and January 2027 and checked across the whole plan.</summary>
[Collection(RenderCollection.Name)]
public sealed class AnchorPlacementTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private readonly RenderFixture f;

    public AnchorPlacementTests(RenderFixture fixture)
    {
        f = fixture;
    }

    [Fact]
    public async Task Mon_Jan_4_anchors_match_the_snapshot()
    {
        RenderedPage page = f.Render(MonJan4, dpi: 72);

        page.Anchors.Select(a => a.AnchorId).Should().Contain(new[] { "dailyDate", "prev", "next", "objectiveLegend", "standingGoal:1", "standingGoal:2", "standingGoal:3", "metricName:1", "metricName:2", "metricName:3", "allDayBand" });
        page.Anchors.Count(a => a.AnchorId.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).Should().Be(16);
        page.Anchors.Should().HaveCount(11 + 16);
        page.Anchors.Single(a => a.AnchorId == "prev").Text.Should().Be("‹ Sun Jan 3");
        page.Anchors.Single(a => a.AnchorId == "next").Text.Should().Be("Tue Jan 5 ›");

        VerifySettings settings = new();
        settings.UseDirectory("snapshots");
        await Verify(Describe(page), "txt", settings);
    }

    [Fact]
    public async Task January_2027_anchors_match_the_snapshot()
    {
        RenderedPage page = f.Render(PageKey.Calendar(2027, 1), dpi: 72);

        page.Anchors.Count(a => a.AnchorId.StartsWith("calendarDay:", StringComparison.Ordinal)).Should().Be(31);
        page.Anchors.Count(a => a.AnchorId.StartsWith("calendarWeek:", StringComparison.Ordinal)).Should().Be(5);

        VerifySettings settings = new();
        settings.UseDirectory("snapshots");
        await Verify(Describe(page), "txt", settings);
    }

    [Fact]
    public void Every_link_of_the_plan_gets_exactly_one_placement_with_its_text()
    {
        RenderOptions options = f.Options(f.Q1Options, dpi: 72);
        foreach (PageSpec page in f.Q1Plan.Pages)
        {
            RenderedPage art = f.Renderer.Render(page, f.Q1Plan, options);
            art.Anchors.Select(a => a.AnchorId).Should().OnlyHaveUniqueItems($"page {page.Key}");
            foreach (LinkSpec link in f.Q1Plan.LinksFrom(page.Key))
            {
                AnchorPlacement placement = art.Anchors.Should().ContainSingle(a => a.AnchorId == link.AnchorId, $"page {page.Key} link {link.AnchorId}").Subject;
                placement.Kind.Should().Be(AnchorKind.Link);
                placement.Text.Should().Be(link.Text);
                placement.W.Should().BePositive();
                placement.H.Should().BePositive();
            }

            foreach (AnchorPlacement placement in art.Anchors.Where(a => a.Kind == AnchorKind.Link && a.AnchorId != "nextQuarter"))
            {
                f.Q1Plan.LinksFrom(page.Key).Should().Contain(l => l.AnchorId == placement.AnchorId, $"page {page.Key} placement {placement.AnchorId} needs a link");
            }
        }
    }

    [Fact]
    public void Propagated_slots_follow_the_page_type()
    {
        RenderOptions options = f.Options(f.Q1Options, dpi: 72);
        foreach (PageSpec page in f.Q1Plan.Pages)
        {
            List<string> slots = f.Renderer.Render(page, f.Q1Plan, options).Anchors.Where(a => a.Kind == AnchorKind.Propagated).Select(a => a.AnchorId).ToList();
            string because = $"page {page.Key}";
            switch (page.Type)
            {
                case PageType.Day:
                    slots.Where(s => !s.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).Should().Equal(
                        new[] { "objectiveLegend", "standingGoal:1", "standingGoal:2", "standingGoal:3", "metricName:1", "metricName:2", "metricName:3", "allDayBand" }, because);
                    slots.Count(s => s.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).Should().Be(16, because);
                    break;
                case PageType.WeeklyOverview:
                    slots.Should().Equal(new[] { "objectiveLegend", "overviewDay:0", "overviewDay:1", "overviewDay:2", "overviewDay:3", "overviewDay:4", "overviewDay:5", "overviewDay:6" }, because);
                    break;
                case PageType.Calendar:
                    slots.Where(s => !s.StartsWith("calendarEvents:", StringComparison.Ordinal)).Should().Equal(new[] { "habitLegend" }, because);
                    slots.Count(s => s.StartsWith("calendarEvents:", StringComparison.Ordinal)).Should().BeInRange(28, 31, because);
                    break;
                case PageType.Annual:
                    slots.Should().HaveCount(4, because).And.OnlyContain(s => s.StartsWith("annualRow:", StringComparison.Ordinal), because);
                    break;
                case PageType.Objective:
                    slots.Should().Equal(new[] { "objectiveHeading" }, because);
                    break;
                case PageType.Scorecard:
                    slots.Should().Equal(Enumerable.Range(1, 8).Select(n => "scorecardRow:" + n.ToString(CultureInfo.InvariantCulture)), because);
                    break;
                default:
                    slots.Should().BeEmpty(because);
                    break;
            }
        }
    }

    [Fact]
    public void Links_off_leaves_only_propagated_slots()
    {
        GenerateOptions noLinks = f.Q1Options with { Links = false };
        NotebookPlan plan = f.Plan(noLinks);
        RenderOptions options = f.Options(noLinks, dpi: 72);
        foreach (PageSpec page in plan.Pages)
        {
            f.Renderer.Render(page, plan, options).Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link, $"page {page.Key}");
        }
    }

    /// <summary>One line per placement in emission order, every number with two decimals at most.</summary>
    private static string Describe(RenderedPage page)
    {
        StringBuilder text = new();
        text.Append(page.Key).Append(": ").Append(page.Anchors.Count.ToString(CultureInfo.InvariantCulture)).Append(" anchors\n");
        foreach (AnchorPlacement a in page.Anchors)
        {
            text.Append(CultureInfo.InvariantCulture, $"{a.AnchorId}  {a.Kind}  x={a.X:0.##} y={a.Y:0.##} w={a.W:0.##} h={a.H:0.##}  \"{a.Text}\"  {a.Align}  {a.SizePt:0.##}pt  opacity={a.Opacity:0.##}\n");
        }

        return text.ToString();
    }
}
