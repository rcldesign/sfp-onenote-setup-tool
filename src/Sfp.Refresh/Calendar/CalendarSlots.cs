using System.Globalization;
using Sfp.Core.Plan;

namespace Sfp.Refresh.Calendar;

/// <summary>Slot ids the calendar import writes, matching the PRD placement table and the sidecar's anchor map.</summary>
public static class CalendarSlots
{
    /// <summary>Daily page: the all-day events and holidays band under the date.</summary>
    public const string AllDayBand = "allDayBand";

    /// <summary>Daily page: the Time Blocks row of an hour, "timeBlockEvent:9".</summary>
    public static string TimeBlockEvent(int hour) => string.Create(CultureInfo.InvariantCulture, $"timeBlockEvent:{hour}");

    /// <summary>Weekly Overview: the column of weekday index 0 (Monday) to 6 (Sunday), "overviewDay:1".</summary>
    public static string OverviewDay(int weekdayIndex) => string.Create(CultureInfo.InvariantCulture, $"overviewDay:{weekdayIndex}");

    /// <summary>Monthly Calendar: the lines under a day box, "calendarEvents:2027-01-04".</summary>
    public static string CalendarEvents(DateOnly date) => "calendarEvents:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Monday-first weekday index of a date, 0 to 6.</summary>
    public static int WeekdayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
}

/// <summary>A (page, slot) pair: the identity of one tool-owned calendar text.</summary>
public readonly record struct SlotRef(PageKey Page, string SlotId)
{
    public override string ToString() => Page.Value + "/" + SlotId;
}

/// <summary>The text the placement engine wants in one slot for one day; empty text means the slot should be empty.</summary>
/// <param name="Page">Target page.</param>
/// <param name="SlotId">Slot id on that page (see <see cref="CalendarSlots"/>).</param>
/// <param name="Date">The planner day the slot belongs to, which decides whether it is frozen.</param>
/// <param name="Text">Desired text, lines separated by "\n"; empty when nothing should be shown.</param>
public sealed record CalendarSlotText(PageKey Page, string SlotId, DateOnly Date, string Text)
{
    public SlotRef Ref => new(Page, SlotId);

    /// <summary>As a Create change with no previous text (the diff turns these into Create, Update or Remove).</summary>
    public PlannedChange ToPlannedChange() => new(Page, SlotId, null, Text, ChangeKind.Create, false, null);
}
