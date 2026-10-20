using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>M4.4: the slot catalog resolves every propagated, calendar and link rectangle from the manifest for a page of the plan.</summary>
public class SlotCatalogTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly DateEngine Dates = new();
    private static readonly Quarter Q1 = Dates.Build(new QuarterSpec(2027, 1));
    private static readonly NotebookPlan Plan = new PlanBuilder(Dates).Build(M, Q1, new GenerateOptions { Start = new QuarterSpec(2027, 1) });
    private static readonly SlotCatalog Catalog = new(M, Dates, Q1, TimeBlockWindow.Default);

    private static PageSpec Page(PageKey key) => Plan.Page(key);

    [Fact]
    public void Daily_page_carries_the_legend_three_goals_three_metrics_and_the_calendar_rows()
    {
        PageSpec day = Page(PageKey.Day(new DateOnly(2027, 1, 4)));

        Catalog.PropagatedSlotsOn(day).Should().Equal("objectiveLegend", "standingGoal:1", "standingGoal:2", "standingGoal:3", "metricName:1", "metricName:2", "metricName:3");
        Catalog.CalendarSlotsOn(day).Should().HaveCount(1 + 16).And.Contain("allDayBand").And.Contain("timeBlockEvent:6").And.Contain("timeBlockEvent:21").And.NotContain("timeBlockEvent:22");

        Catalog.Slot(day, "objectiveLegend").Should().Be(new SlotGeometry(M.Slots.ObjectiveLegend.Rect, TextAlign.Left, M.Slots.ObjectiveLegend.SizePt, 1.0));
        Catalog.Slot(day, "standingGoal:2").Should().Be(new SlotGeometry(M.Slots.StandingGoals.Rows[1], TextAlign.Left, M.Slots.StandingGoals.SizePt, M.Colors.PrefillInkOpacity));
        Catalog.Slot(day, "metricName:3")!.Rect.Should().Be(M.Slots.MetricNames.Cells[2]);
        Catalog.Slot(day, "standingGoal:4").Should().BeNull();
        Catalog.Slot(day, "habitLegend").Should().BeNull("habits go on calendar pages only");

        TimeBlocksSlot column = M.Slots.TimeBlocks;
        double pitch = column.PitchFor(16);
        SlotGeometry row9 = Catalog.Slot(day, "timeBlockEvent:9")!;
        row9.Rect.X.Should().Be(column.EventTextX);
        row9.Rect.Y.Should().BeApproximately(column.Top + (3 * pitch), 1e-9);
        row9.Rect.H.Should().BeApproximately(pitch, 1e-9);
        row9.Opacity.Should().Be(M.Colors.EventInkOpacity);
        Catalog.Slot(day, "timeBlockEvent:5").Should().BeNull("5 is before the window");

        Catalog.Link(day, "dailyDate")!.Rect.Should().Be(M.Links.DailyDate.Rect);
        Catalog.Link(day, "prev")!.Rect.Should().Be(M.Links.Prev.Rect);
    }

    [Fact]
    public void Overview_page_carries_the_legend_and_seven_day_columns()
    {
        PageSpec overview = Page(PageKey.WeekOverview(2));

        Catalog.PropagatedSlotsOn(overview).Should().Equal("objectiveLegend");
        Catalog.CalendarSlotsOn(overview).Should().Equal(Enumerable.Range(0, 7).Select(i => "overviewDay:" + i));
        Catalog.Slot(overview, "overviewDay:3")!.Rect.Should().Be(M.Slots.OverviewDayColumns.Column(3));
        Catalog.Link(overview, "overviewDayHeader:6")!.Rect.Should().Be(M.Links.OverviewDayHeader.Rect(6));
    }

    [Fact]
    public void Calendar_page_carries_the_habit_legend_the_in_month_cells_and_the_day_and_week_links()
    {
        PageSpec january = Page(PageKey.Calendar(2027, 1));

        Catalog.PropagatedSlotsOn(january).Should().Equal("habitLegend");
        IReadOnlyList<string> cells = Catalog.CalendarSlotsOn(january);
        cells.Should().HaveCount(31).And.Contain("calendarEvents:2027-01-01").And.Contain("calendarEvents:2027-01-31").And.NotContain("calendarEvents:2026-12-28");

        DayGridSlot grid = M.Slots.DayGrid;
        RectPt box = grid.Box(1, 0);
        SlotGeometry jan4 = Catalog.Slot(january, "calendarEvents:2027-01-04")!;
        jan4.Rect.X.Should().Be(box.X + M.Slots.CalendarEventLines.XOffset);
        jan4.Rect.Y.Should().Be(box.Bottom + M.Slots.CalendarEventLines.OffsetBelowBox);
        jan4.Rect.W.Should().Be(M.Slots.CalendarEventLines.W);
        Catalog.Slot(january, "calendarEvents:2026-12-28").Should().BeNull("out-of-month cells get nothing");

        Catalog.Link(january, "calendarDay:2027-01-04")!.Rect.Should().Be(M.Links.CalendarDay.Rect(grid, 1, 0));
        Catalog.Link(january, "calendarWeek:02")!.Rect.Should().Be(M.Links.CalendarWeek.Rect(grid, 1));
        Catalog.Link(january, "calendarWeek:01")!.Rect.Should().Be(M.Links.CalendarWeek.Rect(grid, 0), "week 1 starts on Mon Dec 28, the first grid row");
        Catalog.Link(january, "calendarWeek:07").Should().BeNull("week 7 is not in January");
    }

    [Fact]
    public void Annual_objective_and_scorecard_pages_carry_their_rows_and_headings()
    {
        PageSpec annual1 = Page(PageKey.Annual(1));
        PageSpec annual2 = Page(PageKey.Annual(2));
        PageSpec objective3 = Page(PageKey.Objective(3));
        PageSpec scorecard = Page(PageKey.Scorecard);

        Catalog.PropagatedSlotsOn(annual1).Should().Equal("annualRow:1", "annualRow:2", "annualRow:3", "annualRow:4");
        Catalog.PropagatedSlotsOn(annual2).Should().Equal("annualRow:5", "annualRow:6", "annualRow:7", "annualRow:8");
        Catalog.Slot(annual2, "annualRow:6")!.Rect.Should().Be(M.Slots.AnnualTitle.Rect(1));
        Catalog.Slot(annual1, "annualRow:6").Should().BeNull();
        Catalog.Link(annual1, "annualObjective:3")!.Rect.Should().Be(M.Links.AnnualObjective.Rect(2));

        Catalog.PropagatedSlotsOn(objective3).Should().Equal("objectiveHeading");
        Catalog.Slot(objective3, "objectiveHeading")!.Rect.Should().Be(M.Slots.ObjectiveHeading.Rect);

        Catalog.PropagatedSlotsOn(scorecard).Should().Equal(Enumerable.Range(1, 8).Select(n => "scorecardRow:" + n));
        Catalog.Slot(scorecard, "scorecardRow:8")!.Rect.Should().Be(M.Generated.Scorecard.RowLabel(7));
        Catalog.Link(scorecard, "scorecardWeek:03")!.Rect.Should().Be(M.Generated.Scorecard.WeekHeader(2));
    }

    [Fact]
    public void Pages_without_propagated_slots_get_nothing()
    {
        foreach (PageKey key in new[] { PageKey.Cover, PageKey.Contents, PageKey.Setup, PageKey.WeekReflection(2), PageKey.QuarterlyReview, PageKey.Notes("Backlog") })
        {
            Catalog.PropagatedSlotsOn(Page(key)).Should().BeEmpty(key.Value);
            Catalog.CalendarSlotsOn(Page(key)).Should().BeEmpty(key.Value);
        }

        Catalog.Slot(Page(PageKey.Cover), "nonsense").Should().BeNull();
        Catalog.Link(Page(PageKey.Cover), "nonsense").Should().BeNull();
    }

    [Fact]
    public void Placement_from_geometry_is_a_propagated_anchor()
    {
        SlotGeometry geometry = Catalog.Slot(Page(PageKey.Day(new DateOnly(2027, 1, 4))), "standingGoal:1")!;

        Sfp.Render.AnchorPlacement placement = geometry.ToPlacement("standingGoal:1", "Walk");

        placement.Kind.Should().Be(Sfp.Render.AnchorKind.Propagated);
        placement.Opacity.Should().BeApproximately((float)M.Colors.PrefillInkOpacity, 1e-6f);
        placement.X.Should().Be((float)M.Slots.StandingGoals.Rows[0].X);
        placement.Text.Should().Be("Walk");
    }
}
