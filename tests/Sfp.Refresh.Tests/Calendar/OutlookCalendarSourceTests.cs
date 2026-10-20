using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class OutlookCalendarSourceTests
{
    private static readonly CalendarSource Source = new("Outlook", CalendarSourceKind.Outlook, "outlook", null, null);

    [SkippableFact]
    [Trait("Category", "COM")]
    public void Outlook_reader_lists_the_default_calendar_for_a_week()
    {
        Skip.IfNot(OutlookAvailability.IsInstalled, "Outlook desktop is not installed on this machine; skipping the Outlook COM read test.");
        OutlookCalendarSource source = new(Source, Fixtures.NewYorkOptions());
        DateOnly from = new(2027, 1, 4);

        IReadOnlyList<CalendarEvent> events = source.Fetch(from, from.AddDays(6));

        events.Should().NotBeNull();
        events.Should().AllSatisfy(e =>
        {
            e.SourceLabel.Should().Be("Outlook");
            e.Date.Should().BeOnOrAfter(from.AddDays(-1)).And.BeOnOrBefore(from.AddDays(6));
        });
    }

    [SkippableFact]
    public void Without_Outlook_the_reader_fails_with_a_clear_message_instead_of_a_COM_error()
    {
        Skip.If(OutlookAvailability.IsInstalled, "Outlook is installed here, so the unavailable path cannot be exercised.");
        OutlookCalendarSource source = new(Source);

        Action act = () => source.Fetch(new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 10));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Outlook desktop is not installed*");
    }

    [Fact]
    public void Availability_check_never_throws_and_is_false_off_Windows()
    {
        bool installed = OutlookAvailability.IsInstalled;

        if (!OperatingSystem.IsWindows())
        {
            installed.Should().BeFalse();
        }
    }

    [Fact]
    public void Restrict_filter_covers_the_whole_window_in_the_US_date_pattern_Outlook_expects()
    {
        string filter = OutlookCalendarSource.RestrictFilter(new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 10));

        filter.Should().Be("[Start] < '01/11/2027 12:00 AM' AND [End] >= '01/04/2027 12:00 AM'");
    }
}
