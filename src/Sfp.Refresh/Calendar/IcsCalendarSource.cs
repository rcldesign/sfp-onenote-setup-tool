using System.Globalization;
using Ical.Net;
using Ical.Net.DataTypes;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Reads an iCalendar (.ics) URL or file with Ical.Net 5, expands recurrences over the requested window and
/// normalizes every instance to local wall-clock time. One production implementation of <see cref="ICalendarSource"/>
/// for the IcsUrl and IcsFile source kinds.
/// </summary>
/// <remarks>
/// Rules: an occurrence is AllDay when DTSTART is DATE-valued; IsPrivate comes from CLASS:PRIVATE or CONFIDENTIAL and
/// the title is masked unless <see cref="CalendarImportOptions.MaskPrivate"/> is off; Cancelled comes from
/// STATUS:CANCELLED (an EXDATE instance simply never appears); the Uid is the UID plus the RECURRENCE-ID when the
/// instance is an override, otherwise the occurrence start, so an override and the instance it replaces share one Uid.
/// Floating times (no TZID, not UTC) are taken as already local.
/// </remarks>
public sealed class IcsCalendarSource : ICalendarSource, IDisposable
{
    /// <summary>How long a URL fetch may take before the source counts as failed.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private const string CalendarNameProperty = "X-WR-CALNAME";
    private static readonly string[] UrlSchemes = { "http", "https", "webcal" };

    private readonly CalendarSource source;
    private readonly CalendarImportOptions options;
    private readonly Func<string, string>? fetchContent;
    private HttpClient? http;
    private bool ownsHttp;

    /// <summary>Production constructor: URLs are fetched with <paramref name="http"/> (or a client created on first use), file paths are read directly.</summary>
    public IcsCalendarSource(CalendarSource source, CalendarImportOptions? options = null, HttpClient? http = null)
    {
        this.source = source;
        this.options = options ?? CalendarImportOptions.Default;
        this.http = http;
    }

    /// <summary>Test constructor: every location (URL or path) is resolved through <paramref name="fetchContent"/>.</summary>
    public IcsCalendarSource(CalendarSource source, Func<string, string> fetchContent, CalendarImportOptions? options = null)
    {
        this.source = source;
        this.fetchContent = fetchContent;
        this.options = options ?? CalendarImportOptions.Default;
    }

    /// <summary>The source this reader was built for.</summary>
    public CalendarSource Source => source;

    /// <summary>True when the location is an http, https or webcal URL rather than a file path.</summary>
    public static bool IsUrl(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) && UrlSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>webcal:// is an https feed under another name.</summary>
    public static Uri NormalizeUrl(string location)
    {
        Uri uri = new(location, UriKind.Absolute);
        if (string.Equals(uri.Scheme, "webcal", StringComparison.OrdinalIgnoreCase))
        {
            UriBuilder b = new(uri) { Scheme = "https", Port = -1 };
            return b.Uri;
        }

        return uri;
    }

    /// <inheritdoc />
    public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The window end must not precede its start.");
        }

        string text = LoadContent();
        return ParseEvents(text, from, to);
    }

    /// <summary>
    /// Parses iCalendar text already in hand (no I/O) into the events that touch [<paramref name="from"/>, <paramref name="to"/>].
    /// Text without a BEGIN:VCALENDAR line (an empty file or response, a file caught mid-rewrite, an error page) throws
    /// <see cref="InvalidDataException"/>: it is a failed fetch, never an empty calendar whose events would all be removed.
    /// </summary>
    public IReadOnlyList<CalendarEvent> ParseEvents(string icsText, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(icsText);
        if (icsText.IndexOf("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidDataException("The source returned no calendar data: the text is empty or has no BEGIN:VCALENDAR line.");
        }

        CalendarCollection calendars = CalendarCollection.Load(icsText) ?? new CalendarCollection();
        IEnumerable<Ical.Net.Calendar> selected = calendars;
        if (!string.IsNullOrWhiteSpace(source.CalendarName))
        {
            selected = calendars.Where(c => string.Equals(CalendarNameOf(c), source.CalendarName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Expand with a generous margin (a month-long all-day event may start well before the window) and filter by
        // the local days each instance covers afterwards, which also removes any time zone edge cases at the window ends.
        CalDateTime expandFrom = new(from.AddDays(-31));
        CalDateTime expandTo = new(to.AddDays(2));

        List<CalendarEvent> events = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (Ical.Net.Calendar calendar in selected)
        {
            foreach (Occurrence occurrence in calendar.GetOccurrences(expandFrom).TakeWhileBefore(expandTo))
            {
                if (occurrence.Source is not Ical.Net.CalendarComponents.CalendarEvent vevent)
                {
                    continue;
                }

                CalendarEvent? e = ToEvent(vevent, occurrence.Period);
                if (e is null || !TouchesWindow(e, from, to) || !seen.Add(e.Uid))
                {
                    continue;
                }

                events.Add(e);
            }
        }

        return events
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose()
    {
        if (ownsHttp)
        {
            http?.Dispose();
            http = null;
            ownsHttp = false;
        }
    }

    private static string? CalendarNameOf(Ical.Net.Calendar calendar)
    {
        try
        {
            return calendar.Properties.Get<string>(CalendarNameProperty);
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static bool TouchesWindow(CalendarEvent e, DateOnly from, DateOnly to)
    {
        if (!e.AllDay)
        {
            return e.Date >= from && e.Date <= to;
        }

        return e.Date <= to && e.AllDayEndExclusive() > from;
    }

    private string LoadContent()
    {
        string location = source.Location;
        if (fetchContent is not null)
        {
            return fetchContent(location);
        }

        if (IsUrl(location))
        {
            if (http is null)
            {
                http = new HttpClient { Timeout = FetchTimeout };
                ownsHttp = true;
            }

            return http.GetStringAsync(NormalizeUrl(location)).GetAwaiter().GetResult();
        }

        return File.ReadAllText(location);
    }

    private CalendarEvent? ToEvent(Ical.Net.CalendarComponents.CalendarEvent vevent, Period period)
    {
        CalDateTime startCal = period.StartTime;
        bool allDay = !startCal.HasTime;
        DateTime start = ToLocal(startCal);
        CalDateTime? endCal = period.EffectiveEndTime;
        DateTime end = endCal is null ? start : ToLocal(endCal);
        if (allDay)
        {
            start = start.Date;
            end = end.Date;
            if (end <= start)
            {
                end = start.AddDays(1);
            }
        }
        else if (end < start)
        {
            end = start;
        }

        bool isPrivate = IsPrivateClass(vevent.Class);
        bool cancelled = string.Equals(vevent.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
        string title = (vevent.Summary ?? "").Trim();
        if (title.Length == 0)
        {
            title = options.UntitledTitle;
        }

        if (isPrivate && options.MaskPrivate)
        {
            title = options.PrivateTitle;
        }

        CalDateTime? recurrenceId = vevent.RecurrenceIdentifier?.StartTime;
        string instanceKey = recurrenceId is null
            ? InstanceKey(start, allDay)
            : InstanceKey(ToLocal(recurrenceId), !recurrenceId.HasTime);
        string baseUid = string.IsNullOrWhiteSpace(vevent.Uid) ? "no-uid" : vevent.Uid.Trim();

        return new CalendarEvent(
            Uid: baseUid + "@" + instanceKey,
            Title: title,
            Start: start,
            End: end,
            AllDay: allDay,
            IsPrivate: isPrivate,
            Cancelled: cancelled,
            SourceLabel: source.Label,
            ColorTag: source.ColorTag);
    }

    private static bool IsPrivateClass(string? cls) =>
        cls is not null && (cls.Equals("PRIVATE", StringComparison.OrdinalIgnoreCase) || cls.Equals("CONFIDENTIAL", StringComparison.OrdinalIgnoreCase));

    private static string InstanceKey(DateTime local, bool dateOnly) =>
        dateOnly
            ? local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Wall-clock time in the configured zone, Kind Unspecified. DATE values and floating times pass through unchanged.</summary>
    private DateTime ToLocal(CalDateTime dt)
    {
        if (!dt.HasTime || dt.IsFloating)
        {
            return DateTime.SpecifyKind(dt.Value, DateTimeKind.Unspecified);
        }

        DateTime utc = DateTime.SpecifyKind(dt.AsUtc, DateTimeKind.Utc);
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, options.LocalZone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }
}
