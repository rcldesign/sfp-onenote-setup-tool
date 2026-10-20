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
/// beside the linked "23" so the box reads "23 / 30". Week labels, the habit legend band and the event lines under
/// each quarter day box are placements only.
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

        CalendarWeekAnchor weekAnchor = m.Links.CalendarWeek;
        foreach (LinkSpec link in c.LinksOfType(CalendarWeekAnchor))
        {
            int row = RowOfWeek(c, month, link);
            c.AddLink(link.AnchorId, weekAnchor.Rect(grid, row), link.Text, weekAnchor.Align, weekAnchor.SizePt);
        }

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
        double baseline = box.CenterY + (c.CapHeight(grid.SizePt) / 2);

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
                // "23 / 30": the linked day number sits where it would in the centered string, the stacked part is baked after it.
                double width = c.TextWidth(text, grid.SizePt);
                double dayWidth = c.TextWidth(day, grid.SizePt);
                double left = box.CenterX - (width / 2);
                c.AddLink(link.AnchorId, new RectPt(left - 1, rect.Y, dayWidth + 2, rect.H), link.Text, TextAlign.Center, anchor.SizePt);
                c.Text(text[day.Length..], left + dayWidth, baseline, TextAlign.Left, grid.SizePt);
            }
        }
        else if (cell.InMonth)
        {
            // Links are off for this day (GenerateOptions.Links false, or no page for it): the number is baked like the printed planner.
            c.Text(text, box.CenterX, baseline, TextAlign.Center, grid.SizePt);
        }
        else if (c.Generate.OutOfMonthDays == OutOfMonthDayStyle.Greyed)
        {
            c.Text(text, box.CenterX, baseline, TextAlign.Center, grid.SizePt, m.Colors.GreyedInkOpacity);
        }

        CalendarEventLinesSlot events = m.Slots.CalendarEventLines;
        if (cell.InMonth && cell.InQuarter && c.Applies(events.AppliesTo))
        {
            RectPt lines = new(box.X + events.XOffset, box.Bottom + events.OffsetBelowBox, events.W, events.LineHeight * events.MaxLines);
            c.AddPropagated(CalendarEventsSlot + ":" + iso, lines, events.SizePt, m.Colors.EventInkOpacity);
        }
    }

    /// <summary>The grid row whose Monday cell starts the week a calendarWeek:{NN} link names.</summary>
    private static int RowOfWeek(RenderContext c, MonthGrid month, LinkSpec link)
    {
        int number = int.Parse(link.AnchorInstance ?? "", CultureInfo.InvariantCulture);
        Week week = c.Dates.Weeks(c.Plan.Quarter).First(w => w.Number == number);
        for (int row = 0; row < month.Rows.Count; row++)
        {
            if (month.Rows[row][0].Date == week.MondayStart)
            {
                return row;
            }
        }

        throw new InvalidOperationException($"Calendar page {c.Page.Key} has no row starting on {week.MondayStart:yyyy-MM-dd} for link {link.AnchorId}.");
    }
}
