using System.Globalization;
using System.Text;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// The bundled holiday list as a calendar source: one all-day event per observed holiday date inside the window.
/// An observed shift can move a holiday into the previous year (New Year's Day 2028 falls on a Saturday and is observed
/// on Fri Dec 31, 2027), so neighboring years are evaluated as well.
/// </summary>
public sealed class HolidayCalendarSource : ICalendarSource
{
    /// <summary>Default list id, resolved by the factory to templates/holidays-us.json.</summary>
    public const string DefaultListId = "us";

    private readonly CalendarSource source;
    private readonly HolidayList list;

    public HolidayCalendarSource(CalendarSource source, HolidayList list)
    {
        this.source = source;
        this.list = list;
    }

    /// <summary>The source this reader was built for.</summary>
    public CalendarSource Source => source;

    /// <summary>The list in use.</summary>
    public HolidayList List => list;

    /// <inheritdoc />
    public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The window end must not precede its start.");
        }

        List<CalendarEvent> events = new();
        for (int year = from.Year - 1; year <= to.Year + 1; year++)
        {
            foreach ((DateOnly date, HolidayRule rule) in list.DatesIn(year))
            {
                if (date < from || date > to)
                {
                    continue;
                }

                DateTime start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                events.Add(new CalendarEvent(
                    Uid: string.Create(CultureInfo.InvariantCulture, $"holiday:{list.Id}:{Slug(rule.Name)}:{year}"),
                    Title: rule.Name,
                    Start: start,
                    End: start.AddDays(1),
                    AllDay: true,
                    IsPrivate: false,
                    Cancelled: false,
                    SourceLabel: source.Label,
                    ColorTag: source.ColorTag));
            }
        }

        return events.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lower-case name with runs of non-alphanumerics collapsed to one hyphen ("new-year-s-day").</summary>
    public static string Slug(string name)
    {
        StringBuilder sb = new(name.Length);
        bool pendingHyphen = false;
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                }

                pendingHyphen = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return sb.ToString();
    }
}
