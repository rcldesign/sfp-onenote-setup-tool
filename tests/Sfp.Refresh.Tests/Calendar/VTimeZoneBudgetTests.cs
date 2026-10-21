using System.Diagnostics;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>
/// Round 4 refresh-4: a file's VTIMEZONE block is built into a zone only when an event names its TZID, and all the blocks
/// one parse expands share one budget of offset changes (50,000 per file), so a feed of many blocks
/// costs no more than a feed of one; the block that crosses the budget fails the source, naming its TZID.
/// </summary>
public class VTimeZoneBudgetTests
{
    private const string Yearly = "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=1SU;BYMONTH=11";

    /// <summary>An onset every minute: about 1.1 million over the expansion window, so expanding the block alone fails the source.</summary>
    private const string EveryMinute = "RRULE:FREQ=MINUTELY;INTERVAL=1";

    /// <summary>An onset every 35 minutes: about 31,000 over the expansion window, under the budget alone, over it twice.</summary>
    private const string Every35Minutes = "RRULE:FREQ=MINUTELY;INTERVAL=35";

    private static readonly CalendarSource Work = new("Work", CalendarSourceKind.IcsUrl, "https://example.com/cal.ics", null, null);

    [Fact]
    public void A_hundred_blocks_whose_tzids_no_event_names_are_never_expanded()
    {
        string[] unused = Enumerable.Range(1, 100).Select(k => Block("Unused " + k, EveryMinute)).ToArray();
        string ics = Calendar(unused.Append(Block("Legacy Eastern", Yearly)), Event("Legacy Eastern", "e1", 22));
        Fetch(Calendar(new[] { Block("Legacy Eastern", Yearly) }, Event("Legacy Eastern", "e1", 22))).Should().ContainSingle("warm-up");
        Stopwatch clock = Stopwatch.StartNew();

        IReadOnlyList<CalendarEvent> events = Fetch(ics);

        clock.Stop();
        events.Should().ContainSingle().Which.Start.Should().Be(new DateTime(2027, 3, 22, 9, 0, 0), "only the block the event names is read");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "no unused block is expanded");
    }

    [Fact]
    public void Blocks_whose_onsets_together_exceed_the_budget_fail_the_source_naming_the_tzid_that_crossed_it()
    {
        string ics = Calendar(new[] { Block("Legacy A", Every35Minutes), Block("Legacy B", Every35Minutes) }, Event("Legacy A", "a", 1), Event("Legacy B", "b", 2));

        Action fetch = () => Fetch(ics);

        fetch.Should().Throw<InvalidDataException>().WithMessage("*time zone 'Legacy B'*too often*more than 50000 offset changes*")
            .Which.Message.Should().NotContain("'Legacy A'");
    }

    [Fact]
    public void A_block_under_the_budget_alone_still_converts_its_events_when_another_unused_one_would_cross_it()
    {
        string ics = Calendar(new[] { Block("Legacy A", Every35Minutes), Block("Legacy B", Every35Minutes) }, Event("Legacy A", "a", 1));

        IReadOnlyList<CalendarEvent> events = Fetch(ics);

        events.Should().ContainSingle().Which.Start.Should().Be(new DateTime(2027, 3, 1, 9, 0, 0));
    }

    /// <summary>A legacy TZID defined by its own block: a STANDARD observance with the given rule and the US daylight rule, both from 1601.</summary>
    private static string Block(string tzid, string standardRule) => string.Join("\r\n", new[]
    {
        "BEGIN:VTIMEZONE", "TZID:" + tzid,
        "BEGIN:STANDARD", "DTSTART:16010101T020000", "TZOFFSETFROM:-0400", "TZOFFSETTO:-0500", standardRule, "END:STANDARD",
        "BEGIN:DAYLIGHT", "DTSTART:16010101T020000", "TZOFFSETFROM:-0500", "TZOFFSETTO:-0400", "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=2SU;BYMONTH=3", "END:DAYLIGHT",
        "END:VTIMEZONE",
    });

    /// <summary>A 09:00 to 10:00 event on March <paramref name="day"/> 2027 in the given TZID.</summary>
    private static string Event(string tzid, string uid, int day) => string.Join("\r\n", new[]
    {
        "BEGIN:VEVENT", "UID:" + uid, "DTSTAMP:20270101T000000Z",
        $"DTSTART;TZID=\"{tzid}\":202703{day:00}T090000", $"DTEND;TZID=\"{tzid}\":202703{day:00}T100000",
        "SUMMARY:Staff meeting", "END:VEVENT",
    });

    private static string Calendar(IEnumerable<string> blocks, params string[] events) =>
        string.Join("\r\n", new[] { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:x" }.Concat(blocks).Concat(events).Append("END:VCALENDAR").Append(""));

    private static IReadOnlyList<CalendarEvent> Fetch(string ics)
    {
        using IcsCalendarSource source = new(Work, _ => ics, new CalendarImportOptions { LocalZone = TestZones.NewYork });
        return source.Fetch(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
    }
}
