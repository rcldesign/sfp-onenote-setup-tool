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
        link.X.Should().BeGreaterThanOrEqualTo((float)box.X, "the link starts inside the box");
        (link.X + link.W).Should().BeLessThanOrEqualTo((float)box.Right, "the link ends inside the box");
        double inside = Math.Max(link.X, box.X + 1.5);
        f.HasInk(sheet, new RectPt(inside, box.Y + 2, link.X + link.W - inside, box.H - 4)).Should().BeFalse("nothing is baked under the link (the probe starts right of the printed grid line)");
        f.HasInk(sheet, new RectPt(link.X + link.W, box.Y + 2, box.Right - link.X - link.W - 1.5, box.H - 4)).Should().BeTrue("/ 30 is baked to its right");
        f.HasInk(sheet, new RectPt(box.Right + 1.5, box.Y + 2, 4, box.H - 4)).Should().BeFalse("/ 30 ends inside the box, short of its right edge");

        RenderedPage dropped = f.Renderer.Render(plan.Page(november), plan, f.Options(q4 with { SixthRow = SixthRowMode.Drop }));
        using SKBitmap droppedSheet = RenderFixture.Decode(dropped);
        ColorParser.MaxChannelDistance(Pixels.Mean(droppedSheet, BoxInterior(4, 0), RenderFixture.Dpi), DayBox).Should().BeLessThanOrEqualTo(2, "with Drop the box holds only the link");
        dropped.Anchors.Single(a => a.AnchorId == "calendarDay:2026-11-23").W.Should().BeApproximately((float)M.Links.CalendarDay.W, 0.01f);
    }

    /// <summary>
    /// Adversarial review render-2: on the real planner a stacked sixth-row box keeps its ink and its link inside the day
    /// box, so nothing crosses the printed grid rules: November 2026 (the PRD's example, "23 / 30") and August 2027 in Q3
    /// 2027 ("23 / 30" and "24 / 31"), links on and off. The next box of that row is an ordinary one and keeps the
    /// anchor's own rectangle and size, or its full-size number centered on the usual baseline.
    /// </summary>
    [SkippableTheory]
    [InlineData(2026, 10, 11, true)]
    [InlineData(2026, 10, 11, false)]
    [InlineData(2027, 7, 8, true)]
    [InlineData(2027, 7, 8, false)]
    public void On_the_real_planner_stacked_boxes_keep_their_ink_and_links_inside_the_day_box(int year, int startMonth, int month, bool links)
    {
        string pdf = SfpPdf.RequireOrSkip();
        GenerateOptions options = f.Q1Options with { Start = new QuarterSpec(year, startMonth), Links = links };
        NotebookPlan plan = f.Plan(options);
        PageSpec spec = plan.Page(PageKey.Calendar(year, month));
        RenderedPage page = f.Renderer.Render(spec, plan, new RenderOptions(pdf, RenderFixture.Dpi, options));
        using SKBitmap sheet = RenderFixture.Decode(page);
        using SKBitmap raw = f.Rasterizer.Render(pdf, spec.PdfPage!.Value, RenderFixture.Dpi);
        DayGridSlot grid = M.Slots.DayGrid;
        CalendarDayAnchor anchor = M.Links.CalendarDay;
        MonthGrid cells = f.Engine.Grid(year, month, plan.Quarter);
        int last = cells.Rows.Count - 1;
        IReadOnlyList<DayCell> row = cells.Rows[last];
        int stacked = row.Count(c => c.StackedDay is not null);
        stacked.Should().Be(month == 8 ? 2 : 1, "August 2027 stacks 30 and 31 into its last row, November 2026 stacks 30");

        for (int column = 0; column < stacked; column++)
        {
            RectPt box = grid.Box(last, column);
            string label = CalendarPage.BoxText(row[column]);

            // The render changes no pixel within 5 pt of a day box but the box's own number: the week label and the
            // neighboring boxes sit further away, and links and event lines are not baked.
            RectPt ink = Pixels.ChangedBounds(raw, sheet, Grow(box, 5), RenderFixture.Dpi) ?? throw new InvalidOperationException($"Nothing drawn in the box of '{label}'.");
            Inside(ink, box).Should().BeTrue($"the ink of '{label}' ({Describe(ink)}) stays inside its day box ({Describe(box)})");

            if (links)
            {
                AnchorPlacement link = page.Anchors.Single(a => a.AnchorId == "calendarDay:" + Iso(row[column].Date));
                RectPt rect = new(link.X, link.Y, link.W, link.H);
                Inside(rect, box).Should().BeTrue($"the link of '{label}' ({Describe(rect)}) stays inside its day box ({Describe(box)})");
                Pixels.HasInk(raw, rect, RenderFixture.Dpi, DayBox).Should().BeFalse($"no printed grid rule lies under the link of '{label}'");
            }
        }

        DayCell ordinary = row[stacked];
        ordinary.StackedDay.Should().BeNull();
        RectPt plain = grid.Box(last, stacked);
        RectPt? drawn = Pixels.ChangedBounds(raw, sheet, Grow(plain, 5), RenderFixture.Dpi);
        if (links)
        {
            AnchorPlacement link = page.Anchors.Single(a => a.AnchorId == "calendarDay:" + Iso(ordinary.Date));
            RectPt expected = anchor.Rect(grid, last, stacked);
            new[] { link.X, link.Y, link.W, link.H }.Should().Equal(new[] { (float)expected.X, (float)expected.Y, (float)expected.W, (float)expected.H }, "an ordinary box's link is the anchor's own rectangle");
            link.SizePt.Should().Be((float)anchor.SizePt);
            link.Align.Should().Be(anchor.Align);
            drawn.Should().BeNull("nothing is baked in an ordinary box with links on");
        }
        else
        {
            string day = ordinary.Date.Day.ToString(CultureInfo.InvariantCulture);
            RectPt expected = InkOf(day, grid.SizePt, plain.CenterX, plain.CenterY + (CapHeight(grid.SizePt) / 2));
            drawn.Should().NotBeNull($"{day} is baked with links off");
            RectPt found = drawn!.Value;
            string seen = $"{day} is drawn at {Describe(found)}, its glyphs at the grid size centered in the box ink {Describe(expected)}";
            found.X.Should().BeApproximately(expected.X, 0.7, seen);
            found.Right.Should().BeApproximately(expected.Right, 0.7, seen);
            found.Y.Should().BeApproximately(expected.Y, 0.7, seen);
            found.Bottom.Should().BeApproximately(expected.Bottom, 0.7, seen);
        }
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

    private static RectPt Grow(RectPt r, double by) => new(r.X - by, r.Y - by, r.W + (2 * by), r.H + (2 * by));

    private static bool Inside(RectPt inner, RectPt outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static string Describe(RectPt r) =>
        string.Create(CultureInfo.InvariantCulture, $"x {r.X:0.##} to {r.Right:0.##}, y {r.Y:0.##} to {r.Bottom:0.##}");

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private double CapHeight(double sizePt)
    {
        using SKFont font = f.Fonts.Font(sizePt);
        return font.Metrics.CapHeight;
    }

    /// <summary>
    /// Where a text run centered on <paramref name="centerX"/> inks, in points: the tight bounds of its glyph outlines at
    /// the size (the measured glyph boxes overshoot curved digits by most of a point).
    /// </summary>
    private RectPt InkOf(string text, double sizePt, double centerX, double baseline)
    {
        using SKFont font = f.Fonts.Font(sizePt);
        double left = centerX - (font.MeasureText(text) / 2);
        using SKPath outline = font.GetTextPath(text, new SKPoint((float)left, (float)baseline));
        SKRect ink = outline.TightBounds;
        return new RectPt(ink.Left, ink.Top, ink.Width, ink.Height);
    }

    /// <summary>Renders a page of the Q1 2027 plan (adjusted) from another PDF, the real planner for instance.</summary>
    private RenderedPage RenderReal(string pdf, PageKey key, Func<GenerateOptions, GenerateOptions>? adjust = null)
    {
        GenerateOptions options = adjust is null ? f.Q1Options : adjust(f.Q1Options);
        NotebookPlan plan = adjust is null ? f.Q1Plan : f.Plan(options);
        return f.Renderer.Render(plan.Page(key), plan, new RenderOptions(pdf, RenderFixture.Dpi, options));
    }
}
