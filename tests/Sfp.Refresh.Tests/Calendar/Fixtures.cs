using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>Paths of the .ics fixtures copied beside the test assembly.</summary>
public static class Fixtures
{
    public static string Ics(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "calendar", name);

    public static CalendarSource FileSource(string name, string label = "Work", string? calendarName = null) =>
        new(label, CalendarSourceKind.IcsFile, Ics(name), null, calendarName);

    public static CalendarImportOptions NewYorkOptions(bool maskPrivate = true) =>
        new() { LocalZone = TestZones.NewYork, MaskPrivate = maskPrivate };
}
