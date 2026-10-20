namespace Sfp.Refresh.Calendar;

/// <summary>Day-coverage rules shared by the sources and the placement engine.</summary>
public static class CalendarEventExtensions
{
    /// <summary>
    /// True when the event belongs on the given local day. A timed event belongs to the day it starts on only (one that
    /// runs past midnight is still listed once, at its start). An all-day event covers every day from its start date up to
    /// its exclusive end date, so a three-day conference appears on all three daily pages.
    /// </summary>
    public static bool CoversDay(this CalendarEvent e, DateOnly day)
    {
        if (!e.AllDay)
        {
            return e.Date == day;
        }

        DateOnly lastExclusive = e.AllDayEndExclusive();
        return day >= e.Date && day < lastExclusive;
    }

    /// <summary>The exclusive end date of an all-day event (at least the day after its start).</summary>
    public static DateOnly AllDayEndExclusive(this CalendarEvent e)
    {
        DateOnly end = DateOnly.FromDateTime(e.End);
        if (e.End.TimeOfDay > TimeSpan.Zero)
        {
            end = end.AddDays(1);
        }

        return end > e.Date ? end : e.Date.AddDays(1);
    }

    /// <summary>The local hour at which the event stops covering hourly rows (exclusive), capped at the end of its start day.</summary>
    public static int LastCoveredHourExclusive(this CalendarEvent e)
    {
        DateTime dayEnd = e.Start.Date.AddDays(1);
        DateTime end = e.End > dayEnd ? dayEnd : e.End;
        if (end <= e.Start)
        {
            return e.Start.Hour + 1;
        }

        double hours = (end - e.Start.Date).TotalHours;
        return (int)Math.Ceiling(hours);
    }
}
