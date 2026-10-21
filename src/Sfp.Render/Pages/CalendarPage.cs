using System.Globalization;
using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.5: the Monthly Calendar page. The month name goes at slots.monthName; each cell of the engine's 5 by 7 grid gets
/// its day number: an in-month quarter day is a calendarDay link (never baked), an out-of-month day is baked at the
/// greyed opacity or left blank (GenerateOptions.OutOfMonthDays), and a stacked sixth-row day is baked as " / 30"
/// beside the linked "23" so the box reads "23 / 30", set smaller when needed so the whole string fits inside the day
/// box less slots.dayGrid.stackedInsetPt on each side. The "Week NN" labels are calendarWeek link placements, or baked
/// text when the page has no link for them (links off); the habit legend band and the event lines under each quarter
/// day box are placements only.
/// </summary>
internal static class CalendarPage
{
    public const string CalendarDayAnchor = "calendarDay";
    public const string CalendarWeekAnchor = "calendarWeek";
    public const string HabitLegendSlot = "habitLegend";
    public const string CalendarEventsSlot = "calendarEvents";
    public const string StackSeparator = " / ";

    public static void Draw(RenderContext c)
    {
        DateOnly first = c.Page.Date ?? throw new InvalidOperationException($"Calendar page {c.Page.Key} has no date.");
        Manifest m = c.Manifest;
        MonthNameSlot name = m.Slots.MonthName;
        if (c.Applies(name.AppliesTo))
        {
            c.Text(PlannerText.MonthYear(first.Year, first.Month), name.X, name.BaselineY, TextAlign.Left, name.SizePt);
        }

        DayGridSlot grid = m.Slots.DayGrid;
        MonthGrid month = c.Dates.GridWithMode(first.Year, first.Month, c.Plan.Quarter, c.Generate.SixthRow);
        if (c.Applies(grid.AppliesTo))
        {
            for (int row = 0; row < month.Rows.Count && row < grid.RowY.Count; row++)
            {
                for (int column = 0; column < month.Rows[row].Count && column < grid.ColumnX.Count; column++)
                {
                    DrawCell(c, grid, row, column, month.Rows[row][column]);
                }
            }
        }

        WeekLabels(c, grid, month);

        HabitLegendSlot habits = m.Slots.HabitLegend;
        if (c.Applies(habits.AppliesTo))
        {
            c.AddPropagated(HabitLegendSlot, habits.Rect, habits.SizePt);
        }
    }

    /// <summary>The number drawn in a box: "4", or "23 / 30" for a stacked cell.</summary>
    public static string BoxText(DayCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        string day = cell.Date.Day.ToString(CultureInfo.InvariantCulture);
        return cell.StackedDay is int stacked ? day + StackSeparator + stacked.ToString(CultureInfo.InvariantCulture) : day;
    }

    private static void DrawCell(RenderContext c, DayGridSlot grid, int row, int column, DayCell cell)
    {
        Manifest m = c.Manifest;
        RectPt box = grid.Box(row, column);
        string iso = cell.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string day = cell.Date.Day.ToString(CultureInfo.InvariantCulture);
        string text = BoxText(cell);
        double size = cell.StackedDay is null ? grid.SizePt : StackedSize(c, grid, text);
        double baseline = box.CenterY + (c.CapHeight(size) / 2);

        LinkSpec? link = cell.InMonth && cell.InQuarter ? c.Link(CalendarDayAnchor + ":" + iso) : null;
        if (link is not null)
        {
            CalendarDayAnchor anchor = m.Links.CalendarDay;
            RectPt rect = anchor.Rect(grid, row, column);
            if (cell.StackedDay is null)
            {
                c.AddLink(link.AnchorId, rect, link.Text, anchor.Align, anchor.SizePt);
            }
            else
            {
                // "23 / 30": the linked day number covers the "23" of the centered string as drawn at its fitted size, in
                // that size, with a point of air either side for OneNote's text (no more than the inset, so the link stays
                // inside the box); the stacked part is baked after it.
                double width = c.TextWidth(text, size);
                double dayWidth = c.TextWidth(day, size);
                double left = box.CenterX - (width / 2);
                double pad = Math.Min(1, grid.StackedInsetPt);
                c.AddLink(link.AnchorId, new RectPt(left - pad, rect.Y, dayWidth + (2 * pad), rect.H), link.Text, TextAlign.Center, size);
                c.Text(text[day.Length..], left + dayWidth, baseline, TextAlign.Left, size);
            }
        }
        else if (cell.InMonth)
        {
            // Links are off for this day (GenerateOptions.Links false, or no page for it): the number is baked like the printed planner.
            c.Text(text, box.CenterX, baseline, TextAlign.Center, size);
        }
        else if (c.Generate.OutOfMonthDays == OutOfMonthDayStyle.Greyed)
        {
            c.Text(text, box.CenterX, baseline, TextAlign.Center, size, m.Colors.GreyedInkOpacity);
        }

        CalendarEventLinesSlot events = m.Slots.CalendarEventLines;
        if (cell.InMonth && cell.InQuarter && c.Applies(events.AppliesTo))
        {
            RectPt lines = new(box.X + events.XOffset, box.Bottom + events.OffsetBelowBox, events.W, events.LineHeight * events.MaxLines);
            c.AddPropagated(CalendarEventsSlot + ":" + iso, lines, events.SizePt, m.Colors.EventInkOpacity);
        }
    }

    /// <summary>
    /// The size of a stacked string ("23 / 30"): the day grid's size, or, when the string is wider than the day box less
    /// slots.dayGrid.stackedInsetPt on each side, the size that makes it exactly that wide (text widths scale linearly).
    /// </summary>
    private static double StackedSize(RenderContext c, DayGridSlot grid, string text)
    {
        double room = grid.BoxPt - (2 * grid.StackedInsetPt);
        double width = c.TextWidth(text, grid.SizePt);
        return width <= room ? grid.SizePt : grid.SizePt * room / width;
    }

    /// <summary>
    /// The "Week NN" label of every grid row whose Monday cell starts a week of the quarter: the calendarWeek:{NN} link
    /// placement, or, with no link for it (links off), the label baked where the link would sit, in the link's size and
    /// alignment, so the calendar still numbers its weeks.
    /// </summary>
    private static void WeekLabels(RenderContext c, DayGridSlot grid, MonthGrid month)
    {
        CalendarWeekAnchor anchor = c.Manifest.Links.CalendarWeek;
        IReadOnlyList<Week> weeks = c.Dates.Weeks(c.Plan.Quarter);
        for (int row = 0; row < month.Rows.Count && row < grid.RowY.Count; row++)
        {
            DateOnly monday = month.Rows[row][0].Date;
            Week? week = weeks.FirstOrDefault(w => w.MondayStart == monday);
            if (week is null)
            {
                continue;
            }

            RectPt rect = anchor.Rect(grid, row);
            LinkSpec? link = c.Link(CalendarWeekAnchor + ":" + PlannerText.WeekNumber(week.Number));
            if (link is not null)
            {
                c.AddLink(link.AnchorId, rect, link.Text, anchor.Align, anchor.SizePt);
            }
            else
            {
                c.TextIn(rect, LinkGraphBuilder.WeekLabel(week.Number), anchor.Align, anchor.SizePt);
            }
        }
    }
}
