using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>refresh-13: a response or file without BEGIN:VCALENDAR is a failed source, never an empty calendar.</summary>
public class IcsCalendarContentTests
{
    private static readonly DateOnly From = new(2027, 1, 4);
    private static readonly DateOnly To = new(2027, 1, 18);
    private static readonly CalendarSource Work = new("Work", CalendarSourceKind.IcsUrl, "https://example.com/cal.ics", null, null);

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    [InlineData("The calendar you requested is not available.")]
    public void Text_without_a_calendar_is_a_failure(string body)
    {
        using IcsCalendarSource source = new(Work, _ => body, Fixtures.NewYorkOptions());

        Action fetch = () => source.Fetch(From, To);

        fetch.Should().Throw<InvalidDataException>().WithMessage("*no BEGIN:VCALENDAR line*");
    }

    [Fact]
    public void A_calendar_with_no_events_is_still_an_empty_calendar()
    {
        using IcsCalendarSource source = new(Work, _ => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n", Fixtures.NewYorkOptions());

        source.Fetch(From, To).Should().BeEmpty();
    }

    [Fact]
    public void An_empty_response_is_reported_like_a_timeout()
    {
        CalendarSourceFactory factory = new(TestPaths.TemplatesDir, Fixtures.NewYorkOptions(), contentFetcher: _ => "");

        CalendarFetchResult result = CalendarImportRunner.Fetch(new[] { Work }, factory, From, To, new CapturingLog().Logger);

        result.Events.Should().BeEmpty();
        result.AllSourcesFailed.Should().BeTrue("the only source failed, so the run keeps the events on the pages and counts toward the toast");
        result.FailureSummary.Should().Be("Work: The source returned no calendar data: the text is empty or has no BEGIN:VCALENDAR line.");
    }
}
