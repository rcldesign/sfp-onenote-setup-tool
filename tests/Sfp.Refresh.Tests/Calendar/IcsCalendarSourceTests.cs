using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class IcsCalendarSourceTests
{
    private static readonly DateOnly Mar1 = new(2027, 3, 1);
    private static readonly DateOnly Apr5 = new(2027, 4, 5);

    [Fact]
    public void Weekly_meeting_keeps_its_nine_oclock_local_start_on_both_sides_of_the_March_2027_DST_change()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("weekly-dst.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(Mar1, Apr5);

        events.Select(e => e.Date).Should().Equal(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 15),
            new DateOnly(2027, 3, 22), new DateOnly(2027, 3, 29), new DateOnly(2027, 4, 5));
        events.Should().AllSatisfy(e =>
        {
            e.Start.TimeOfDay.Should().Be(new TimeSpan(9, 0, 0));
            e.End.TimeOfDay.Should().Be(new TimeSpan(10, 0, 0));
            e.Start.Kind.Should().Be(DateTimeKind.Unspecified);
            e.AllDay.Should().BeFalse();
            e.Title.Should().Be("Staff meeting");
            e.SourceLabel.Should().Be("Work");
        });
    }

    [Fact]
    public void Instance_uid_is_the_UID_plus_the_local_occurrence_start()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("weekly-dst.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(Mar1, Mar1);

        events.Should().ContainSingle().Which.Uid.Should().Be("weekly-staff@2027-03-01T09:00");
    }

    [Fact]
    public void Window_filter_keeps_only_instances_on_the_requested_days()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("weekly-dst.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 14));

        events.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2027, 3, 8));
    }

    [Fact]
    public void All_day_holiday_is_flagged_AllDay_with_an_exclusive_end_on_the_next_day()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("all-day-holiday.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 7, 4), new DateOnly(2027, 7, 4));

        CalendarEvent holiday = events.Should().ContainSingle().Subject;
        holiday.AllDay.Should().BeTrue();
        holiday.Title.Should().Be("Independence Day");
        holiday.Start.Should().Be(new DateTime(2027, 7, 4, 0, 0, 0, DateTimeKind.Unspecified));
        holiday.End.Should().Be(new DateTime(2027, 7, 5, 0, 0, 0, DateTimeKind.Unspecified));
        holiday.Uid.Should().Be("independence-day-2027@2027-07-04");
    }

    [Fact]
    public void Multi_day_all_day_event_is_returned_when_the_window_starts_inside_it_and_covers_each_of_its_days()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("all-day-holiday.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 7, 8), new DateOnly(2027, 7, 10));

        CalendarEvent conference = events.Should().ContainSingle().Subject;
        conference.Title.Should().Be("Planner conference");
        conference.CoversDay(new DateOnly(2027, 7, 6)).Should().BeTrue();
        conference.CoversDay(new DateOnly(2027, 7, 8)).Should().BeTrue();
        conference.CoversDay(new DateOnly(2027, 7, 9)).Should().BeFalse("DTEND is exclusive");
    }

    [Fact]
    public void Private_event_is_masked_as_Private_by_default()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("private-event.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 2));

        events.Should().HaveCount(2);
        CalendarEvent dentist = events.Single(e => e.IsPrivate);
        dentist.Title.Should().Be("Private");
        events.Single(e => !e.IsPrivate).Title.Should().Be("Lunch with Sam");
    }

    [Fact]
    public void Private_event_keeps_its_title_when_masking_is_off_but_stays_flagged()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("private-event.ics"), Fixtures.NewYorkOptions(maskPrivate: false));

        CalendarEvent dentist = source.Fetch(new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 2)).Single(e => e.IsPrivate);

        dentist.Title.Should().Be("Dentist");
    }

    [Fact]
    public void Utc_times_are_converted_to_the_injected_local_zone()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("private-event.ics"), Fixtures.NewYorkOptions());

        CalendarEvent dentist = source.Fetch(new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 2)).Single(e => e.IsPrivate);

        dentist.Start.Should().Be(new DateTime(2027, 3, 2, 9, 0, 0), "14:00Z is 09:00 in New York before the DST change");
        dentist.End.Should().Be(new DateTime(2027, 3, 2, 10, 0, 0));
    }

    [Fact]
    public void Same_file_read_in_another_zone_moves_the_utc_event_but_not_the_zoned_one()
    {
        CalendarImportOptions berlin = new() { LocalZone = TestZones.Berlin };
        using IcsCalendarSource source = new(Fixtures.FileSource("private-event.ics"), berlin);

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 2));

        events.Single(e => e.IsPrivate).Start.Should().Be(new DateTime(2027, 3, 2, 15, 0, 0), "14:00Z is 15:00 in Berlin");
        events.Single(e => !e.IsPrivate).Start.Should().Be(new DateTime(2027, 3, 2, 18, 0, 0), "12:00 New York is 18:00 in Berlin");
    }

    [Fact]
    public void Cancelled_override_comes_back_Cancelled_and_an_exdate_instance_does_not_appear()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("cancelled-instance.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(Mar1, Apr5);

        events.Select(e => e.Date).Should().Equal(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 15), new DateOnly(2027, 3, 29), new DateOnly(2027, 4, 5));
        CalendarEvent cancelled = events.Single(e => e.Date == new DateOnly(2027, 3, 15));
        cancelled.Cancelled.Should().BeTrue();
        cancelled.Uid.Should().Be("weekly-staff@2027-03-15T09:00", "the override shares the Uid of the instance it replaces");
        events.Where(e => e.Date != new DateOnly(2027, 3, 15)).Should().AllSatisfy(e => e.Cancelled.Should().BeFalse());
    }

    [Fact]
    public void Url_source_reads_its_content_through_the_injected_fetcher()
    {
        string? requested = null;
        CalendarSource url = new("Work", CalendarSourceKind.IcsUrl, "https://example.test/feeds/work.ics", null, null);
        using IcsCalendarSource source = new(url, location =>
        {
            requested = location;
            return File.ReadAllText(Fixtures.Ics("weekly-dst.ics"));
        }, Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(Mar1, Mar1);

        requested.Should().Be("https://example.test/feeds/work.ics");
        events.Should().ContainSingle();
    }

    [Fact]
    public void Calendar_name_filter_selects_one_calendar_of_a_multi_calendar_file()
    {
        using IcsCalendarSource home = new(Fixtures.FileSource("multi-calendar.ics", "Home", calendarName: "home"), Fixtures.NewYorkOptions());
        using IcsCalendarSource all = new(Fixtures.FileSource("multi-calendar.ics"), Fixtures.NewYorkOptions());

        home.Fetch(new DateOnly(2027, 3, 3), new DateOnly(2027, 3, 3)).Should().ContainSingle().Which.Title.Should().Be("Piano lesson");
        all.Fetch(new DateOnly(2027, 3, 3), new DateOnly(2027, 3, 3)).Should().HaveCount(2);
    }

    [Fact]
    public void Events_come_back_ordered_by_start_then_title()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("multi-calendar.ics"), Fixtures.NewYorkOptions());

        IReadOnlyList<CalendarEvent> events = source.Fetch(new DateOnly(2027, 3, 3), new DateOnly(2027, 3, 3));

        events.Select(e => e.Title).Should().Equal("Design review", "Piano lesson");
    }

    [Fact]
    public void Webcal_urls_are_fetched_over_https()
    {
        IcsCalendarSource.IsUrl("webcal://example.test/a.ics").Should().BeTrue();
        IcsCalendarSource.NormalizeUrl("webcal://example.test/a.ics").Should().Be(new Uri("https://example.test/a.ics"));
        IcsCalendarSource.IsUrl(@"C:\Users\me\family.ics").Should().BeFalse();
        IcsCalendarSource.IsUrl("/home/me/family.ics").Should().BeFalse();
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_is_rejected()
    {
        using IcsCalendarSource source = new(Fixtures.FileSource("weekly-dst.ics"), Fixtures.NewYorkOptions());

        Action act = () => source.Fetch(Apr5, Mar1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_missing_file_fails_the_fetch_with_the_io_error()
    {
        using IcsCalendarSource source = new(new CalendarSource("Gone", CalendarSourceKind.IcsFile, Fixtures.Ics("does-not-exist.ics"), null, null), Fixtures.NewYorkOptions());

        Action act = () => source.Fetch(Mar1, Mar1);

        act.Should().Throw<FileNotFoundException>();
    }
}
