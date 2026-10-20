using Sfp.Refresh.Calendar;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

public class CalendarSourceFactoryTests
{
    private static CalendarSourceFactory Factory(Func<string, string>? fetcher = null) =>
        new(TestPaths.TemplatesDir, Fixtures.NewYorkOptions(), contentFetcher: fetcher);

    [Theory]
    [InlineData(CalendarSourceKind.IcsUrl, "https://x.test/c.ics", typeof(IcsCalendarSource))]
    [InlineData(CalendarSourceKind.IcsFile, "/x/c.ics", typeof(IcsCalendarSource))]
    [InlineData(CalendarSourceKind.Holidays, "us", typeof(HolidayCalendarSource))]
    [InlineData(CalendarSourceKind.Outlook, "outlook", typeof(OutlookCalendarSource))]
    public void Each_kind_maps_to_its_reader(CalendarSourceKind kind, string location, Type readerType)
    {
        ICalendarSource reader = Factory().Create(new CalendarSource("L", kind, location, null, null));

        reader.Should().BeOfType(readerType);
        (reader as IDisposable)?.Dispose();
    }

    [Fact]
    public void Holidays_source_is_backed_by_the_bundled_list_and_fetches_real_dates()
    {
        ICalendarSource reader = Factory().Create(new CalendarSource("Holidays", CalendarSourceKind.Holidays, "us", null, null));

        reader.Fetch(new DateOnly(2027, 11, 25), new DateOnly(2027, 11, 25)).Should().ContainSingle().Which.Title.Should().Be("Thanksgiving Day");
    }

    [Theory]
    [InlineData("", "holidays-us.json")]
    [InlineData("us", "holidays-us.json")]
    [InlineData("UK", "holidays-uk.json")]
    [InlineData("custom.json", "custom.json")]
    public void Holiday_file_path_is_resolved_under_the_templates_folder(string location, string expectedFileName)
    {
        string path = Factory().HolidayFilePath(new CalendarSource("H", CalendarSourceKind.Holidays, location, null, null));

        path.Should().Be(Path.Combine(TestPaths.TemplatesDir, expectedFileName));
    }

    [Fact]
    public void Unknown_holiday_list_fails_at_creation_with_the_file_named()
    {
        Action act = () => Factory().Create(new CalendarSource("H", CalendarSourceKind.Holidays, "nowhere", null, null));

        act.Should().Throw<FileNotFoundException>().WithMessage("*holidays-nowhere.json*");
    }

    [Fact]
    public void Content_fetcher_is_handed_to_ics_sources()
    {
        string? seen = null;
        ICalendarSource reader = Factory(location =>
        {
            seen = location;
            return File.ReadAllText(Fixtures.Ics("weekly-dst.ics"));
        }).Create(new CalendarSource("Work", CalendarSourceKind.IcsUrl, "https://x.test/c.ics", null, null));

        reader.Fetch(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 1)).Should().ContainSingle();
        seen.Should().Be("https://x.test/c.ics");
        (reader as IDisposable)?.Dispose();
    }
}
