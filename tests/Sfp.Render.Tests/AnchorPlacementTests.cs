using System.Globalization;
using System.Text;
using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
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

    /// <summary>
    /// Wave 3 review: no two placements of a page overlap, so a native object never lands on another (the legend and the
    /// all-day band once shared x 560 to 860 of the title band). Q4 2026 adds the six-row November, whose stacked
    /// "23 / 30" box narrows its link to the "23"; it needs no exception.
    /// </summary>
    [Theory]
    [InlineData(2027, 1)]
    [InlineData(2026, 10)]
    public void No_two_placements_overlap_on_any_page_of_the_quarter(int year, int month)
    {
        GenerateOptions options = f.Q1Options with { Start = new QuarterSpec(year, month) };
        NotebookPlan plan = f.Plan(options);
        RenderOptions render = f.Options(options, dpi: 72);
        List<string> problems = new();
        int pagesWithPlacements = 0;
        foreach (PageSpec page in plan.Pages)
        {
            RenderedPage art = f.Renderer.Render(page, plan, render);
            pagesWithPlacements += art.Anchors.Count > 1 ? 1 : 0;
            problems.AddRange(Overlaps(art.Anchors).Select(p => $"{page.Key}: {Describe(p.A)} overlaps {Describe(p.B)}"));
        }

        pagesWithPlacements.Should().BeGreaterThan(100, "the check must see the daily, overview, calendar and review pages");
        problems.Should().BeEmpty();
    }

    [Fact]
    public void The_overlap_check_catches_the_legend_and_all_day_band_sharing_the_title_band()
    {
        // The daily title band before the wave 3 adjudication: legend x 230 to 870, all-day events x 560 to 860.
        Manifest m = f.Manifest;
        Manifest old = m with
        {
            Slots = m.Slots with
            {
                ObjectiveLegend = m.Slots.ObjectiveLegend with { Rect = new RectPt(230, 49, 640, 13) },
                AllDayBand = m.Slots.AllDayBand with { Rect = new RectPt(560, 49, 300, 13) },
            },
        };
        PageRenderer renderer = new(old, f.Rasterizer, f.Fonts, f.Engine);
        RenderedPage page = renderer.Render(f.Q1Plan.Page(MonJan4), f.Q1Plan, f.Options(f.Q1Options, dpi: 72));

        Overlaps(page.Anchors).Select(p => (p.A.AnchorId, p.B.AnchorId)).Should().Equal(new[] { ("objectiveLegend", "allDayBand") });
    }

    [Fact]
    public void Placements_that_only_share_an_edge_do_not_overlap()
    {
        AnchorPlacement row9 = new("timeBlockEvent:9", 900, 100, 182.7f, 34.6875f, "", TextAlign.Left, 8);
        AnchorPlacement row10 = row9 with { AnchorId = "timeBlockEvent:10", Y = 134.6875f };
        AnchorPlacement inside = row9 with { AnchorId = "inside", X = 950, Y = 120, W = 10, H = 5 };

        Overlaps(new[] { row9, row10 }).Should().BeEmpty("consecutive hour rows touch");
        Overlaps(new[] { row9, inside }).Should().ContainSingle();
    }

    /// <summary>Every pair of placements whose rectangles share an area wider and taller than <paramref name="tolerancePt"/>.</summary>
    internal static IReadOnlyList<(AnchorPlacement A, AnchorPlacement B)> Overlaps(IReadOnlyList<AnchorPlacement> anchors, double tolerancePt = 0.01)
    {
        List<(AnchorPlacement, AnchorPlacement)> pairs = new();
        for (int i = 0; i < anchors.Count; i++)
        {
            for (int j = i + 1; j < anchors.Count; j++)
            {
                AnchorPlacement a = anchors[i];
                AnchorPlacement b = anchors[j];
                double width = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
                double height = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
                if (width > tolerancePt && height > tolerancePt)
                {
                    pairs.Add((a, b));
                }
            }
        }

        return pairs;
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

    private static string Describe(AnchorPlacement a) =>
        string.Create(CultureInfo.InvariantCulture, $"{a.AnchorId} (x {a.X:0.##} to {a.X + a.W:0.##}, y {a.Y:0.##} to {a.Y + a.H:0.##})");

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
