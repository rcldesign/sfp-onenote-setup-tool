using Serilog;
using Serilog.Core;
using Sfp.Refresh.Calendar;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

public sealed class CalendarImportRunnerTests : IDisposable
{
    private static readonly DateOnly From = new(2027, 1, 4);
    private static readonly DateOnly To = new(2027, 1, 18);
    private static readonly DateTimeOffset Now = new(2027, 1, 4, 5, 0, 0, TimeSpan.FromHours(-5));
    private static readonly ILogger Log = Logger.None;

    private readonly string dir = TestPaths.NewTempDir("import-runner");

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static CalendarEvent Event(string title, int day, int hour) =>
        new(title + "@" + day, title, new DateTime(2027, 1, day, hour, 0, 0), new DateTime(2027, 1, day, hour + 1, 0, 0), false, false, false, "Work", null);

    private FailureCounter Counter() => FailureCounter.Load(Path.Combine(dir, FailureCounter.FileName));

    [Fact]
    public void One_failing_source_among_two_is_skipped_and_the_other_sources_events_are_returned()
    {
        StubCalendarSource broken = new(new HttpRequestException("503 Service Unavailable"));
        StubCalendarSource working = new(Event("Standup", 5, 9), Event("Review", 4, 14));
        CalendarSourceBinding[] sources = { new("Work", broken), new("Home", working) };

        CalendarFetchResult result = CalendarImportRunner.Fetch(sources, From, To, Log);

        result.Events.Select(e => e.Title).Should().Equal("Review", "Standup");
        result.Failures.Should().ContainSingle().Which.Should().Be(new SourceFailure("Work", "503 Service Unavailable"));
        result.AnyFailed.Should().BeTrue();
        result.AllSourcesFailed.Should().BeFalse();
        result.SourceCount.Should().Be(2);
        working.FetchCalls.Should().Be(1, "the failure of the first source does not stop the second");
    }

    [Fact]
    public void All_sources_failing_sets_AllSourcesFailed_which_maps_to_exit_code_6()
    {
        CalendarSourceBinding[] sources =
        {
            new("Work", new StubCalendarSource(new HttpRequestException("timeout"))),
            new("Holidays", new StubCalendarSource(new FileNotFoundException("holidays-xx.json"))),
        };

        CalendarFetchResult result = CalendarImportRunner.Fetch(sources, From, To, Log);

        result.AllSourcesFailed.Should().BeTrue();
        result.Events.Should().BeEmpty();
        result.FailureSummary.Should().Be("Work: timeout; Holidays: holidays-xx.json");
    }

    [Fact]
    public void No_sources_is_neither_a_failure_nor_a_success_to_count()
    {
        CalendarFetchResult result = CalendarImportRunner.Fetch(Array.Empty<CalendarSourceBinding>(), From, To, Log);

        result.AllSourcesFailed.Should().BeFalse();
        result.AnyFailed.Should().BeFalse();
    }

    [Fact]
    public void Three_consecutive_total_failures_raise_the_toast_and_the_fourth_raises_it_again()
    {
        FakeSourceFailureNotifier notifier = new();
        FailureCounter counter = Counter();
        CalendarSourceBinding[] sources = { new("Work", new StubCalendarSource(new HttpRequestException("timeout"))) };
        bool[] toasted = new bool[4];

        for (int run = 0; run < 4; run++)
        {
            CalendarFetchResult result = CalendarImportRunner.Fetch(sources, From, To, Log);
            toasted[run] = CalendarImportRunner.Track(result, counter, notifier, Now.AddDays(run), Log);
        }

        toasted.Should().Equal(false, false, true, true);
        counter.ShouldToast.Should().BeTrue();
        counter.ConsecutiveTotalFailures.Should().Be(4);
        notifier.Notifications.Should().HaveCount(2);
        notifier.Notifications[0].Title.Should().Be(CalendarImportRunner.ToastTitle);
        notifier.Notifications[0].Message.Should().Be("All 1 calendar source(s) failed 3 runs in a row. Last error: Work: timeout");
        FailureCounter.Load(counter.Path).ConsecutiveTotalFailures.Should().Be(4, "every run saves the counter");
    }

    [Fact]
    public void A_success_resets_the_counter_and_no_toast_is_raised()
    {
        FakeSourceFailureNotifier notifier = new();
        FailureCounter counter = Counter();
        counter.RecordTotalFailure(Now.AddDays(-2));
        counter.RecordTotalFailure(Now.AddDays(-1));
        CalendarSourceBinding[] sources = { new("Work", new StubCalendarSource(Event("Standup", 5, 9))) };

        bool toasted = CalendarImportRunner.Track(CalendarImportRunner.Fetch(sources, From, To, Log), counter, notifier, Now, Log);

        toasted.Should().BeFalse();
        counter.ConsecutiveTotalFailures.Should().Be(0);
        counter.LastSuccess.Should().Be(Now);
        notifier.Notifications.Should().BeEmpty();
        FailureCounter.Load(counter.Path).ConsecutiveTotalFailures.Should().Be(0);
    }

    [Fact]
    public void A_partial_failure_counts_as_a_success_for_the_streak()
    {
        FailureCounter counter = Counter();
        counter.RecordTotalFailure(Now.AddDays(-1));
        CalendarSourceBinding[] sources =
        {
            new("Work", new StubCalendarSource(new HttpRequestException("timeout"))),
            new("Home", new StubCalendarSource(Event("Dinner", 6, 19))),
        };

        CalendarImportRunner.Track(CalendarImportRunner.Fetch(sources, From, To, Log), counter, new FakeSourceFailureNotifier(), Now, Log);

        counter.ConsecutiveTotalFailures.Should().Be(0);
    }

    [Fact]
    public void Sources_the_factory_cannot_open_are_reported_as_failures_alongside_fetch_failures()
    {
        CalendarSource[] setup =
        {
            new("Holidays", CalendarSourceKind.Holidays, "nowhere", null, null),
            new("Work", CalendarSourceKind.IcsFile, Fixtures.Ics("weekly-dst.ics"), null, null),
            new("Gone", CalendarSourceKind.IcsFile, Fixtures.Ics("missing.ics"), null, null),
        };
        CalendarSourceFactory factory = new(TestPaths.TemplatesDir, Fixtures.NewYorkOptions());

        CalendarFetchResult result = CalendarImportRunner.Fetch(setup, factory, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 8), Log);

        result.SourceCount.Should().Be(3);
        result.Failures.Select(f => f.Label).Should().Equal("Holidays", "Gone");
        result.Events.Should().HaveCount(2).And.AllSatisfy(e => e.SourceLabel.Should().Be("Work"));
        result.AllSourcesFailed.Should().BeFalse();
    }
}
