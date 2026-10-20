using Serilog.Events;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// M4.6 and M4.7: the orchestrator over the fake app and a Q1 2027 plan: a dry run lists the changes and writes nothing;
/// a real run writes and a second run is empty; a blocked slot is counted as skipped; inked values wait for confirmation;
/// exit codes 2, 3, 5 and 6; the calendar path writes, removes and survives a feed outage.
/// </summary>
public class RefreshOrchestratorTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly SetupPageSpec S = M.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private static readonly DateOnly Jan4 = new(2027, 1, 4);
    private static readonly DateTimeOffset Now = new(2027, 1, 4, 5, 0, 0, TimeSpan.Zero);

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static RefreshRequest Request(bool dryRun = false, bool calendar = false, bool writeInked = false, string? folder = null, string name = "SFP Q1 2027", DateOnly? today = null) =>
        new(name, folder, calendar, 14, dryRun, writeInked, today ?? Jan4);

    private sealed class Harness
    {
        public Harness(bool saveSidecar = true, ICalendarSourceFactory? calendars = null, bool holidays = false, TimeBlockWindow? window = null)
        {
            Folder = TestPaths.NewTempDir("refresh");
            Notebook = FakeNotebook.Generate(folder: Folder, window: window);
            Store = new SidecarStore();
            SidecarPath = Store.PathFor(Notebook.NotebookPath);
            if (saveSidecar)
            {
                Store.Save(SidecarPath, Notebook.Sidecar);
            }

            Log = new CapturingLog();
            Services = new RefreshServices
            {
                App = Notebook.App,
                Sidecars = Store,
                Manifest = M,
                Dates = new DateEngine(),
                Plans = new PlanBuilder(new DateEngine()),
                SetupReader = new SetupReader(),
                Propagators = (catalog, reader) => new Propagator(catalog, reader, Mono),
                Writer = new AnchorWriter(Notebook.App, Notebook.Options, Log.Logger),
                Measure = Mono,
                Log = Log.Logger,
                CalendarSources = calendars,
                Notifier = Notifier,
                SearchFolders = new[] { Folder },
                Clock = () => Now,
                ToolVersion = "9.9.9",
                LogPath = Path.Combine(Folder, "run.log"),
                IncludeBundledHolidays = holidays,
                OpenTimeout = TimeSpan.FromSeconds(4),
                Sleep = _ => { },
            };
        }

        public string Folder { get; }

        public FakeNotebook Notebook { get; }

        public SidecarStore Store { get; }

        public string SidecarPath { get; }

        public CapturingLog Log { get; }

        public FakeSourceFailureNotifier Notifier { get; } = new();

        public RefreshServices Services { get; }

        public FakeOneNoteApp App => Notebook.App;

        public RefreshResult Run(RefreshRequest request, List<string>? progress = null) =>
            new RefreshOrchestrator(Services).Run(request, progress is null ? null : new ListProgress(progress), CancellationToken.None);

        public Sidecar SavedSidecar() => Store.Load(SidecarPath)!;

        public void Type(RectPt rect, string text) => Notebook.TypeUserText(PageKey.Setup, rect, text);

        private sealed class ListProgress : IProgress<string>
        {
            private readonly List<string> lines;

            public ListProgress(List<string> lines)
            {
                this.lines = lines;
            }

            public void Report(string value) => lines.Add(value);
        }
    }

    [Fact]
    public void Dry_run_lists_every_change_with_titles_and_writes_nothing()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Type(S.Objectives[1], "Hire two PMs");
        h.Type(S.StandingGoals[0], "Walk 30 min");
        int updates = h.App.CallCount("UpdatePageContent");
        List<string> progress = new();

        RefreshResult result = h.Run(Request(dryRun: true), progress);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.LogPath.Should().Be(h.Services.LogPath);
        RefreshSummary summary = result.Summary!;
        summary.Values.Objectives[0].Should().Be("Ship the connector");
        summary.Values.StandingGoals[0].Should().Be("Walk 30 min");
        summary.Changes.Should().HaveCount(104 + 2 + 2 + 2 + 90);
        summary.Changes.Should().AllSatisfy(c => c.Kind.Should().Be(ChangeKind.Create));
        summary.PageTitles[MonJan4.Value].Should().Be("Mon Jan 4");
        summary.Skipped.Should().Be(0);
        summary.PagesTouched.Should().Be(0);
        summary.SidecarRebuilt.Should().BeFalse();
        h.App.CallCount("UpdatePageContent").Should().Be(updates, "a dry run writes nothing");
        h.App.CallCount("SyncHierarchy").Should().Be(0);
        h.SavedSidecar().LastRefresh.Should().BeNull();
        progress.Should().Contain("Planning propagation").And.NotContain(p => p.StartsWith("Writing", StringComparison.Ordinal));
        RefreshText.DryRunLines(summary).Should().Contain(l => l.Contains("Mon Jan 4  objectiveLegend  create", StringComparison.Ordinal));
    }

    [Fact]
    public void Real_run_writes_updates_the_sidecar_syncs_and_a_second_run_is_empty()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Type(S.Objectives[1], "Hire two PMs");
        h.Type(S.StandingGoals[0], "Walk 30 min");

        RefreshResult first = h.Run(Request());

        first.Success.Should().BeTrue();
        first.ExitCode.Should().Be(0);
        RefreshSummary summary = first.Summary!;
        summary.ValuesWritten.Should().Be(104 + 2 + 2 + 2 + 90);
        summary.EventsWritten.Should().Be(0);
        summary.PagesTouched.Should().Be(104 + 1 + 2 + 1, "overviews and days, the first annual page (objectives 1 and 2 are both in its blocks), two objective pages and the scorecard");
        summary.Skipped.Should().Be(0);
        h.App.CallCount("SyncHierarchy").Should().Be(1);
        Sidecar saved = h.SavedSidecar();
        saved.LastRefresh.Should().Be(Now);
        saved.Pages[MonJan4.Value].Anchors.Should().ContainKeys("objectiveLegend", "standingGoal:1", "dailyDate", "prev", "next");
        saved.Pages["scorecard"].Anchors.Should().ContainKeys("scorecardRow:1", "scorecardRow:2");
        PageXml monday = h.Notebook.Read(MonJan4);
        monday.Outlines.Single(o => o.ObjectId == saved.Pages[MonJan4.Value].Anchors["objectiveLegend"]).PlainText.Should().Be("O1 Ship the connector  O2 Hire two PMs");
        monday.Outlines.Single(o => o.ObjectId == saved.Pages[MonJan4.Value].Anchors["standingGoal:1"]).PlainText.Should().Be("Walk 30 min");

        int updates = h.App.CallCount("UpdatePageContent");
        RefreshResult second = h.Run(Request());

        second.Success.Should().BeTrue();
        second.Summary!.Changes.Should().BeEmpty();
        second.Summary.PagesTouched.Should().Be(0);
        h.App.CallCount("UpdatePageContent").Should().Be(updates);
    }

    [Fact]
    public void A_blocked_slot_is_counted_as_skipped_and_the_users_note_is_untouched()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Notebook.TypeUserText(MonJan4, M.Slots.ObjectiveLegend.Rect, "my note");

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue();
        RefreshSummary summary = result.Summary!;
        summary.Skipped.Should().Be(1);
        summary.ValuesWritten.Should().Be(103 + 1 + 1 + 1);
        PlannedChange blocked = summary.Changes.Single(c => c.Blocked);
        blocked.Page.Should().Be(MonJan4);
        blocked.Reason.Should().Contain("my note");
        PageXml monday = h.Notebook.Read(MonJan4);
        monday.Outlines.Should().Contain(o => o.PlainText == "my note");
        monday.Outlines.Should().NotContain(o => o.PlainText.StartsWith("O1", StringComparison.Ordinal));
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Should().NotContainKey("objectiveLegend");
        RefreshText.SummaryLines(result, "SFP Q1 2027").Should().Contain(l => l.StartsWith("Skipped: Mon Jan 4", StringComparison.Ordinal));
    }

    [Fact]
    public void Inked_values_are_listed_but_written_only_when_confirmed()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Notebook.InkUserText(PageKey.Setup, S.Objectives[2], "Launch", "v2");

        RefreshResult dry = h.Run(Request(dryRun: true));
        dry.Summary!.Values.InkedSlots.Should().Equal("objective:3");
        dry.Summary.Changes.Where(c => c.Inked).Should().HaveCount(104 + 1 + 1 + 1, "every legend plus objective 3's row, heading and scorecard row");
        dry.Summary.Skipped.Should().Be(107, "inked changes count as skipped until confirmed");

        RefreshResult unconfirmed = h.Run(Request());
        unconfirmed.Summary!.ValuesWritten.Should().Be(3, "objective 1's row, heading and scorecard row");
        unconfirmed.Summary.Skipped.Should().Be(107);
        unconfirmed.Summary.Changes.Where(c => c.Blocked).Should().AllSatisfy(c => c.Reason.Should().Be(RefreshOrchestrator.InkedSkipReason));
        h.Notebook.Read(MonJan4).Outlines.Should().NotContain(o => o.PlainText.StartsWith("O1", StringComparison.Ordinal));

        RefreshResult confirmed = h.Run(Request(writeInked: true));
        confirmed.Summary!.ValuesWritten.Should().Be(107);
        confirmed.Summary.Skipped.Should().Be(0);
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v2");
    }

    [Fact]
    public void A_missing_sidecar_is_rebuilt_and_saved_even_on_a_dry_run()
    {
        Harness h = new(saveSidecar: false);
        File.Exists(h.SidecarPath).Should().BeFalse();

        RefreshResult result = h.Run(Request(dryRun: true, folder: h.Folder));

        result.Success.Should().BeTrue();
        result.Summary!.SidecarRebuilt.Should().BeTrue();
        result.Summary.Warnings.Should().Contain(SidecarRecovery.RebuiltWarning);
        Sidecar rebuilt = h.SavedSidecar();
        rebuilt.Pages.Should().HaveCount(144);
        rebuilt.ToolVersion.Should().Be("9.9.9");
        rebuilt.Quarter.Should().Be(new QuarterSpec(2027, 1));
    }

    [Fact]
    public void A_user_note_at_a_slots_origin_survives_a_sidecar_rebuild_and_blocks_that_slot()
    {
        Harness h = new(saveSidecar: false);
        h.Type(S.Objectives[0], "Ship the connector");
        string note = h.Notebook.AddOutlineAt(MonJan4, M.Slots.ObjectiveLegend.Rect, "my note", fixedWidth: true);

        RefreshResult result = h.Run(Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeTrue();
        PlannedChange legend = result.Summary.Changes.Single(c => c.Page == MonJan4 && c.SlotId == "objectiveLegend");
        legend.Blocked.Should().BeTrue("the note sits where the legend goes and is not the tool's");
        legend.Reason.Should().Contain("my note");
        h.Notebook.Read(MonJan4).Outlines.Single(o => o.ObjectId == note).PlainText.Should().Be("my note");
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Values.Should().NotContain(note);
        result.Summary.Changes.Count(c => c.SlotId == "objectiveLegend" && !c.Blocked).Should().Be(103);
    }

    [Fact]
    public void Exit_codes_cover_not_found_unavailable_and_write_failures()
    {
        Harness h = new();
        RefreshResult missing = h.Run(Request(name: "SFP Q9 2099"));
        missing.Success.Should().BeFalse();
        missing.ExitCode.Should().Be(2);
        missing.Error.Should().Contain("SFP Q9 2099");
        missing.Summary.Should().BeNull();

        h.Type(S.Objectives[0], "Ship the connector");
        h.App.UpdatePageContentFailure = id => id == h.Notebook.PageIds[PageKey.Scorecard] ? new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InvalidXml, "rejected", null, null) : null;
        RefreshResult failed = h.Run(Request());
        failed.Success.Should().BeFalse();
        failed.ExitCode.Should().Be(5);
        failed.Error.Should().Contain("Quarter scorecard").And.Contain("rejected");
        failed.Summary.Should().NotBeNull();
        failed.Summary!.Changes.Count(c => c.Blocked).Should().BeGreaterThan(0);
        h.App.CallCount("SyncHierarchy").Should().Be(0, "a failed run does not sync");
        h.SavedSidecar().LastRefresh.Should().Be(Now, "what was written before the failure is recorded");

        h.App.Unavailable = new OneNoteUnavailableException();
        RefreshResult unavailable = h.Run(Request());
        unavailable.ExitCode.Should().Be(3);
        unavailable.Error.Should().Be(OneNoteUnavailableException.UserMessage);
    }

    [Fact]
    public void Calendar_run_writes_events_removes_a_cancelled_one_and_keeps_them_when_every_source_fails()
    {
        StubCalendarFactory calendars = new();
        Harness h = new(calendars: calendars);
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        DateTime start = new(2027, 1, 5, 9, 0, 0);
        calendars.Events = new[] { new CalendarEvent("m1@2027-01-05T09:00", "Staff meeting", start, start.AddHours(1), false, false, false, "Work", null) };
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));

        RefreshResult first = h.Run(Request(calendar: true));

        first.Success.Should().BeTrue();
        first.ExitCode.Should().Be(0);
        first.Summary!.EventsWritten.Should().Be(2, "the 9:00 row on Tue Jan 5 and Tuesday's column on Week 02's overview");
        first.Summary.ValuesWritten.Should().Be(0);
        first.Summary.FrozenThrough.Should().Be(new DateOnly(2027, 1, 3));
        first.Summary.FailedSources.Should().BeEmpty();
        calendars.Windows.Should().ContainSingle().Which.Should().Be((Jan4, Jan4.AddDays(14)));
        Sidecar saved = h.SavedSidecar();
        saved.FrozenThrough.Should().Be(new DateOnly(2027, 1, 3));
        string rowId = saved.Pages[tueJan5.Value].Anchors["timeBlockEvent:9"];
        h.Notebook.Read(tueJan5).Outlines.Single(o => o.ObjectId == rowId).PlainText.Should().Be("9:00 Staff meeting");
        string columnId = saved.Pages["week:02:overview"].Anchors["overviewDay:1"];
        h.Notebook.Read(PageKey.WeekOverview(2)).Outlines.Single(o => o.ObjectId == columnId).PlainText.Should().Be("09:00 Staff meeting");
        File.Exists(FailureCounter.PathBeside(h.SidecarPath)).Should().BeTrue();

        calendars.Events = new[] { new CalendarEvent("m1@2027-01-05T09:00", "Staff meeting", start, start.AddHours(1), false, false, true, "Work", null) };
        int deletes = h.App.CallCount("DeletePageContent");
        RefreshResult second = h.Run(Request(calendar: true, today: Jan4.AddDays(1)));

        second.Summary!.Changes.Should().HaveCount(2).And.AllSatisfy(c => c.Kind.Should().Be(ChangeKind.Remove));
        second.Summary.EventsWritten.Should().Be(0);
        second.Summary.PagesTouched.Should().Be(2);
        h.App.CallCount("DeletePageContent").Should().Be(deletes + 2, "the fake keeps an outline the XML leaves out, as OneNote does, so only DeletePageContent removes the two lines");
        h.App.Calls.Where(c => c.Call == "DeletePageContent").Select(c => c.Detail).Should().Contain(h.Notebook.PageIds[tueJan5] + " " + rowId);
        h.Notebook.Read(tueJan5).Outlines.Should().NotContain(o => o.ObjectId == rowId);
        h.SavedSidecar().Pages[tueJan5.Value].Anchors.Should().NotContainKey("timeBlockEvent:9");
        h.SavedSidecar().FrozenThrough.Should().Be(Jan4);

        calendars.Events = new[] { new CalendarEvent("m2@2027-01-07T10:00", "Review", new DateTime(2027, 1, 7, 10, 0, 0), new DateTime(2027, 1, 7, 11, 0, 0), false, false, false, "Work", null) };
        h.Run(Request(calendar: true, today: Jan4.AddDays(1))).Summary!.EventsWritten.Should().Be(2);
        calendars.Failure = new HttpRequestException("timeout");
        RefreshResult outage = h.Run(Request(calendar: true, today: Jan4.AddDays(2)));

        outage.Success.Should().BeFalse();
        outage.ExitCode.Should().Be(6);
        outage.Summary.Should().NotBeNull();
        outage.Summary!.FailedSources.Should().ContainSingle().Which.Should().StartWith("Work: timeout");
        outage.Summary.Changes.Should().BeEmpty("a feed outage never removes what was written");
        outage.Summary.Warnings.Should().Contain(w => w.Contains("Every calendar source failed", StringComparison.Ordinal));
        PageKey thuJan7 = PageKey.Day(new DateOnly(2027, 1, 7));
        h.Notebook.Read(thuJan7).Outlines.Should().Contain(o => o.PlainText == "10:00 Review");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1);
    }

    [Fact]
    public void Calendar_rows_follow_the_window_the_sidecar_records_and_an_event_before_it_goes_in_the_first_row()
    {
        StubCalendarFactory calendars = new();
        TimeBlockWindow generated = new(8, 18);
        Harness h = new(calendars: calendars, window: generated);
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        h.Type(S.TimeBlockStart, "6");
        h.Type(S.TimeBlockEnd, "22");
        DateTime nine = new(2027, 1, 5, 9, 0, 0);
        DateTime seven = new(2027, 1, 5, 7, 0, 0);
        calendars.Events = new[]
        {
            new CalendarEvent("m1@2027-01-05T09:00", "Staff meeting", nine, nine.AddHours(1), false, false, false, "Work", null),
            new CalendarEvent("g1@2027-01-05T07:00", "Gym", seven, seven.AddHours(1), false, false, false, "Work", null),
        };
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));

        RefreshResult result = h.Run(Request(calendar: true));

        result.Success.Should().BeTrue(result.Error);
        Sidecar saved = h.SavedSidecar();
        saved.TimeBlocks.Should().Be(generated, "Refresh keeps the window the pages were generated with");
        IReadOnlyDictionary<string, string> anchors = saved.Pages[tueJan5.Value].Anchors;
        anchors.Should().ContainKeys("timeBlockEvent:9", "timeBlockEvent:8").And.NotContainKey("timeBlockEvent:7", "7 is outside the 8 to 18 window the pages show");
        PageXml page = h.Notebook.Read(tueJan5);
        OutlineInfo nineRow = page.Outlines.Single(o => o.ObjectId == anchors["timeBlockEvent:9"]);
        nineRow.PlainText.Should().Be("9:00 Staff meeting");
        page.Outlines.Single(o => o.ObjectId == anchors["timeBlockEvent:8"]).PlainText.Should().Be("7:00 Gym", "an event before the window is listed in its first row with its time");
        SlotCatalog pages = new(M, new DateEngine(), h.Notebook.Plan.Quarter, generated);
        double rowY = pages.Slot(h.Notebook.Plan.Page(tueJan5), "timeBlockEvent:9")!.Rect.Y + page.SheetOutline!.Y + M.OneNote.AnchorPaddingPt.Y;
        nineRow.Y.Should().BeApproximately(rowY, 0.01, "the line sits on the 9 row of the 8 to 18 column, not of the Setup page's 6 to 22");
        h.Log.Messages(LogEventLevel.Information).Should().Contain(m => m.Contains("Time-block window 8 to 18 from the sidecar", StringComparison.Ordinal));
        h.Log.Messages(LogEventLevel.Information).Should().Contain(m => m.Contains("The Setup page asks for 6 to 22", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sidecar_without_a_window_places_by_the_setup_page_window_as_before_and_logs_it()
    {
        StubCalendarFactory calendars = new();
        Harness h = new(calendars: calendars);
        h.Notebook.Sidecar.TimeBlocks.Should().BeNull("a sidecar written before the window was recorded");
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        h.Type(S.TimeBlockStart, "8");
        h.Type(S.TimeBlockEnd, "18");
        DateTime seven = new(2027, 1, 5, 7, 0, 0);
        calendars.Events = new[] { new CalendarEvent("g1@2027-01-05T07:00", "Gym", seven, seven.AddHours(1), false, false, false, "Work", null) };
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));

        RefreshResult result = h.Run(Request(calendar: true));

        result.Success.Should().BeTrue(result.Error);
        Sidecar saved = h.SavedSidecar();
        saved.Pages[tueJan5.Value].Anchors.Should().ContainKey("timeBlockEvent:8").And.NotContainKey("timeBlockEvent:7");
        saved.TimeBlocks.Should().BeNull("Refresh does not invent the window the pages were generated with");
        h.Log.Messages(LogEventLevel.Information).Should().Contain(m => m.Contains("Time-block window 8 to 18 from the Setup page", StringComparison.Ordinal));
    }

    [Fact]
    public void The_calendar_window_is_the_sidecars_else_the_setup_pages_else_the_default()
    {
        Sidecar recorded = new()
        {
            ToolVersion = "1.0.0",
            ManifestId = M.TemplateId,
            Quarter = new QuarterSpec(2027, 1),
            NotebookName = "SFP Q1 2027",
            NotebookPath = "/notebooks/SFP Q1 2027",
            NotebookId = "{nb}",
            TimeBlocks = new TimeBlockWindow(8, 18),
        };
        Sidecar old = recorded with { TimeBlocks = null };
        SetupValues typed = SetupValues.Empty with { TimeBlockWindow = (7, 21) };

        RefreshOrchestrator.CalendarWindow(recorded, typed).Should().Be((new TimeBlockWindow(8, 18), TimeBlockWindowSource.Sidecar));
        RefreshOrchestrator.CalendarWindow(old, typed).Should().Be((new TimeBlockWindow(7, 21), TimeBlockWindowSource.SetupPage));
        RefreshOrchestrator.CalendarWindow(recorded with { TimeBlocks = new TimeBlockWindow(20, 4) }, typed)
            .Should().Be((new TimeBlockWindow(7, 21), TimeBlockWindowSource.SetupPage), "a recorded window that is not valid is ignored");
        RefreshOrchestrator.CalendarWindow(old, SetupValues.Empty).Should().Be((TimeBlockWindow.Default, TimeBlockWindowSource.Default));
        RefreshOrchestrator.CalendarWindow(old, SetupValues.Empty with { TimeBlockWindow = (22, 6) }).Should().Be((TimeBlockWindow.Default, TimeBlockWindowSource.Default));
    }

    [Fact]
    public void Calendar_without_a_source_factory_warns_and_the_bundled_holidays_are_added_when_asked()
    {
        Harness none = new();
        RefreshResult result = none.Run(Request(dryRun: true, calendar: true));
        result.Summary!.Warnings.Should().Contain(w => w.Contains("not available", StringComparison.Ordinal));

        StubCalendarFactory calendars = new();
        Harness h = new(calendars: calendars, holidays: true);
        h.Run(Request(dryRun: true, calendar: true, today: new DateOnly(2027, 1, 10)));
        calendars.Created.Select(s => s.Kind).Should().Equal(CalendarSourceKind.Holidays);
        h.Run(Request(dryRun: true, calendar: true, today: new DateOnly(2027, 1, 10))).Summary!.Changes
            .Should().Contain(c => c.Page == PageKey.Day(new DateOnly(2027, 1, 18)) && c.SlotId == "allDayBand" && c.NewText.Contains("Martin Luther King", StringComparison.Ordinal));
    }

    /// <summary>A calendar source factory handing out one stub source for every Setup line, with the events and failure the test sets.</summary>
    private sealed class StubCalendarFactory : ICalendarSourceFactory
    {
        public IReadOnlyList<CalendarEvent> Events { get; set; } = Array.Empty<CalendarEvent>();

        public Exception? Failure { get; set; }

        public List<(DateOnly From, DateOnly To)> Windows { get; } = new();

        public List<CalendarSource> Created { get; } = new();

        public ICalendarSource Create(CalendarSource source)
        {
            Created.Add(source);
            return source.Kind == CalendarSourceKind.Holidays
                ? new HolidayCalendarSource(source, HolidayList.Load(Path.Combine(TestPaths.TemplatesDir, "holidays-us.json")))
                : new StubSource(this, source);
        }

        private sealed class StubSource : ICalendarSource
        {
            private readonly StubCalendarFactory owner;
            private readonly CalendarSource source;

            public StubSource(StubCalendarFactory owner, CalendarSource source)
            {
                this.owner = owner;
                this.source = source;
            }

            public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
            {
                owner.Windows.Add((from, to));
                if (owner.Failure is not null)
                {
                    throw owner.Failure;
                }

                return owner.Events.Select(e => e with { SourceLabel = source.Label }).ToList();
            }
        }
    }
}
