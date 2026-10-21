using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>
/// refresh-14: a TZID that is neither an IANA nor a Windows zone id is read from the file's own VTIMEZONE block, and
/// without one the source fails naming the TZID; it is never read as UTC.
/// </summary>
public class IcsTimeZoneTests
{
    private static readonly CalendarSource Work = new("Work", CalendarSourceKind.IcsUrl, "https://example.com/cal.ics", null, null);

    /// <summary>A weekly 9:00 meeting on four Mondays from Mar 1, 2027 (US daylight time starts Mar 14), in <paramref name="tzid"/>.</summary>
    private static string Ics(string tzid, bool withDefinition)
    {
        List<string> lines = new() { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:Microsoft Exchange Server 2010" };
        if (withDefinition)
        {
            lines.AddRange(new[]
            {
                "BEGIN:VTIMEZONE", "TZID:" + tzid,
                "BEGIN:STANDARD", "DTSTART:16010101T020000", "TZOFFSETFROM:-0400", "TZOFFSETTO:-0500", "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=1SU;BYMONTH=11", "END:STANDARD",
                "BEGIN:DAYLIGHT", "DTSTART:16010101T020000", "TZOFFSETFROM:-0500", "TZOFFSETTO:-0400", "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=2SU;BYMONTH=3", "END:DAYLIGHT",
                "END:VTIMEZONE",
            });
        }

        lines.AddRange(new[]
        {
            "BEGIN:VEVENT", "UID:o1", "DTSTAMP:20270101T000000Z",
            "DTSTART;TZID=\"" + tzid + "\":20270301T090000", "DTEND;TZID=\"" + tzid + "\":20270301T100000",
            "RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=4", "SUMMARY:Staff meeting", "END:VEVENT", "END:VCALENDAR", "",
        });
        return string.Join("\r\n", lines);
    }

    private static IReadOnlyList<string> Starts(string ics)
    {
        using IcsCalendarSource source = new(Work, _ => ics, Fixtures.NewYorkOptions());
        return source.Fetch(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31)).Select(e => e.Start.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    [Theory]
    [InlineData("(GMT-05.00) Eastern Time (US & Canada)")]
    [InlineData("(UTC-05:00) Eastern Time (US & Canada)")]
    [InlineData("Custom Zone 1")]
    public void A_display_name_zone_is_read_from_the_files_VTIMEZONE_block(string tzid) =>
        Starts(Ics(tzid, withDefinition: true)).Should().Equal("03-01 09:00", "03-08 09:00", "03-15 09:00", "03-22 09:00");

    [Theory]
    [InlineData("Eastern Standard Time")]
    [InlineData("America/New_York")]
    [InlineData("US/Eastern")]
    public void IANA_and_Windows_ids_still_come_from_the_zone_database(string tzid) =>
        Starts(Ics(tzid, withDefinition: false)).Should().Equal("03-01 09:00", "03-08 09:00", "03-15 09:00", "03-22 09:00");

    [Theory]
    [InlineData("(GMT-05.00) Eastern Time (US & Canada)")]
    [InlineData("Custom Zone 1")]
    public void An_unknown_zone_without_a_definition_fails_the_source_naming_it(string tzid)
    {
        Action fetch = () => Starts(Ics(tzid, withDefinition: false));

        fetch.Should().Throw<InvalidDataException>().WithMessage("*'" + tzid + "' is neither an IANA nor a Windows zone id*");
    }
}
