using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Read-only reader of the local Outlook calendar through its COM object model (MAPI), late-bound through
/// <c>dynamic</c> so the project compiles on any OS. Only runs on Windows with Outlook installed; callers check
/// <see cref="OutlookAvailability.IsInstalled"/> first (the factory does). Exercised only by a COM-category test.
/// </summary>
/// <remarks>
/// Outlook constants used: olFolderCalendar = 9, OlSensitivity.olPrivate = 2, OlMeetingStatus.olMeetingCanceled = 5 and
/// olMeetingReceivedAndCanceled = 7. Recurring series are expanded by Outlook itself (Items.IncludeRecurrences with a
/// Restrict filter, walked with GetFirst/GetNext as the object model requires). Times come back as local wall-clock.
/// </remarks>
public sealed class OutlookCalendarSource : ICalendarSource
{
    private const int OlFolderCalendar = 9;
    private const int OlSensitivityPrivate = 2;
    private const int OlMeetingCanceled = 5;
    private const int OlMeetingReceivedAndCanceled = 7;

    private readonly CalendarSource source;
    private readonly CalendarImportOptions options;

    public OutlookCalendarSource(CalendarSource source, CalendarImportOptions? options = null)
    {
        this.source = source;
        this.options = options ?? CalendarImportOptions.Default;
    }

    /// <summary>The source this reader was built for.</summary>
    public CalendarSource Source => source;

    /// <inheritdoc />
    public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The window end must not precede its start.");
        }

        if (!OperatingSystem.IsWindows() || !OutlookAvailability.IsInstalled)
        {
            throw new InvalidOperationException("Outlook desktop is not installed on this machine (the Outlook.Application ProgID did not resolve), so the Outlook calendar source cannot be read.");
        }

        return FetchFromOutlook(from, to);
    }

    /// <summary>The Restrict filter Outlook evaluates: items that overlap the window. Outlook parses the dates with the US pattern.</summary>
    public static string RestrictFilter(DateOnly from, DateOnly to)
    {
        string start = from.ToDateTime(TimeOnly.MinValue).ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture);
        string end = to.AddDays(1).ToDateTime(TimeOnly.MinValue).ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture);
        return $"[Start] < '{end}' AND [End] >= '{start}'";
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<CalendarEvent> FetchFromOutlook(DateOnly from, DateOnly to)
    {
        Type type = Type.GetTypeFromProgID(OutlookAvailability.ProgId)
            ?? throw new InvalidOperationException("Outlook.Application is not registered.");
        object app = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Outlook.Application could not be created.");
        try
        {
            dynamic application = app;
            dynamic session = application.GetNamespace("MAPI");
            dynamic folder = session.GetDefaultFolder(OlFolderCalendar);
            if (!string.IsNullOrWhiteSpace(source.CalendarName))
            {
                folder = FindCalendarFolder(folder, source.CalendarName.Trim())
                    ?? throw new InvalidOperationException($"Outlook has no calendar folder named '{source.CalendarName}'.");
            }

            dynamic items = folder.Items;
            items.IncludeRecurrences = true;
            items.Sort("[Start]");
            dynamic restricted = items.Restrict(RestrictFilter(from, to));

            List<CalendarEvent> events = new();
            dynamic? item = restricted.GetFirst();
            while (item is not null)
            {
                CalendarEvent? e = ToEvent(item);
                if (e is not null && e.Date >= from.AddDays(-1) && e.Date <= to)
                {
                    events.Add(e);
                }

                item = restricted.GetNext();
            }

            return events.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
        }
        finally
        {
            Marshal.FinalReleaseComObject(app);
        }
    }

    [SupportedOSPlatform("windows")]
    private static dynamic? FindCalendarFolder(dynamic defaultCalendar, string name)
    {
        if (string.Equals((string)defaultCalendar.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return defaultCalendar;
        }

        dynamic subfolders = defaultCalendar.Folders;
        int count = (int)subfolders.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic sub = subfolders[i];
            if (string.Equals((string)sub.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sub;
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private CalendarEvent? ToEvent(dynamic item)
    {
        string subject = ((string?)item.Subject ?? "").Trim();
        DateTime start = (DateTime)item.Start;
        DateTime end = (DateTime)item.End;
        bool allDay = (bool)item.AllDayEvent;
        bool isPrivate = (int)item.Sensitivity == OlSensitivityPrivate;
        int meetingStatus = (int)item.MeetingStatus;
        bool cancelled = meetingStatus is OlMeetingCanceled or OlMeetingReceivedAndCanceled;
        string id = (string?)item.GlobalAppointmentID ?? (string?)item.EntryID ?? "no-id";

        start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        end = DateTime.SpecifyKind(end, DateTimeKind.Unspecified);
        if (allDay)
        {
            start = start.Date;
            end = end.Date <= start ? start.AddDays(1) : end.Date;
        }

        if (subject.Length == 0)
        {
            subject = options.UntitledTitle;
        }

        if (isPrivate && options.MaskPrivate)
        {
            subject = options.PrivateTitle;
        }

        string key = allDay
            ? start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : start.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        return new CalendarEvent(id + "@" + key, subject, start, end, allDay, isPrivate, cancelled, source.Label, source.ColorTag);
    }
}
