using Sfp.Refresh.Calendar;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

public class HolidayCalendarSourceTests
{
    private static readonly CalendarSource Source = new("Holidays", CalendarSourceKind.Holidays, "us", null, null);

    private static HolidayCalendarSource UsFederal() =>
        new(Source, HolidayList.Load(Path.Combine(TestPaths.TemplatesDir, "holidays-us.json")));

    [Fact]
    public void New_Years_Day_2028_is_observed_on_Friday_December_31_2027()
    {
        IReadOnlyList<CalendarEvent> events = UsFederal().Fetch(new DateOnly(2027, 12, 26), new DateOnly(2028, 1, 2));

        CalendarEvent newYear = events.Should().ContainSingle(e => e.Title == "New Year's Day").Subject;
        newYear.Date.Should().Be(new DateOnly(2027, 12, 31));
        newYear.Date.DayOfWeek.Should().Be(DayOfWeek.Friday);
        newYear.Uid.Should().Be("holiday:us-federal:new-year-s-day:2028");
    }

    [Fact]
    public void Christmas_2027_falls_on_a_Saturday_and_is_observed_on_the_Friday_before()
    {
        IReadOnlyList<CalendarEvent> events = UsFederal().Fetch(new DateOnly(2027, 12, 20), new DateOnly(2027, 12, 26));

        events.Should().ContainSingle(e => e.Title == "Christmas Day").Which.Date.Should().Be(new DateOnly(2027, 12, 24));
    }

    [Fact]
    public void Thanksgiving_2027_is_November_25()
    {
        IReadOnlyList<CalendarEvent> events = UsFederal().Fetch(new DateOnly(2027, 11, 1), new DateOnly(2027, 11, 30));

        events.Should().HaveCount(2);
        events.Single(e => e.Title == "Thanksgiving Day").Date.Should().Be(new DateOnly(2027, 11, 25));
        events.Single(e => e.Title == "Veterans Day").Date.Should().Be(new DateOnly(2027, 11, 11));
    }

    [Fact]
    public void Memorial_Day_2027_is_the_last_Monday_of_May()
    {
        IReadOnlyList<CalendarEvent> events = UsFederal().Fetch(new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 31));

        events.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2027, 5, 31));
    }

    [Fact]
    public void Holidays_are_all_day_events_carrying_the_source_label()
    {
        IReadOnlyList<CalendarEvent> events = UsFederal().Fetch(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));

        events.Select(e => (e.Title, e.Date)).Should().Equal(
            ("New Year's Day", new DateOnly(2027, 1, 1)),
            ("Birthday of Martin Luther King, Jr.", new DateOnly(2027, 1, 18)),
            ("Washington's Birthday", new DateOnly(2027, 2, 15)));
        events.Should().AllSatisfy(e =>
        {
            e.AllDay.Should().BeTrue();
            e.End.Should().Be(e.Start.AddDays(1));
            e.Start.TimeOfDay.Should().Be(TimeSpan.Zero);
            e.SourceLabel.Should().Be("Holidays");
            e.Cancelled.Should().BeFalse();
            e.IsPrivate.Should().BeFalse();
        });
    }

    [Fact]
    public void Sunday_holiday_is_observed_on_Monday()
    {
        HolidayRule rule = new("Independence Day", 7, 4, null, null, true);

        rule.NominalDateIn(2027).DayOfWeek.Should().Be(DayOfWeek.Sunday);
        rule.ObservedDateIn(2027).Should().Be(new DateOnly(2027, 7, 5));
    }

    [Fact]
    public void A_floating_holiday_is_never_shifted()
    {
        HolidayRule rule = new("Labor Day", 9, null, "Monday", 1, false);

        rule.ObservedDateIn(2027).Should().Be(new DateOnly(2027, 9, 6));
    }

    [Fact]
    public void Holiday_list_parsing_rejects_a_rule_without_a_day_or_weekday()
    {
        string json = """{ "id": "x", "name": "X", "holidays": [ { "name": "Broken", "month": 3 } ] }""";

        Action act = () => HolidayList.Parse(json, "test");

        act.Should().Throw<FormatException>().WithMessage("*Broken*weekday*");
    }

    [Fact]
    public void Slug_lower_cases_and_hyphenates()
    {
        HolidayCalendarSource.Slug("Birthday of Martin Luther King, Jr.").Should().Be("birthday-of-martin-luther-king-jr");
    }
}
