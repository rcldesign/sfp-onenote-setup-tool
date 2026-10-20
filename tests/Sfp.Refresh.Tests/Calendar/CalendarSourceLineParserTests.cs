using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class CalendarSourceLineParserTests
{
    [Fact]
    public void Labelled_ics_url_parses_to_an_IcsUrl_source()
    {
        CalendarSource s = CalendarSourceLineParser.Parse("Work | https://outlook.office.com/owa/calendar/abc/calendar.ics");

        s.Should().Be(new CalendarSource("Work", CalendarSourceKind.IcsUrl, "https://outlook.office.com/owa/calendar/abc/calendar.ics", null, null));
    }

    [Fact]
    public void Bare_url_takes_the_host_as_its_label()
    {
        CalendarSource s = CalendarSourceLineParser.Parse("webcal://calendar.google.com/calendar/ical/x/basic.ics");

        s.Label.Should().Be("calendar.google.com");
        s.Kind.Should().Be(CalendarSourceKind.IcsUrl);
        s.Location.Should().Be("webcal://calendar.google.com/calendar/ical/x/basic.ics");
    }

    [Theory]
    [InlineData(@"C:\Users\me\family.ics", "family")]
    [InlineData("/home/me/calendars/Family.ICS", "Family")]
    public void Bare_file_path_takes_the_file_name_as_its_label(string path, string label)
    {
        CalendarSource s = CalendarSourceLineParser.Parse(path);

        s.Kind.Should().Be(CalendarSourceKind.IcsFile);
        s.Label.Should().Be(label);
        s.Location.Should().Be(path);
    }

    [Theory]
    [InlineData("outlook", "Outlook", null)]
    [InlineData("Outlook", "Outlook", null)]
    [InlineData("Shared | outlook:Team Calendar", "Shared", "Team Calendar")]
    public void Outlook_keyword_parses_to_an_Outlook_source(string line, string label, string? calendarName)
    {
        CalendarSource s = CalendarSourceLineParser.Parse(line);

        s.Kind.Should().Be(CalendarSourceKind.Outlook);
        s.Label.Should().Be(label);
        s.Location.Should().Be("outlook");
        s.CalendarName.Should().Be(calendarName);
    }

    [Theory]
    [InlineData("holidays", "Holidays", "us")]
    [InlineData("US holidays | holidays:us", "US holidays", "us")]
    [InlineData("HOLIDAYS:uk", "Holidays", "uk")]
    public void Holidays_keyword_parses_to_a_Holidays_source_with_a_list_id(string line, string label, string listId)
    {
        CalendarSource s = CalendarSourceLineParser.Parse(line);

        s.Kind.Should().Be(CalendarSourceKind.Holidays);
        s.Label.Should().Be(label);
        s.Location.Should().Be(listId);
    }

    [Fact]
    public void Third_and_fourth_fields_are_the_color_tag_and_the_calendar_name_filter()
    {
        CalendarSource s = CalendarSourceLineParser.Parse("Work | https://x.test/c.ics | blue | Meetings");

        s.ColorTag.Should().Be("blue");
        s.CalendarName.Should().Be("Meetings");
    }

    [Fact]
    public void Empty_label_before_the_separator_falls_back_to_the_default()
    {
        CalendarSourceLineParser.Parse(" | https://x.test/c.ics").Label.Should().Be("x.test");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Work |")]
    [InlineData("just some words")]
    [InlineData("ftp://x.test/c.ics")]
    [InlineData(@"C:\notes.txt")]
    public void Unrecognized_lines_are_rejected(string line)
    {
        Action act = () => CalendarSourceLineParser.Parse(line);

        act.Should().Throw<FormatException>();
        CalendarSourceLineParser.TryParse(line, out CalendarSource? parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void ParseAll_skips_blank_slots_reports_bad_lines_and_caps_at_four_sources()
    {
        List<string> problems = new();
        string?[] slots =
        {
            "Work | https://a.test/a.ics", null, "nonsense", "holidays", "outlook", "  ", "/x/b.ics", "/x/c.ics",
        };

        IReadOnlyList<CalendarSource> sources = CalendarSourceLineParser.ParseAll(slots, problems);

        sources.Select(s => s.Kind).Should().Equal(CalendarSourceKind.IcsUrl, CalendarSourceKind.Holidays, CalendarSourceKind.Outlook, CalendarSourceKind.IcsFile);
        sources.Should().HaveCount(CalendarSourceLineParser.MaxSources);
        problems.Should().ContainSingle().Which.Should().Contain("nonsense");
    }
}
