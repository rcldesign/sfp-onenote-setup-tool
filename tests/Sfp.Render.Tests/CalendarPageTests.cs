using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Colors;
using Sfp.Render.Pages;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.5: the Monthly Calendar page: month name, baked and linked day numbers, greyed neighbors, stacked sixth row, placements.</summary>
[Collection(RenderCollection.Name)]
public sealed class CalendarPageTests
{
    private static readonly PageKey January = PageKey.Calendar(2027, 1);
    private readonly RenderFixture f;

    public CalendarPageTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private Manifest M => f.Manifest;

    private SKColor DayBox => ColorParser.Parse(M.Colors.DayBox);

    private RectPt BoxInterior(int row, int column)
    {
        RectPt box = M.Slots.DayGrid.Box(row, column);
        return new RectPt(box.X + 2, box.Y + 2, box.W - 4, box.H - 4);
    }

    [SkippableFact]
    public void January_2027_reads_its_month_name()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(January));
        MonthNameSlot name = M.Slots.MonthName;

        string text = RenderFixture.Read(sheet, new RectPt(name.X - 2, name.BaselineY - 14, 160, 19));
        text.Should().Be("January 2027");
    }

    [Fact]
    public void Dec_28_is_greyed_and_Jan_4_is_left_blank_for_its_link()
    {
        RenderedPage page = f.Render(January);
        using SKBitmap sheet = RenderFixture.Decode(page);

        // Row 1 starts on Monday Dec 28; Jan 4 is the Monday of row 2.
        RectPt dec28 = BoxInterior(0, 0);
        SKColor darkest = Pixels.Darkest(sheet, dec28, RenderFixture.Dpi);
        ((int)darkest.Red).Should().BeLessThan(DayBox.Red - 40, "28 is drawn");
        ((int)darkest.Red).Should().BeGreaterThan(f.Ink.Red + 60, "at 40% ink");

        RectPt jan4 = BoxInterior(1, 0);
        ColorParser.MaxChannelDistance(Pixels.Mean(sheet, jan4, RenderFixture.Dpi), DayBox).Should().BeLessThanOrEqualTo(2, "the box holds nothing but its fill");
        page.Anchors.Should().Contain(a => a.AnchorId == "calendarDay:2027-01-04" && a.Kind == AnchorKind.Link && a.Text == "4");
        page.Anchors.Should().NotContain(a => a.AnchorId == "calendarDay:2026-12-28");
    }

    [Fact]
    public void Blank_mode_leaves_out_of_month_days_empty()
    {
        using SKBitmap sheet = RenderFixture.Decode(f.Render(January, o => o with { OutOfMonthDays = OutOfMonthDayStyle.Blank }));
        ColorParser.MaxChannelDistance(Pixels.Mean(sheet, BoxInterior(0, 0), RenderFixture.Dpi), DayBox).Should().BeLessThanOrEqualTo(2);
    }

    [SkippableFact]
    public void With_links_off_the_quarter_days_are_baked()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(January, o => o with { Links = false });
        using SKBitmap sheet = RenderFixture.Decode(page);

        page.Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link);
        RectPt jan4 = M.Slots.DayGrid.Box(1, 0);
        f.HasInk(sheet, BoxInterior(1, 0)).Should().BeTrue("4 is baked when there is no link");
        string text = RenderFixture.Read(sheet, jan4, paddingPt: 2, layout: OcrLayout.SingleWord);
        RenderFixture.Digits(text).Should().Be("4", "OCR read '" + text + "'");
    }

    [Fact]
    public void Week_labels_are_baked_at_their_anchors_with_links_off_and_left_to_the_links_with_links_on()
    {
        RenderedPage off = f.Render(January, o => o with { Links = false });
        RenderedPage on = f.Render(January);
        using SKBitmap offSheet = RenderFixture.Decode(off);
        using SKBitmap onSheet = RenderFixture.Decode(on);
        CalendarWeekAnchor anchor = M.Links.CalendarWeek;

        off.Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link, "links are off");
        for (int row = 0; row < 5; row++)
        {
            RectPt rect = anchor.Rect(M.Slots.DayGrid, row);
            string nn = (row + 1).ToString("00", CultureInfo.InvariantCulture);
            string label = "Week " + nn;
            double width = f.Fonts.TextWidth(label, anchor.SizePt);
            f.HasInk(offSheet, new RectPt(rect.X, rect.Y + 1, 6, rect.H - 2)).Should().BeTrue($"'{label}' starts at the anchor's left edge");
            f.HasInk(offSheet, new RectPt(rect.X + width + 1, rect.Y + 1, rect.W - width - 1, rect.H - 2)).Should().BeFalse($"'{label}' is {anchor.SizePt} pt like the link text, no wider");

            AnchorPlacement link = on.Anchors.Single(a => a.AnchorId == "calendarWeek:" + nn);
            link.Kind.Should().Be(AnchorKind.Link);
            link.Text.Should().Be(label);
            link.X.Should().BeApproximately((float)rect.X, 0.01f);
            link.Y.Should().BeApproximately((float)rect.Y, 0.01f);
            f.HasInk(onSheet, rect).Should().BeFalse($"'{label}' is a link with links on; nothing is baked under it");
        }
    }

    [SkippableFact]
    public void On_the_real_planner_the_week_labels_read_with_links_off_and_stay_unbaked_with_links_on()
    {
        string pdf = SfpPdf.RequireOrSkip();
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap off = RenderFixture.Decode(RenderReal(pdf, January, o => o with { Links = false }));
        RenderedPage onPage = RenderReal(pdf, January);
        using SKBitmap on = RenderFixture.Decode(onPage);
        CalendarWeekAnchor anchor = M.Links.CalendarWeek;

        for (int row = 0; row < 5; row++)
        {
            RectPt rect = anchor.Rect(M.Slots.DayGrid, row);
            string nn = (row + 1).ToString("00", CultureInfo.InvariantCulture);
            string label = "Week " + nn;
            string text = RenderFixture.Read(off, rect, paddingPt: 2);
            text.Should().Be(label, "OCR read '" + text + "'");
            onPage.Anchors.Should().Contain(a => a.AnchorId == "calendarWeek:" + nn && a.Text == label);
            f.HasInk(on, rect).Should().BeFalse($"the printed cell is empty where '{label}' sits as a link");
        }
    }

    [Fact]
    public void Calendar_page_carries_week_labels_habit_legend_event_lines_and_navigation()
    {
        RenderedPage page = f.Render(January);
        DayGridSlot grid = M.Slots.DayGrid;

        page.Anchors.Where(a => a.AnchorId.StartsWith("calendarDay:", StringComparison.Ordinal)).Should().HaveCount(31);
        IReadOnlyList<AnchorPlacement> weeks = page.Anchors.Where(a => a.AnchorId.StartsWith("calendarWeek:", StringComparison.Ordinal)).ToList();
        weeks.Select(a => a.AnchorId).Should().Equal("calendarWeek:01", "calendarWeek:02", "calendarWeek:03", "calendarWeek:04", "calendarWeek:05");
        weeks.Select(a => a.Text).Should().Equal("Week 01", "Week 02", "Week 03", "Week 04", "Week 05");
        for (int row = 0; row < 5; row++)
        {
            weeks[row].X.Should().BeApproximately((float)(grid.ColumnX[0] + M.Links.CalendarWeek.XOffset), 0.01f);
            weeks[row].Y.Should().BeApproximately((float)(grid.RowY[row] + M.Links.CalendarWeek.YOffset), 0.01f);
        }

        AnchorPlacement habits = page.Anchors.Single(a => a.AnchorId == "habitLegend");
        habits.Kind.Should().Be(AnchorKind.Propagated);
        habits.X.Should().Be((float)M.Slots.HabitLegend.Rect.X);

        IReadOnlyList<AnchorPlacement> events = page.Anchors.Where(a => a.AnchorId.StartsWith("calendarEvents:", StringComparison.Ordinal)).ToList();
        events.Should().HaveCount(31).And.OnlyContain(a => a.Kind == AnchorKind.Propagated && a.Opacity == (float)M.Colors.EventInkOpacity);
        AnchorPlacement jan4Events = events.Single(a => a.AnchorId == "calendarEvents:2027-01-04");
        RectPt box = grid.Box(1, 0);
        jan4Events.Y.Should().BeApproximately((float)(box.Bottom + M.Slots.CalendarEventLines.OffsetBelowBox), 0.01f);
        jan4Events.H.Should().BeApproximately((float)(M.Slots.CalendarEventLines.LineHeight * M.Slots.CalendarEventLines.MaxLines), 0.01f);

        page.Anchors.Should().Contain(a => a.AnchorId == "nextCalendar" && a.Text == "February 2027 ›");
        page.Anchors.Should().NotContain(a => a.AnchorId == "prevCalendar", "January is the first month");
        page.Anchors.Should().Contain(a => a.AnchorId == "calendarReview" && a.Text == "Review ›" && a.X == (float)M.Links.CalendarReview.X);
    }

    [Fact]
    public void November_2026_stacks_30_beside_the_linked_23()
    {
        GenerateOptions q4 = f.Q1Options with { Start = new QuarterSpec(2026, 10) };
        NotebookPlan plan = f.Plan(q4);
        PageKey november = PageKey.Calendar(2026, 11);
        RenderedPage page = f.Renderer.Render(plan.Page(november), plan, f.Options(q4));
        using SKBitmap sheet = RenderFixture.Decode(page);
        DayGridSlot grid = M.Slots.DayGrid;

        // Nov 1, 2026 is a Sunday: Nov 23 is the Monday of row 5 and Nov 30 stacks into its box.
        MonthGrid month = f.Engine.Grid(2026, 11, plan.Quarter);
        DayCell cell = month.Rows[4][0];
        cell.Date.Should().Be(new DateOnly(2026, 11, 23));
        cell.StackedDay.Should().Be(30);
        CalendarPage.BoxText(cell).Should().Be("23 / 30");

        AnchorPlacement link = page.Anchors.Single(a => a.AnchorId == "calendarDay:2026-11-23");
        link.Text.Should().Be("23");
        page.Anchors.Should().NotContain(a => a.AnchorId == "calendarDay:2026-11-30", "one anchor per box");
        RectPt box = grid.Box(4, 0);
        link.W.Should().BeLessThan((float)box.W, "the link covers only the 23 part of the box");
        double inside = Math.Max(link.X, box.X + 1.5);
        f.HasInk(sheet, new RectPt(inside, box.Y + 2, link.X + link.W - inside, box.H - 4)).Should().BeFalse("nothing is baked under the link (the probe starts right of the printed grid line)");
        f.HasInk(sheet, new RectPt(link.X + link.W, box.Y + 2, box.Right - link.X - link.W - 1.5, box.H - 4)).Should().BeTrue("/ 30 is baked to its right");

        RenderedPage dropped = f.Renderer.Render(plan.Page(november), plan, f.Options(q4 with { SixthRow = SixthRowMode.Drop }));
        using SKBitmap droppedSheet = RenderFixture.Decode(dropped);
        ColorParser.MaxChannelDistance(Pixels.Mean(droppedSheet, BoxInterior(4, 0), RenderFixture.Dpi), DayBox).Should().BeLessThanOrEqualTo(2, "with Drop the box holds only the link");
        dropped.Anchors.Single(a => a.AnchorId == "calendarDay:2026-11-23").W.Should().BeApproximately((float)M.Links.CalendarDay.W, 0.01f);
    }

    [Fact]
    public void March_2027_greys_the_April_days_of_its_last_row()
    {
        RenderedPage page = f.Render(PageKey.Calendar(2027, 3));
        using SKBitmap sheet = RenderFixture.Decode(page);

        // March 1, 2027 is a Monday, so row 5 runs Mar 29 to Apr 4: Thursday (column 3) is April 1.
        SKColor darkest = Pixels.Darkest(sheet, BoxInterior(4, 3), RenderFixture.Dpi);
        ((int)darkest.Red).Should().BeGreaterThan(f.Ink.Red + 60, "April 1 is greyed");
        ((int)darkest.Red).Should().BeLessThan(DayBox.Red - 40);
        page.Anchors.Should().NotContain(a => a.AnchorId.StartsWith("calendarDay:2027-04", StringComparison.Ordinal));
        page.Anchors.Where(a => a.AnchorId.StartsWith("calendarDay:", StringComparison.Ordinal)).Should().HaveCount(31);
        page.Anchors.Should().Contain(a => a.AnchorId == "prevCalendar" && a.Text == "‹ February 2027");
        page.Anchors.Should().NotContain(a => a.AnchorId == "nextCalendar");
    }

    /// <summary>Renders a page of the Q1 2027 plan (adjusted) from another PDF, the real planner for instance.</summary>
    private RenderedPage RenderReal(string pdf, PageKey key, Func<GenerateOptions, GenerateOptions>? adjust = null)
    {
        GenerateOptions options = adjust is null ? f.Q1Options : adjust(f.Q1Options);
        NotebookPlan plan = adjust is null ? f.Q1Plan : f.Plan(options);
        return f.Renderer.Render(plan.Page(key), plan, new RenderOptions(pdf, RenderFixture.Dpi, options));
    }
}
