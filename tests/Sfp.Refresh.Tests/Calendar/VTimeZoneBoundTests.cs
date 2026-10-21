using System.Diagnostics;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>
/// Round 3 refresh-5: a VTIMEZONE block is expanded only over the fetch window plus a year on each side, and a block that
/// changes its offset too often fails the source naming its TZID, so a broken or hostile feed cannot stall the run while
/// it holds the notebook lock.
/// </summary>
public class VTimeZoneBoundTests
{
    private static readonly CalendarSource Work = new("Work", CalendarSourceKind.IcsUrl, "https://example.com/cal.ics", null, null);

    [Fact]
    public void An_hourly_observance_from_1601_is_expanded_over_the_window_only_and_converts_the_event()
    {
        Stopwatch clock = Stopwatch.StartNew();

        IReadOnlyList<CalendarEvent> events = Fetch("RRULE:FREQ=HOURLY;INTERVAL=1");

        clock.Stop();
        events.Should().ContainSingle().Which.Start.Should().Be(new DateTime(2027, 3, 1, 9, 0, 0));
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "expanding hourly onsets from 1601 to 2200 took over 15 s");
    }

    [Fact]
    public void A_yearly_observance_from_1601_still_places_the_event_after_the_change_to_daylight_time()
    {
        IReadOnlyList<CalendarEvent> events = Fetch("RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=1SU;BYMONTH=11", day: 22);

        events.Should().ContainSingle().Which.Start.Should().Be(new DateTime(2027, 3, 22, 9, 0, 0), "the block's daylight onset on Mar 14 is inside the window");
    }

    [Theory]
    [InlineData("RRULE:FREQ=MINUTELY;INTERVAL=1")]
    [InlineData("RRULE:FREQ=SECONDLY;INTERVAL=1")]
    [InlineData("RRULE:FREQ=MINUTELY;COUNT=100000000")]
    [InlineData("RRULE:FREQ=SECONDLY;BYHOUR=2;BYMINUTE=0;BYSECOND=0")]
    public void A_block_that_changes_its_offset_too_often_fails_the_source_naming_the_tzid(string rrule)
    {
        Stopwatch clock = Stopwatch.StartNew();

        Action fetch = () => Fetch(rrule);

        fetch.Should().Throw<InvalidDataException>().WithMessage("*time zone 'Legacy Eastern'*too often*");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    /// <summary>The reviewer's probe calendar: a legacy TZID defined by its own block, one 09:00 event on March <paramref name="day"/> 2027.</summary>
    private static IReadOnlyList<CalendarEvent> Fetch(string standardRule, int day = 1)
    {
        string ics = string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:x",
            "BEGIN:VTIMEZONE", "TZID:Legacy Eastern",
            "BEGIN:STANDARD", "DTSTART:16010101T020000", "TZOFFSETFROM:-0400", "TZOFFSETTO:-0500", standardRule, "END:STANDARD",
            "BEGIN:DAYLIGHT", "DTSTART:16010101T020000", "TZOFFSETFROM:-0500", "TZOFFSETTO:-0400", "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=2SU;BYMONTH=3", "END:DAYLIGHT",
            "END:VTIMEZONE",
            "BEGIN:VEVENT", "UID:o1", "DTSTAMP:20270101T000000Z",
            $"DTSTART;TZID=\"Legacy Eastern\":202703{day:00}T090000", $"DTEND;TZID=\"Legacy Eastern\":202703{day:00}T100000",
            "SUMMARY:Staff meeting", "END:VEVENT",
            "END:VCALENDAR", "",
        });
        using IcsCalendarSource source = new(Work, _ => ics, new CalendarImportOptions { LocalZone = TestZones.NewYork });
        return source.Fetch(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
    }
}
