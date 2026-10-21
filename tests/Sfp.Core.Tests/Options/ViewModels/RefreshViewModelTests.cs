using Sfp.App.ViewModels.Refresh;
using Sfp.Core.Dates;
using Sfp.Core.Plan;
using Sfp.Refresh;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>M6.5: the notebook list, Preview changes, Refresh now, the daily task checkbox and the run summary.</summary>
public class RefreshViewModelTests
{
    private const string Q1Path = @"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027";
    private const string Q4Path = @"D:\Notebooks\SFP Q4 2026";

    private readonly Harness _h = new();

    private static KnownNotebook Q1 => new("SFP Q1 2027", Q1Path, new QuarterSpec(2027, 1), new DateTimeOffset(2026, 12, 20, 9, 30, 0, TimeSpan.Zero), null, Q1Path + ".sfp-state.json");

    private static KnownNotebook Q4 => new("SFP Q4 2026", Q4Path, new QuarterSpec(2026, 10), new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero), Q4Path + ".sfp-state.json");

    private static SetupValues Values() => new(
        new string?[] { "Ship the connector", "Hire two PMs", null, null, null, null, null, null },
        new string?[] { "Inbox zero", null, null },
        new string?[] { "Steps", "Sleep", "Deep work" },
        new string?[16],
        new[] { new CalendarSource("Work", CalendarSourceKind.IcsUrl, "https://x/cal.ics", null, null) },
        AnyInked: true)
    {
        InkedSlots = new[] { "objective:2" },
        TimeBlockWindow = (6, 22),
    };

    private static RefreshSummary Summary() => new()
    {
        Values = Values(),
        Changes = new[]
        {
            new PlannedChange(PageKey.WeekOverview(1), "objectiveLegend", null, "O1 Ship the connector  O2 Hire two PMs", ChangeKind.Create, false, null),
            new PlannedChange(PageKey.Day(new DateOnly(2027, 1, 4)), "standingGoal:1", "Old", "Inbox zero", ChangeKind.Update, true, "a user outline sits in the slot"),
            new PlannedChange(PageKey.Objective(2), "objectiveHeading", null, "Hire two PMs", ChangeKind.Create, false, null),
        },
        PagesTouched = 2,
        ValuesWritten = 2,
        EventsWritten = 5,
        Skipped = 1,
        FailedSources = new[] { "Holidays" },
        Warnings = new[] { "Sidecar had a stale page id" },
        SidecarRebuilt = true,
    };

    private async Task<RefreshViewModel> Loaded()
    {
        _h.Registry.Notebooks.Add(Q1);
        _h.Registry.Notebooks.Add(Q4);
        RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(new[] { @"D:\Notebooks", Harness.DefaultFolder });
        await vm.PendingReload;
        return vm;
    }

    [UiFact]
    public async Task The_list_comes_from_the_registry_over_the_known_folders_plus_the_default_folder()
    {
        RefreshViewModel vm = await Loaded();

        vm.Folders.Should().Equal(@"D:\Notebooks", Harness.DefaultFolder);
        _h.Registry.Queries.Should().ContainSingle().Which.Should().Equal(@"D:\Notebooks", Harness.DefaultFolder);
        vm.Notebooks.Select(n => n.Name).Should().Equal("SFP Q1 2027", "SFP Q4 2026");
        vm.SelectedNotebook!.Name.Should().Be("SFP Q1 2027");
        vm.HasSelection.Should().BeTrue();
        vm.NotebooksStatus.Should().BeNull();

        KnownNotebookItem q4 = vm.Notebooks[1];
        q4.QuarterLabel.Should().Be("Q4 2026");
        q4.GeneratedText.Should().Be("2026-09-25 08:00");
        q4.LastRefreshText.Should().Be("2026-10-19 05:00");
        vm.Notebooks[0].LastRefreshText.Should().Be("never");
        q4.ToString().Should().Be("SFP Q4 2026");
    }

    [UiFact]
    public async Task Default_folder_is_listed_once_even_when_it_is_also_a_known_folder()
    {
        RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(new[] { Harness.DefaultFolder.ToUpperInvariant(), "" });
        await vm.PendingReload;

        vm.Folders.Should().ContainSingle();
        vm.NotebooksStatus.Should().StartWith("No notebooks built by this tool were found under");
    }

    [UiFact]
    public async Task A_registry_failure_is_a_status_line()
    {
        _h.Registry.Throw = new NotSupportedException("The notebook registry (M4.3) is not wired into the app yet.");
        RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;

        vm.Notebooks.Should().BeEmpty();
        vm.NotebooksStatus.Should().Be("Could not list notebooks: The notebook registry (M4.3) is not wired into the app yet.");
        vm.PreviewChangesCommand.CanExecute(null).Should().BeFalse();
        vm.ToRequest(dryRun: true).Should().BeNull();
    }

    [UiFact]
    public async Task Preview_changes_runs_a_dry_run_and_lists_every_change_with_blocked_and_inked_marks()
    {
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary(), Elapsed = TimeSpan.FromSeconds(3) };
        _h.Refresher.ProgressToReport.Add("Reading Setup");
        RefreshViewModel vm = await Loaded();
        Task loadedReload = vm.PendingReload;
        vm.HorizonDays = 21;

        await vm.PreviewChangesCommand.ExecuteAsync(null);

        RefreshRequest request = _h.Refresher.LastRequest!;
        request.NotebookName.Should().Be("SFP Q1 2027");
        request.NotebooksFolder.Should().Be(Harness.DefaultFolder);
        request.DryRun.Should().BeTrue();
        request.Calendar.Should().BeTrue();
        request.HorizonDays.Should().Be(21);
        request.WriteInkedValues.Should().BeFalse();
        request.Today.Should().Be(new DateOnly(2026, 10, 20));

        vm.IsBusy.Should().BeFalse();
        vm.ErrorText.Should().BeNull();
        vm.ProgressText.Should().Be("Preview complete.");
        vm.ChangesHeading.Should().Be("3 changes would be made (1 blocked)");
        vm.Changes.Should().HaveCount(3);
        vm.Changes[0].Page.Should().Be("week:01:overview");
        vm.Changes[0].IsInked.Should().BeTrue("the legend holds the inked objective 2");
        vm.Changes[0].StatusText.Should().Be("Inked");
        vm.Changes[1].Blocked.Should().BeTrue();
        vm.Changes[1].StatusText.Should().Be("Blocked: a user outline sits in the slot");
        vm.Changes[1].OldText.Should().Be("Old");
        vm.Changes[1].KindText.Should().Be("Update");
        vm.Changes[2].StatusText.Should().Be("Inked");
        vm.SetupLines.Should().Contain("Objective 2: Hire two PMs (inked)").And.Contain("Metric 3: Deep work").And.Contain("Source: Work (ics link)").And.Contain("Time blocks: 6 to 22");
        vm.InkNotice.Should().Contain("Inked").And.Contain("Write values that came from ink");
        vm.Summary!.Headline.Should().Be("Preview only: nothing was written.");
        vm.Summary.PagesTouched.Should().Be(2);
        vm.Summary.EventsWritten.Should().Be(5);
        vm.Summary.Skipped.Should().Be(1);
        vm.Summary.FailedSourcesText.Should().Be("Sources that failed: Holidays");
        vm.Summary.SidecarRebuilt.Should().BeTrue();
        vm.Summary.HasWarnings.Should().BeTrue();
        vm.Summary.ElapsedText.Should().Be("3 s");

        // Fix round 4 cliapp-5: the reload after a run is a detached task (PendingReload), so counting the listings right
        // after the run proved nothing while the pool had not run a reload's listing yet. A dry run starts no reload at
        // all, so the pending reload is still the one the load started, and once it has settled one listing was made.
        vm.PendingReload.Should().BeSameAs(loadedReload, "a dry run starts no reload");
        await _h.Settle(vm.PendingReload);
        _h.Registry.Queries.Should().HaveCount(1, "a dry run does not reload the list");
    }

    [UiFact]
    public async Task Each_run_is_dated_by_the_clock_when_it_starts_not_by_the_day_the_app_opened()
    {
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() };
        RefreshViewModel vm = await Loaded();

        await vm.PreviewChangesCommand.ExecuteAsync(null);
        _h.Refresher.LastRequest!.Today.Should().Be(new DateOnly(2026, 10, 20));

        _h.Now = new DateOnly(2026, 10, 21);
        await vm.RefreshNowCommand.ExecuteAsync(null);

        _h.Refresher.LastRequest!.Today.Should().Be(new DateOnly(2026, 10, 21), "the window stayed open past midnight, so yesterday is frozen now");
        _h.Env.Today.Should().Be(new DateOnly(2026, 10, 20), "the start date itself does not move");
    }

    [UiFact]
    public async Task Refresh_now_writes_and_reloads_the_list_afterwards()
    {
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() with { Values = SetupValues.Empty }, Elapsed = TimeSpan.FromSeconds(30) };
        RefreshViewModel vm = await Loaded();
        vm.WriteInkedValues = true;
        vm.IncludeCalendar = false;

        await vm.RefreshNowCommand.ExecuteAsync(null);
        await _h.Settle(vm.PendingReload);

        _h.Refresher.LastRequest!.DryRun.Should().BeFalse();
        _h.Refresher.LastRequest.WriteInkedValues.Should().BeTrue();
        _h.Refresher.LastRequest.Calendar.Should().BeFalse();
        vm.Summary!.Headline.Should().Be("Refreshed SFP Q1 2027.");
        vm.ChangesHeading.Should().Be("3 changes planned (1 blocked)");
        vm.Changes.Should().AllSatisfy(c => c.IsInked.Should().BeFalse());
        vm.InkNotice.Should().BeNull();
        vm.SetupLines.Should().Equal("Nothing is filled in on the Setup page yet.");
        vm.ProgressText.Should().Be("Refresh complete.");
        _h.Registry.Queries.Should().HaveCount(2, "a real run reloads the list so Last refreshed updates");
        vm.SelectedNotebook!.Name.Should().Be("SFP Q1 2027", "the selection is kept across the reload");
    }

    [UiFact]
    public async Task A_failed_run_shows_the_error_and_the_log_path()
    {
        _h.Refresher.Result = new RefreshResult { Success = false, ExitCode = 6, Error = "All calendar sources failed", LogPath = @"C:\logs\refresh.log" };
        RefreshViewModel vm = await Loaded();

        await vm.RefreshNowCommand.ExecuteAsync(null);

        vm.ErrorText.Should().Be(@"All calendar sources failed Log: C:\logs\refresh.log");
        vm.ProgressText.Should().Be("Refresh failed.");
        vm.Summary.Should().BeNull();
    }

    [UiFact]
    public async Task The_orchestrator_is_given_the_run_log_and_a_failure_names_that_file()
    {
        FakeRunLogFactory logs = new();
        Serilog.ILogger? givenLogger = null;
        string? givenPath = null;
        _h.Registry.Notebooks.Add(Q1);
        RefreshViewModel vm = new(
            _h.Registry,
            _h.Tasks,
            logs,
            (logger, logPath) =>
            {
                givenLogger = logger;
                givenPath = logPath;

                // As the production orchestrator does: the result reports the log path it was composed with.
                _h.Refresher.Result = new RefreshResult { Success = false, ExitCode = 5, Error = "Writing page 'Mon Jan 4' failed", LogPath = logPath };
                return _h.Refresher;
            },
            _h.Dialogs,
            _h.Ui,
            _h.Env);
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;

        await vm.RefreshNowCommand.ExecuteAsync(null);

        FakeRunLog log = logs.Started.Should().ContainSingle().Subject;
        givenLogger.Should().BeSameAs(log.Logger);
        givenPath.Should().Be(log.Path);
        vm.ErrorText.Should().Be("Writing page 'Mon Jan 4' failed Log: " + log.Path);
    }

    [UiFact]
    public async Task An_orchestrator_that_throws_is_reported_not_rethrown()
    {
        _h.Refresher.Throw = new InvalidOperationException("OneNote is busy");
        RefreshViewModel vm = await Loaded();

        await vm.PreviewChangesCommand.ExecuteAsync(null);

        vm.ErrorText.Should().StartWith("OneNote is busy Log: ");
        vm.ProgressText.Should().Be("Preview failed.");
        vm.IsBusy.Should().BeFalse();
    }

    [UiFact]
    public async Task The_daily_checkbox_registers_and_removes_the_scheduled_task_for_the_selected_notebook()
    {
        RefreshViewModel vm = await Loaded();
        vm.HorizonDays = 10;
        vm.DailyRefreshEnabled.Should().BeFalse();

        vm.DailyRefreshEnabled = true;
        _h.Tasks.Registrations.Should().Equal(("SFP Q1 2027", Harness.Exe, 10));
        _h.Tasks.Folders.Should().Equal(new[] { Harness.DefaultFolder }, "the task runs refresh with --folder set to the sidecar's folder");
        vm.ScheduleStatus.Should().Be("Scheduled: SFP Q1 2027 is refreshed every day at 05:00.");

        vm.DailyRefreshEnabled = false;
        _h.Tasks.Removals.Should().Equal("SFP Q1 2027");
        vm.ScheduleStatus.Should().Be("The daily refresh task was removed.");

        vm.SelectedNotebook = vm.Notebooks[1];
        vm.DailyRefreshEnabled = true;
        _h.Tasks.Folders.Should().Equal(Harness.DefaultFolder, @"D:\Notebooks");
    }

    [UiFact]
    public async Task A_notebook_moved_to_onedrive_is_refreshed_and_scheduled_from_the_folder_of_its_sidecar()
    {
        // After File, Share, Move Notebook the sidecar's notebook path is the OneDrive URL; the sidecar itself stays put.
        KnownNotebook moved = Q1 with { Path = "https://d.docs.live.net/0123456789abcdef/Documents/SFP Q1 2027" };
        _h.Registry.Notebooks.Add(moved);
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() };
        RefreshViewModel vm = _h.NewRefresh();
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;

        await vm.PreviewChangesCommand.ExecuteAsync(null);
        vm.DailyRefreshEnabled = true;

        _h.Refresher.LastRequest!.NotebooksFolder.Should().Be(Harness.DefaultFolder, "the sidecar lives there, not under the OneDrive URL");
        _h.Tasks.Folders.Should().Equal(Harness.DefaultFolder);
        RefreshViewModel.NotebooksFolderOf(moved).Should().Be(Harness.DefaultFolder);
        RefreshViewModel.NotebooksFolderOf(moved with { SidecarPath = "" }).Should().BeNull("no sidecar path means the default folders are searched");
    }

    [UiFact]
    public async Task The_daily_checkbox_follows_the_selected_notebook_and_reverts_when_the_service_refuses()
    {
        _h.Tasks.Registered.Add("SFP Q4 2026");
        RefreshViewModel vm = await Loaded();

        vm.SelectedNotebook = vm.Notebooks[1];
        vm.DailyRefreshEnabled.Should().BeTrue();
        _h.Tasks.Registrations.Should().BeEmpty("syncing the checkbox from the service registers nothing");

        vm.SelectedNotebook = vm.Notebooks[0];
        vm.DailyRefreshEnabled.Should().BeFalse();

        _h.Tasks.Throw = new NotSupportedException("The scheduled task service (M5.4) is not wired into the app yet.");
        vm.DailyRefreshEnabled = true;
        vm.DailyRefreshEnabled.Should().BeFalse("the checkbox returns to the real state");
        vm.ScheduleStatus.Should().Contain("M5.4");
    }

    [UiFact]
    public async Task Selecting_another_notebook_clears_the_previous_results()
    {
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() };
        RefreshViewModel vm = await Loaded();
        await vm.PreviewChangesCommand.ExecuteAsync(null);
        vm.Changes.Should().NotBeEmpty();

        vm.SelectedNotebook = vm.Notebooks[1];

        vm.Changes.Should().BeEmpty();
        vm.SetupLines.Should().BeEmpty();
        vm.Summary.Should().BeNull();
        vm.ChangesHeading.Should().BeNull();
    }

    [UiFact]
    public void Setup_value_text_rules()
    {
        SetupValues values = Values();

        SetupValueText.SlotText(values, "objective:2").Should().Be("Hire two PMs");
        SetupValueText.SlotText(values, "standingGoal:1").Should().Be("Inbox zero");
        SetupValueText.SlotText(values, "metricName:3").Should().Be("Deep work");
        SetupValueText.SlotText(values, "habit:1").Should().BeNull();
        SetupValueText.SlotText(values, "objective:9").Should().BeNull();
        SetupValueText.SlotText(values, "nonsense").Should().BeNull();
        SetupValueText.SlotText(values, "objective:x").Should().BeNull();
        SetupValueText.InkedTexts(values).Should().Equal("Hire two PMs");
        SetupValueText.IsInked(new PlannedChange(PageKey.Cover, "x", null, "", ChangeKind.Remove, false, null), SetupValueText.InkedTexts(values)).Should().BeFalse();
        SetupValueText.Lines(values).Should().StartWith(new[] { "Objective 1: Ship the connector", "Objective 2: Hire two PMs (inked)", "Standing goal 1: Inbox zero" });
    }

    [UiFact]
    public async Task Any_inked_without_slot_ids_still_warns()
    {
        SetupValues values = Values() with { InkedSlots = Array.Empty<string>() };
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() with { Values = values } };
        RefreshViewModel vm = await Loaded();

        await vm.PreviewChangesCommand.ExecuteAsync(null);

        vm.InkNotice.Should().Be("Some Setup values came from handwriting recognition; check the values read from Setup above.");
        vm.Changes.Should().AllSatisfy(c => c.IsInked.Should().BeFalse());
    }

    [UiFact]
    public async Task Cancel_is_enabled_only_while_a_run_is_on()
    {
        _h.Refresher.Result = new RefreshResult { Success = true, ExitCode = 0, Summary = Summary() };
        using RefreshViewModel vm = await Loaded();
        int notified = 0;
        vm.CancelCommand.CanExecuteChanged += (_, _) => notified++;
        vm.CancelCommand.CanExecute(null).Should().BeFalse("nothing runs");

        await vm.PreviewChangesCommand.ExecuteAsync(null);

        vm.CancelCommand.CanExecute(null).Should().BeFalse("the run is over");
        notified.Should().BeGreaterThanOrEqualTo(2, "the button is told when the run starts and when it ends");
        vm.CancelledText.Should().BeNull();
    }

    [UiFact]
    public async Task Cancel_mid_refresh_says_cancelling_until_the_current_page_is_done_then_shows_the_cancelled_line()
    {
        using ManualResetEventSlim finishing = new(false);
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.AfterCancelGate = finishing;
        _h.Refresher.ProgressToReport.AddRange(new[] { "Opening notebook SFP Q1 2027", "Planning propagation", "Writing changes", "Writing Annual Objectives" });
        _h.Refresher.ProgressAfterCancel.Add("Writing Objective 1");
        using RefreshViewModel vm = await Loaded();

        Task refreshing = vm.RefreshNowCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);
        await _h.Ui.Drain();
        vm.IsBusy.Should().BeTrue();
        vm.ProgressText.Should().Be("Writing Annual Objectives");
        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.CancelCommand.Execute(null);

        vm.ProgressText.Should().Be(RefreshViewModel.CancellingText);
        await _h.WaitUntilAsync(() => _h.Refresher.SawCancel, "the run was given the token Cancel cancels");
        await _h.Ui.Drain();
        vm.ProgressText.Should().Be("Cancelling after the current page", "the page in flight reporting does not replace the cancelling line");
        vm.IsBusy.Should().BeTrue("the run is still finishing that page");
        vm.CancelledText.Should().BeNull();

        finishing.Set();
        await refreshing;

        vm.IsBusy.Should().BeFalse();
        vm.CancelCommand.CanExecute(null).Should().BeFalse();
        vm.ProgressText.Should().Be("Refresh cancelled.");
        vm.CancelledText.Should().Be("Refresh cancelled after the current page; 2 pages were written. The next refresh continues where this one stopped.");
        vm.ErrorText.Should().BeNull("a cancel is not a failure");
        vm.Summary.Should().BeNull("the cancelled result carries no summary");
        _h.Refresher.LastRequest!.DryRun.Should().BeFalse();
    }

    [UiFact]
    public async Task A_cancelled_preview_says_nothing_was_written()
    {
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.ProgressToReport.Add("Checking the sidecar");
        using RefreshViewModel vm = await Loaded();

        Task previewing = vm.PreviewChangesCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);

        await vm.CancelAndWaitAsync();

        vm.IsBusy.Should().BeFalse("CancelAndWaitAsync completes once the run has stopped");
        vm.ProgressText.Should().Be("Preview cancelled.");
        vm.CancelledText.Should().Be("Preview cancelled; nothing was written.");
        vm.ErrorText.Should().BeNull();
        vm.CancelAndWaitAsync().IsCompleted.Should().BeTrue("with no run on it completes at once");
        await previewing;
    }

    [UiFact]
    public async Task A_run_that_fails_after_cancel_was_pressed_shows_its_error_not_the_cancelled_line()
    {
        _h.Refresher.WaitForCancel = true;
        _h.Refresher.CancelledResult = new RefreshResult
        {
            Success = false,
            ExitCode = 3,
            Error = "OneNote stopped responding or was closed during the run. UpdatePageContent failed with RPC_S_CALL_FAILED (0x800706BE).",
            LogPath = @"C:\logs\refresh.log",
        };
        using RefreshViewModel vm = await Loaded();

        Task refreshing = vm.RefreshNowCommand.ExecuteAsync(null);
        await _h.WaitUntilAsync(() => _h.Refresher.WaitingForCancel);
        vm.CancelCommand.Execute(null);
        await refreshing;

        vm.CancelledText.Should().BeNull("the run did not stop because of the cancel");
        vm.ErrorText.Should().Be(@"OneNote stopped responding or was closed during the run. UpdatePageContent failed with RPC_S_CALL_FAILED (0x800706BE). Log: C:\logs\refresh.log");
        vm.ProgressText.Should().Be("Refresh failed.");
    }

    [UiFact]
    public void The_cancelled_line_counts_the_pages_written_and_says_the_next_refresh_continues()
    {
        RefreshViewModel.CancelledTextFor(dryRun: false, pagesWritten: 0).Should().Be("Refresh cancelled before any page was written. The next refresh continues where this one stopped.");
        RefreshViewModel.CancelledTextFor(dryRun: false, pagesWritten: 1).Should().Be("Refresh cancelled after the current page; 1 page was written. The next refresh continues where this one stopped.");
        RefreshViewModel.CancelledTextFor(dryRun: false, pagesWritten: 12).Should().Be("Refresh cancelled after the current page; 12 pages were written. The next refresh continues where this one stopped.");
        RefreshViewModel.CancelledTextFor(dryRun: true, pagesWritten: 3).Should().Be("Preview cancelled; nothing was written.");
    }
}
