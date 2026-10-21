using Serilog.Events;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
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

    /// <summary>The sidecar schema the OneNote tests validate the generated file with.</summary>
    private static string SidecarSchemaPath => Path.Combine(TestPaths.RepoRoot, "tests", "Sfp.OneNote.Tests", "schema", "sfp-state.schema.json");

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

        public RefreshServices Services { get; set; }

        public FakeOneNoteApp App => Notebook.App;

        public RefreshResult Run(RefreshRequest request, List<string>? progress = null) =>
            new RefreshOrchestrator(Services).Run(request, progress is null ? null : new ListProgress(progress), CancellationToken.None);

        public RefreshResult Run(RefreshRequest request, IProgress<string> progress, CancellationToken cancellationToken) =>
            new RefreshOrchestrator(Services).Run(request, progress, cancellationToken);

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
    public void Text_the_user_typed_into_a_pre_fill_is_left_alone_and_reported_while_untouched_pre_fills_update()
    {
        Harness h = new();
        h.Type(S.StandingGoals[0], "Walk 30 min");
        h.Run(Request()).Summary!.ValuesWritten.Should().Be(90);
        Sidecar first = h.SavedSidecar();
        first.Pages[MonJan4.Value].AnchorTexts.Should().Contain("standingGoal:1", "Walk 30 min", "the sidecar records what the tool wrote");
        JsonSchemaLite.AssertValid(File.ReadAllText(SidecarSchemaPath), File.ReadAllText(h.SidecarPath));
        string goalId = first.Pages[MonJan4.Value].Anchors["standingGoal:1"];
        SlotGeometry goal = h.Notebook.Catalog.Slot(h.Notebook.Plan.Page(MonJan4), "standingGoal:1")!;
        h.Notebook.EditOutlineText(MonJan4, goalId, AnchorWriter.Html("standingGoal:1", "Walk 30 min, then stretch", goal, h.Notebook.Options));
        string setupGoal = h.Notebook.Read(PageKey.Setup).Outlines.Single(o => o.PlainText == "Walk 30 min").ObjectId;
        h.Notebook.EditOutlineText(PageKey.Setup, setupGoal, "Walk 45 min");

        RefreshResult second = h.Run(Request());

        second.Success.Should().BeTrue(second.Error);
        PlannedChange edited = second.Summary!.Changes.Single(c => c.Page == MonJan4);
        edited.Blocked.Should().BeTrue();
        edited.Reason.Should().Be(Propagator.EditedReason);
        h.Notebook.Read(MonJan4).Outlines.Single(o => o.ObjectId == goalId).PlainText.Should().Be("Walk 30 min, then stretch", "what the user typed is never overwritten");
        second.Summary.ValuesWritten.Should().Be(89, "every pre-fill the user did not touch updates normally");
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
        Sidecar saved = h.SavedSidecar();
        h.Notebook.Read(tueJan5).Outlines.Single(o => o.ObjectId == saved.Pages[tueJan5.Value].Anchors["standingGoal:1"]).PlainText.Should().Be("Walk 45 min");
        saved.Pages[tueJan5.Value].AnchorTexts["standingGoal:1"].Should().Be("Walk 45 min");
        saved.Pages[MonJan4.Value].AnchorTexts["standingGoal:1"].Should().Be("Walk 30 min", "the edited outline keeps the text the tool wrote, so it stays the user's");
        RefreshText.SummaryLines(second, "SFP Q1 2027").Should().Contain(l => l.StartsWith("Skipped: Mon Jan 4  standingGoal:1", StringComparison.Ordinal) && l.Contains("edited by the user", StringComparison.Ordinal));

        h.Run(Request()).Summary!.Changes.Should().ContainSingle().Which.Reason.Should().Be(Propagator.EditedReason, "it stays the user's on later runs");
    }

    [Fact]
    public void A_calendar_line_the_user_typed_into_is_left_alone_when_its_event_changes()
    {
        StubCalendarFactory calendars = new();
        Harness h = new(calendars: calendars);
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        DateTime nine = new(2027, 1, 5, 9, 0, 0);
        calendars.Events = new[] { new CalendarEvent("m1@2027-01-05T09:00", "Staff meeting", nine, nine.AddHours(1), false, false, false, "Work", null) };
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
        h.Run(Request(calendar: true)).Summary!.EventsWritten.Should().Be(2);
        Sidecar saved = h.SavedSidecar();
        saved.Pages[tueJan5.Value].AnchorTexts.Should().Contain("timeBlockEvent:9", "9:00 Staff meeting");
        string rowId = saved.Pages[tueJan5.Value].Anchors["timeBlockEvent:9"];
        h.Notebook.EditOutlineText(tueJan5, rowId, "9:00 Staff meeting, bring the slides");
        calendars.Events = new[] { new CalendarEvent("m1@2027-01-05T09:00", "Staff sync", nine, nine.AddHours(1), false, false, false, "Work", null) };

        RefreshResult second = h.Run(Request(calendar: true));

        PlannedChange row = second.Summary!.Changes.Single(c => c.Page == tueJan5);
        row.Blocked.Should().BeTrue();
        row.Reason.Should().Be(Propagator.EditedReason);
        h.Notebook.Read(tueJan5).Outlines.Single(o => o.ObjectId == rowId).PlainText.Should().Be("9:00 Staff meeting, bring the slides");
        second.Summary.EventsWritten.Should().Be(1, "Tuesday's column on the overview, which the user did not touch, is updated");
    }

    [Fact]
    public void A_note_written_over_a_current_legend_is_reported_as_skipped_and_left_alone()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Run(Request()).Summary!.Skipped.Should().Be(0);
        h.Notebook.TypeUserText(MonJan4, M.Slots.ObjectiveLegend.Rect, "my note");
        string pageId = h.Notebook.PageIds[MonJan4];
        int updates = h.App.UpdateCount(pageId);

        RefreshResult again = h.Run(Request());

        again.Success.Should().BeTrue();
        PlannedChange skipped = again.Summary!.Changes.Should().ContainSingle("the legend is current everywhere else").Which;
        skipped.Page.Should().Be(MonJan4);
        skipped.SlotId.Should().Be("objectiveLegend");
        skipped.Blocked.Should().BeTrue();
        skipped.Reason.Should().Be("A user object overlaps the slot: text 'my note'. The tool's text there is already current and was left as it is.");
        again.Summary.Skipped.Should().Be(1);
        again.Summary.PagesTouched.Should().Be(0);
        h.App.UpdateCount(pageId).Should().Be(updates, "nothing is written for a reported slot");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "my note");
        RefreshText.SummaryLines(again, "SFP Q1 2027").Should().Contain(l => l.StartsWith("Skipped: Mon Jan 4  objectiveLegend", StringComparison.Ordinal));
    }

    [Fact]
    public void Inked_values_are_listed_but_written_only_when_confirmed()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Notebook.InkUserText(PageKey.Setup, S.Objectives[2], "Launch", "v2");

        RefreshResult dry = h.Run(Request(dryRun: true));
        dry.Summary!.Values.InkedSlots.Should().Equal("objective:3");
        dry.Summary.Changes.Where(c => c.Inked).Select(c => c.SlotId).Should().BeEquivalentTo(new[] { "annualRow:3", "objectiveHeading", "scorecardRow:3" }, "the inked objective is listed in its own slots");
        dry.Summary.Changes.Where(c => c.SlotId == "objectiveLegend").Should().HaveCount(104).And.AllSatisfy(c =>
        {
            c.NewText.Should().Be("O1 Ship the connector", "without confirmation the legend is built from the typed values");
            c.Inked.Should().BeFalse();
        });
        dry.Summary.Skipped.Should().Be(3, "inked changes count as skipped until confirmed");

        RefreshResult unconfirmed = h.Run(Request());
        unconfirmed.Summary!.ValuesWritten.Should().Be(104 + 3, "every legend with objective 1, and objective 1's row, heading and scorecard row");
        unconfirmed.Summary.Skipped.Should().Be(3);
        unconfirmed.Summary.Changes.Where(c => c.Blocked).Should().AllSatisfy(c =>
        {
            c.Reason.Should().Be(RefreshOrchestrator.InkedSkipReason);
            c.NewText.Should().Be("Launch v2");
        });
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector", "one inked objective no longer holds back the legend on 104 pages");

        RefreshResult confirmed = h.Run(Request(writeInked: true));
        confirmed.Summary!.ValuesWritten.Should().Be(104 + 3, "every legend gains O3 and objective 3's row, heading and scorecard row are written");
        confirmed.Summary.Changes.Where(c => c.SlotId == "objectiveLegend").Should().AllSatisfy(c =>
        {
            c.Kind.Should().Be(ChangeKind.Update);
            c.Inked.Should().BeTrue("the legend now includes an inked value");
        });
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
    public void A_sidecar_outside_the_search_folders_is_found_beside_the_notebook_and_not_rebuilt()
    {
        Harness h = new();
        h.Services = h.Services with { SearchFolders = new[] { TestPaths.NewTempDir("refresh-elsewhere") } };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeFalse("the sidecar beside the notebook's folder is the one generated with it");
        result.Summary.Warnings.Should().NotContain(SidecarRecovery.RebuiltWarning);
        Sidecar saved = h.SavedSidecar();
        saved.Generated.Should().Be(FakeNotebook.GeneratedAt, "the generated sidecar was updated, not replaced by a rebuilt one");
        saved.Pages[MonJan4.Value].Anchors.Should().ContainKey("objectiveLegend");
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
    public void With_the_bundled_holidays_on_a_dead_setup_source_still_exits_6_and_builds_the_streak()
    {
        StubCalendarFactory calendars = new() { Failure = new HttpRequestException("timeout") };
        Harness h = new(calendars: calendars, holidays: true);
        h.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");

        RefreshResult first = h.Run(Request(calendar: true));

        calendars.Created.Select(s => s.Kind).Should().Contain(CalendarSourceKind.Holidays, "the bundled list was read and did not fail");
        first.Success.Should().BeFalse();
        first.ExitCode.Should().Be(6, "every source on the Setup page failed; the tool's own holiday list does not count");
        first.Summary!.FailedSources.Should().ContainSingle().Which.Should().StartWith("Work: timeout");
        first.Summary.Changes.Should().BeEmpty("a feed outage never removes what was written");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1);
        h.Notifier.Notifications.Should().BeEmpty();

        h.Run(Request(calendar: true, today: Jan4.AddDays(1))).ExitCode.Should().Be(6);
        h.Run(Request(calendar: true, today: Jan4.AddDays(2))).ExitCode.Should().Be(6);

        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(3);
        (string title, string message) = h.Notifier.Notifications.Should().ContainSingle().Which;
        title.Should().Be(CalendarImportRunner.ToastTitle);
        message.Should().Be("All 1 calendar source(s) failed 3 runs in a row. Last error: Work: timeout");
    }

    [Fact]
    public void OneNote_failures_of_real_runs_count_toward_the_toast_and_a_success_resets_the_streak()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.App.UpdatePageContentFailure = _ => new OneNoteException("UpdatePageContent", OneNoteHResults.PageReadOnly, "The page is read-only.", null);

        for (int run = 1; run <= 3; run++)
        {
            RefreshResult failed = h.Run(Request());
            failed.ExitCode.Should().Be(5);
            FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(run);
            h.Notifier.Notifications.Should().HaveCount(run < 3 ? 0 : 1, "the toast comes with the third failure in a row");
        }

        (string title, string message) = h.Notifier.Notifications.Single();
        title.Should().Be(RefreshOrchestrator.RefreshFailureToastTitle);
        message.Should().StartWith("Refresh of SFP Q1 2027 failed 3 runs in a row. Last error: Writing page").And.Contain("read-only");

        h.Run(Request(dryRun: true));
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(3, "a dry run writes nothing and leaves the streak alone");

        h.App.UpdatePageContentFailure = null;
        h.Run(Request()).ExitCode.Should().Be(0);
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(0, "a run that succeeds clears the streak");

        h.App.Unavailable = new OneNoteUnavailableException();
        h.Run(Request()).ExitCode.Should().Be(3);
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1, "OneNote unavailable counts too once the sidecar path is known from the folders");
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

    [Fact]
    public void Outlines_written_before_a_cancel_are_recorded_and_the_next_run_has_no_blocked_slots()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        using CancellationTokenSource cancel = new();
        OnPageWrite progress = new(2, cancel.Cancel);

        RefreshResult cancelled = h.Run(Request(), progress, cancel.Token);

        cancelled.Success.Should().BeFalse();
        cancelled.ExitCode.Should().Be(5);
        cancelled.Error.Should().Be("Cancelled.");
        progress.Pages.Should().Equal("Writing Annual Objectives", "Writing Objective 1");
        Sidecar saved = h.SavedSidecar();
        saved.Pages[PageKey.Annual(1).Value].Anchors.Should().ContainKey("annualRow:1", "the outlines written before the cancel are recorded");
        saved.Pages[PageKey.Objective(1).Value].Anchors.Should().ContainKey("objectiveHeading");
        saved.Pages[PageKey.Objective(1).Value].AnchorTexts.Should().Contain("objectiveHeading", "Ship the connector");
        saved.Pages[MonJan4.Value].Anchors.Should().NotContainKey("objectiveLegend");
        File.Exists(FailureCounter.PathBeside(h.SidecarPath)).Should().BeFalse("a cancelled run is not a failure of the daily task");

        RefreshResult next = h.Run(Request());

        next.Success.Should().BeTrue(next.Error);
        next.Summary!.Changes.Should().NotContain(c => c.Blocked, "the outlines the cancelled run wrote are the tool's, not user objects in the slots");
        next.Summary.ValuesWritten.Should().Be(104 + 1, "every legend and the scorecard row; the two pages written before the cancel are current");
        h.Run(Request()).Summary!.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Outlines_written_before_OneNote_went_away_are_recorded_too()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        OnPageWrite progress = new(3, () => h.App.Unavailable = new OneNoteUnavailableException());

        RefreshResult failed = h.Run(Request(), progress, CancellationToken.None);

        failed.ExitCode.Should().Be(3);
        h.App.Unavailable = null;
        Sidecar saved = h.SavedSidecar();
        saved.Pages[PageKey.Annual(1).Value].Anchors.Should().ContainKey("annualRow:1");
        saved.Pages[PageKey.Objective(1).Value].Anchors.Should().ContainKey("objectiveHeading");
        h.Run(Request()).Summary!.Changes.Should().NotContain(c => c.Blocked);
    }

    [Fact]
    public void A_slot_the_user_filled_after_planning_is_blocked_when_the_page_is_read_again_before_writing()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Services = h.Services with { Propagators = (catalog, reader) => new ThenEdit(new Propagator(catalog, reader, Mono), () => h.Notebook.TypeUserText(MonJan4, M.Slots.ObjectiveLegend.Rect, "typed meanwhile")) };

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue(result.Error);
        PlannedChange legend = result.Summary!.Changes.Single(c => c.Page == MonJan4);
        legend.Blocked.Should().BeTrue("the band was empty when the run planned and holds the user's note when it writes");
        legend.Reason.Should().Contain("typed meanwhile");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "typed meanwhile").And.NotContain(o => o.PlainText.StartsWith("O1", StringComparison.Ordinal));
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Should().NotContainKey("objectiveLegend");
        result.Summary.ValuesWritten.Should().Be(103 + 3);
    }

    [Fact]
    public void An_edit_made_after_planning_is_not_written_over()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string note = h.Notebook.AddOutlineAt(MonJan4, new RectPt(400, 400, 200, 14), "draft", fixedWidth: false);
        h.Services = h.Services with { Propagators = (catalog, reader) => new ThenEdit(new Propagator(catalog, reader, Mono), () => h.Notebook.EditOutlineText(MonJan4, note, "final")) };

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue(result.Error);
        PageXml monday = h.Notebook.Read(MonJan4);
        monday.Outlines.Single(o => o.ObjectId == note).PlainText.Should().Be("final", "the page is read again before the write, so the copy read while planning is never sent back over the user's edit");
        monday.Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
    }

    [Fact]
    public void A_page_that_changes_between_the_read_and_the_write_is_read_again_and_written()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string pageId = h.Notebook.PageIds[MonJan4];
        int edits = 0;
        h.App.BeforeWrite = id =>
        {
            if (id == pageId && edits++ == 0)
            {
                h.Notebook.TypeUserText(MonJan4, new RectPt(400, 400, 200, 14), "typed meanwhile");
            }
        };
        int updates = h.App.UpdateCount(pageId);

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.Changes.Should().NotContain(c => c.Blocked);
        h.App.UpdateCount(pageId).Should().Be(updates + 3, "the refused write, the user's edit inside it and the write after reading the page again");
        h.Log.Messages(LogEventLevel.Warning).Should().Contain(m => m.Contains("Mon Jan 4", StringComparison.Ordinal) && m.Contains("changed while it was being written; reading it again", StringComparison.Ordinal));
        PageXml monday = h.Notebook.Read(MonJan4);
        monday.Outlines.Should().Contain(o => o.PlainText == "typed meanwhile");
        monday.Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Should().ContainKey("objectiveLegend");
    }

    [Fact]
    public void A_page_that_keeps_changing_is_reported_and_left_for_the_next_run()
    {
        Harness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string pageId = h.Notebook.PageIds[MonJan4];
        int edits = 0;
        h.App.BeforeWrite = id =>
        {
            if (id == pageId)
            {
                edits++;
                h.Notebook.TypeUserText(MonJan4, new RectPt(400, 380 + (20 * edits), 200, 14), "typing " + edits);
            }
        };

        RefreshResult result = h.Run(Request());

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0, "a page the user is editing is not a failure; it is tried again next run");
        edits.Should().Be(2, "one write and one retry");
        PlannedChange legend = result.Summary!.Changes.Single(c => c.Page == MonJan4);
        legend.Blocked.Should().BeTrue();
        legend.Reason.Should().Be(RefreshOrchestrator.ConflictReason);
        result.Summary.Warnings.Should().Contain("Page Mon Jan 4 changed while Refresh was writing it; its changes were not written and will be tried again on the next run.");
        result.Summary.ValuesWritten.Should().Be(103 + 3, "every other page is written");
        h.Notebook.Read(MonJan4).Outlines.Should().NotContain(o => o.PlainText.StartsWith("O1", StringComparison.Ordinal));

        h.App.BeforeWrite = null;
        h.Run(Request()).Summary!.Changes.Should().ContainSingle().Which.Should().Match<PlannedChange>(c => c.Page == MonJan4 && !c.Blocked, "the next run writes it");
    }

    /// <summary>Plans with the real propagator, then runs an action: the user editing a page between planning and writing.</summary>
    private sealed class ThenEdit : IPropagator
    {
        private readonly IPropagator inner;
        private readonly Action edit;

        public ThenEdit(IPropagator inner, Action edit)
        {
            this.inner = inner;
            this.edit = edit;
        }

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s) => Then(inner.Plan(plan, v, s));

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues) => Then(inner.Plan(plan, v, s, writeInkedValues));

        private IReadOnlyList<PlannedChange> Then(IReadOnlyList<PlannedChange> changes)
        {
            edit();
            return changes;
        }
    }

    /// <summary>Runs an action when the run reports it is writing its n-th page (not the "Writing changes" phase line).</summary>
    private sealed class OnPageWrite : IProgress<string>
    {
        private readonly int page;
        private readonly Action action;

        public OnPageWrite(int page, Action action)
        {
            this.page = page;
            this.action = action;
        }

        public List<string> Pages { get; } = new();

        public void Report(string value)
        {
            if (!value.StartsWith("Writing ", StringComparison.Ordinal) || value == "Writing changes")
            {
                return;
            }

            Pages.Add(value);
            if (Pages.Count == page)
            {
                action();
            }
        }
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
