using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>refresh-15: calendar files and holiday lists are read only from full local paths, never from a network or device path.</summary>
public class LocalPathTests
{
    private static readonly DateOnly From = new(2027, 1, 4);
    private static readonly DateOnly To = new(2027, 1, 18);

    [Theory]
    [InlineData(@"\\evil.example.com\cal\work.ics")]
    [InlineData(@"\\evil.example.com@80\DavWWWRoot\work.ics")]
    [InlineData("//evil.example.com/cal/work.ics")]
    [InlineData(@"\\?\C:\cal\work.ics")]
    [InlineData(@"\\.\pipe\cal")]
    public void Network_and_device_paths_are_refused(string path) =>
        LocalPath.Problem(path).Should().Contain("is a network or device path");

    [Theory]
    [InlineData("work.ics")]
    [InlineData(@"cal\work.ics")]
    [InlineData("C:work.ics")]
    [InlineData(@"\cal\work.ics")]
    public void Paths_without_their_drive_are_refused(string path) =>
        LocalPath.Problem(path).Should().Contain("is not a full local path");

    [Theory]
    [InlineData(@"C:\Users\me\work.ics")]
    [InlineData("D:/cal/work.ics")]
    public void Drive_paths_are_read(string path) =>
        LocalPath.Problem(path).Should().BeNull();

    [Fact]
    public void A_calendar_file_on_a_network_share_fails_its_source_before_any_read()
    {
        CalendarSource work = new("Work", CalendarSourceKind.IcsFile, @"\\evil.example.com\cal\work.ics", null, null);

        CalendarFetchResult result = CalendarImportRunner.Fetch(new[] { work }, new CalendarSourceFactory(TestPaths.TemplatesDir, Fixtures.NewYorkOptions()), From, To, new CapturingLog().Logger);

        result.FailureSummary.Should().Be(@"Work: '\\evil.example.com\cal\work.ics' is a network or device path; calendar files and holiday lists are read only from a local drive, such as C:\Users\me\calendar.ics.");
    }

    [Fact]
    public void A_holiday_list_on_a_network_share_fails_its_source()
    {
        CalendarSource holidays = CalendarSourceLineParser.Parse(@"Hol | holidays:\\evil.example.com\h\list.json");
        CalendarSourceFactory factory = new(TestPaths.TemplatesDir, Fixtures.NewYorkOptions());

        CalendarFetchResult result = CalendarImportRunner.Fetch(new[] { holidays }, factory, From, To, new CapturingLog().Logger);

        result.FailureSummary.Should().StartWith(@"Hol: '\\evil.example.com\h\list.json' is a network or device path");
        factory.HolidayFilePath(new CalendarSource("Hol", CalendarSourceKind.Holidays, "custom.json", null, null))
            .Should().Be(Path.Combine(TestPaths.TemplatesDir, "custom.json"), "a relative list is still taken under the templates folder");
    }
}
