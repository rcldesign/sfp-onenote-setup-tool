using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Services;
using Sfp.Refresh;

namespace Sfp.App.ViewModels.Refresh;

/// <summary>
/// Creates the Refresh orchestrator for one run, writing to that run's log. <paramref name="logPath"/> is the run log's
/// file, which the orchestrator reports as <see cref="RefreshResult.LogPath"/> and the tab shows with a failure.
/// </summary>
/// <param name="logger">The run log's logger.</param>
/// <param name="logPath">The run log's file path.</param>
public delegate IRefreshOrchestrator RefreshOrchestratorFactory(ILogger logger, string logPath);

/// <summary>
/// The Refresh tab: the notebooks the tool built (from their sidecars under the known output folders plus the default
/// folder), when each was last refreshed, Preview changes (a dry run listing every planned change with blocked ones and
/// inked values marked), Refresh now, Cancel while a run is on, the daily 05:00 scheduled task checkbox, and the run
/// summary.
/// </summary>
public sealed partial class RefreshViewModel : ObservableObject, IDisposable
{
    /// <summary>The progress line from the moment Cancel is pressed until the run has stopped.</summary>
    public const string CancellingText = "Cancelling after the current page";

    /// <summary>The title of the question asked when the window is closed during a run.</summary>
    public const string CloseWhileRunningTitle = "Refresh is running";

    /// <summary>The question asked when the window is closed during a run; yes cancels the run and closes.</summary>
    public const string CloseWhileRunningQuestion =
        "A refresh is running. Cancel it and close the window? The run stops after the current page, and the next refresh continues where it stopped.";

    /// <summary>Exit code reported when the orchestrator throws instead of returning a result.</summary>
    private const int UnexpectedErrorExitCode = 5;

    /// <summary>The error of the orchestrator's cancelled result (exit 5, no summary), as RefreshOrchestratorTests pin it.</summary>
    private const string CancelledError = "Cancelled.";

    /// <summary>
    /// The orchestrator's progress line that opens the write phase; each page it then writes is reported as "Writing {title}"
    /// (RefreshOrchestratorTests count pages by the same two lines).
    /// </summary>
    private const string WritePhaseLine = "Writing changes";

    private const string WritingPagePrefix = "Writing ";

    private readonly INotebookRegistry _registry;
    private readonly IScheduledTaskService _tasks;
    private readonly IRunLogFactory _logs;
    private readonly RefreshOrchestratorFactory _orchestrators;
    private readonly IDialogService _dialogs;
    private readonly IUiThread _ui;
    private readonly AppEnvironment _env;
    private IReadOnlyList<string> _knownFolders = Array.Empty<string>();
    private bool _syncingSchedule;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The run in progress, or the last one (completed); a window close awaits it after cancelling. It ends once the
    /// result is shown: the list reload a successful Refresh now starts is <see cref="PendingReload"/>, not part of it.
    /// </summary>
    private Task _running = Task.CompletedTask;

    /// <summary>Cancel was pressed: the progress line keeps saying so until the run stops.</summary>
    private bool _cancelRequested;

    /// <summary>Pages the run reported writing, for the cancelled line (the orchestrator's cancelled result carries no summary).</summary>
    private int _pagesWrittenSeen;

    /// <summary>
    /// A reload is rebuilding the list: the selection changes it makes, and the null a WPF list pushes into the selection
    /// while its items are cleared, leave the shown results alone; the reload itself decides once the list is rebuilt.
    /// </summary>
    private bool _reloading;

    /// <summary>
    /// The list reloads started so far. Each reload takes the next number as it starts (the UI thread starts every reload,
    /// so a plain counter orders them), and a listing that returns after a newer reload started is dropped.
    /// </summary>
    private int _reloadsStarted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshNowCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private KnownNotebookItem? _selectedNotebook;

    [ObservableProperty]
    private bool _includeCalendar = true;

    [ObservableProperty]
    private int _horizonDays = 14;

    [ObservableProperty]
    private bool _writeInkedValues;

    [ObservableProperty]
    private bool _dailyRefreshEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadNotebooksCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelectNotebook))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>After a cancelled run: that it stopped after the current page, the pages written and that the next run continues.</summary>
    [ObservableProperty]
    private string? _cancelledText;

    [ObservableProperty]
    private string? _notebooksStatus;

    [ObservableProperty]
    private string? _scheduleStatus;

    [ObservableProperty]
    private string? _inkNotice;

    /// <summary>The one line shown when the run's log could not be started anywhere, so the run has no log file; else null.</summary>
    [ObservableProperty]
    private string? _logNotice;

    [ObservableProperty]
    private string? _changesHeading;

    [ObservableProperty]
    private RefreshRunSummary? _summary;

    public RefreshViewModel(
        INotebookRegistry registry,
        IScheduledTaskService tasks,
        IRunLogFactory logs,
        RefreshOrchestratorFactory orchestrators,
        IDialogService dialogs,
        IUiThread ui,
        AppEnvironment env)
    {
        _registry = registry;
        _tasks = tasks;
        _logs = logs;
        _orchestrators = orchestrators;
        _dialogs = dialogs;
        _ui = ui;
        _env = env;
        PendingReload = Task.CompletedTask;
    }

    public ObservableCollection<KnownNotebookItem> Notebooks { get; } = new();

    public ObservableCollection<PlannedChangeItem> Changes { get; } = new();

    /// <summary>The values read from Setup, one line each, after a run.</summary>
    public ObservableCollection<string> SetupLines { get; } = new();

    public bool HasSelection => SelectedNotebook is not null;

    /// <summary>The notebook list is enabled while no run is on, so the selection cannot move under a run.</summary>
    public bool CanSelectNotebook => !IsBusy;

    /// <summary>The window is closing and waits for runs to stop (<see cref="BeginClosing"/>); no run or reload starts.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>The folders the notebook list scans: the known output folders plus the default folder, each once.</summary>
    public IReadOnlyList<string> Folders => _knownFolders
        .Append(_env.DefaultOutputFolder)
        .Where(f => !string.IsNullOrWhiteSpace(f))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The last reload started (by <see cref="SetKnownFolders"/> or after a successful Refresh now), for tests. A window
    /// close never waits for it: listing a folder on an unreachable drive can take as long as the network timeout.
    /// </summary>
    public Task PendingReload { get; private set; }

    /// <summary>Sets the known output folders (from settings or after a Generate) and reloads the list.</summary>
    public void SetKnownFolders(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _knownFolders = folders.ToList();
        OnPropertyChanged(nameof(Folders));
        PendingReload = ReloadAsync();
    }

    /// <summary>
    /// Lists the notebooks again, keeping the selected notebook (the same sidecar file) when it still exists. The selection
    /// kept is the one the list has when it is rebuilt, after the folders were listed, so a notebook clicked while they
    /// were listed stays selected. The rows are new records, since the registry reads each sidecar again (after a run,
    /// with the new Last refreshed), so the shown results (summary, Setup values, changes) are cleared, and the daily
    /// checkbox read again, only when the selection ends up on another notebook than the one selected at the rebuild.
    /// Reloads can overlap (Refresh now is enabled while the reload after a run lists the folders, and a Generate sets the
    /// known folders): a listing that returns after a newer reload started is dropped, so the list always shows the
    /// newest listing, whichever reload ends last.
    /// </summary>
    public async Task ReloadAsync()
    {
        int reload = ++_reloadsStarted;
        IReadOnlyList<string> folders = Folders;
        IReadOnlyList<KnownNotebook> found;
        string? status;
        try
        {
            found = await Task.Run(() => _registry.List(folders));
            status = found.Count == 0
                ? "No notebooks built by this tool were found under " + string.Join("; ", folders)
                : null;
        }
        catch (Exception ex)
        {
            // A registry that is not wired or a folder that cannot be read is a line, not a crash.
            found = Array.Empty<KnownNotebook>();
            status = "Could not list notebooks: " + ex.Message;
        }

        if (reload != _reloadsStarted)
        {
            // A newer reload started while this one listed the folders, so its listing is the newer one (a later Last
            // refreshed after another Refresh now, the folder a Generate just added): this one changes nothing.
            return;
        }

        NotebooksStatus = status;

        // Taken now, not before the listing: the list is enabled while the folders are listed, and a notebook clicked
        // meanwhile has already cleared the results and synced the daily checkbox to its own task. Without a click this is
        // still the notebook selected before, after a run the run's notebook, whose results the reload keeps.
        KnownNotebookItem? previous = SelectedNotebook;
        _reloading = true;
        try
        {
            Notebooks.Clear();
            foreach (KnownNotebook notebook in found.OrderByDescending(n => n.Quarter.Year).ThenByDescending(n => n.Quarter.Month).ThenBy(n => n.Name, StringComparer.Ordinal))
            {
                Notebooks.Add(new KnownNotebookItem(notebook));
            }

            SelectedNotebook = (previous is null ? null : Notebooks.FirstOrDefault(n => IsSameNotebook(n, previous))) ?? Notebooks.FirstOrDefault();
        }
        finally
        {
            _reloading = false;
        }

        if (!IsSameNotebook(SelectedNotebook, previous))
        {
            // The first load, or the notebook selected at the rebuild is gone: the results on screen and the daily checkbox
            // were not about this one.
            ClearResults();
            SyncScheduleFromService(SelectedNotebook);
        }
    }

    /// <summary>The same notebook: the same sidecar file, or the same name when a sidecar path is unknown. Two nulls are the same.</summary>
    public static bool IsSameNotebook(KnownNotebookItem? a, KnownNotebookItem? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        string sidecarA = a.Notebook.SidecarPath;
        string sidecarB = b.Notebook.SidecarPath;
        return !string.IsNullOrWhiteSpace(sidecarA) && !string.IsNullOrWhiteSpace(sidecarB)
            ? string.Equals(sidecarA, sidecarB, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a.Name, b.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The folder that holds a notebook's sidecar: what the orchestrator's NotebooksFolder and the verb's --folder mean.
    /// It comes from the sidecar's own path, never from the notebook path, which turns into a OneDrive URL
    /// (https://d.docs.live.net/...) once the notebook is moved while the sidecar stays in the local output folder.
    /// Null when the sidecar path is unknown (the orchestrator then searches the default folders).
    /// </summary>
    public static string? NotebooksFolderOf(KnownNotebook notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        string? folder = string.IsNullOrWhiteSpace(notebook.SidecarPath) ? null : PathText.ParentOf(notebook.SidecarPath);
        return string.IsNullOrEmpty(folder) ? null : folder;
    }

    /// <summary>The request for the selected notebook, dated by the clock at the moment it is built (never the app's start date).</summary>
    public RefreshRequest? ToRequest(bool dryRun) => SelectedNotebook is { } notebook ? RequestFor(notebook, dryRun) : null;

    /// <summary>
    /// Runs a dry run or a real refresh of the selected notebook and fills the listing and the summary. The notebook is
    /// captured when the run starts: the request and the summary's headline are about it, whatever is selected later. A
    /// call while a run is on, or while the window is closing, does nothing. The task completes once the result is shown;
    /// a successful Refresh now then reloads the list on its own (<see cref="PendingReload"/>).
    /// </summary>
    public Task RunAsync(bool dryRun)
    {
        if (SelectedNotebook is not { } notebook || IsBusy || IsClosing)
        {
            return Task.CompletedTask;
        }

        _running = RunCoreAsync(RequestFor(notebook, dryRun), dryRun, notebook);
        return _running;
    }

    /// <summary>
    /// The window's close flow asks this first, and asking changes nothing: true when no run is on or the user agreed to
    /// cancel the one in progress (one Yes/No question). <see cref="CancelAndWaitAsync"/> then stops it.
    /// </summary>
    public bool ConfirmClose() => !IsBusy || _dialogs.Confirm(CloseWhileRunningTitle, CloseWhileRunningQuestion);

    /// <summary>
    /// The window is closing and waits for runs to stop: Preview changes, Refresh now and Reload stay disabled from now
    /// on, so no run starts that the close would then cut off.
    /// </summary>
    public void BeginClosing()
    {
        if (IsClosing)
        {
            return;
        }

        IsClosing = true;
        OnPropertyChanged(nameof(IsClosing));
        PreviewChangesCommand.NotifyCanExecuteChanged();
        RefreshNowCommand.NotifyCanExecuteChanged();
        ReloadNotebooksCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels the run in progress and completes once it has stopped and the cancelled line is shown: the orchestrator
    /// stops after the current page and saves the sidecar with what it wrote before it returns. Completes at once when
    /// no run is on, also while the list reload after a run still lists the folders (that reload only reads).
    /// </summary>
    public Task CancelAndWaitAsync()
    {
        if (IsBusy)
        {
            Cancel();
        }

        return _running;
    }

    /// <summary>
    /// The cancelled run's line. The orchestrator's cancelled result carries no summary, so the pages come from the run's
    /// "Writing {title}" progress lines. It stops after the page in flight and saves the sidecar with what it wrote, so
    /// the next refresh continues where this one stopped. A preview writes nothing.
    /// </summary>
    /// <param name="dryRun">The run was Preview changes.</param>
    /// <param name="pagesWritten">Pages the run reported writing before it stopped.</param>
    public static string CancelledTextFor(bool dryRun, int pagesWritten)
    {
        if (dryRun)
        {
            return "Preview cancelled; nothing was written.";
        }

        string stopped = pagesWritten <= 0
            ? "Refresh cancelled before any page was written."
            : string.Format(
                CultureInfo.InvariantCulture,
                "Refresh cancelled after the current page; {0} {1} written.",
                pagesWritten,
                pagesWritten == 1 ? "page was" : "pages were");
        return stopped + " The next refresh continues where this one stopped.";
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private RefreshRequest RequestFor(KnownNotebookItem notebook, bool dryRun) => new(
        notebook.Name,
        NotebooksFolderOf(notebook.Notebook),
        IncludeCalendar,
        Math.Max(0, HorizonDays),
        dryRun,
        WriteInkedValues,
        _env.CurrentDate());

    private async Task RunCoreAsync(RefreshRequest request, bool dryRun, KnownNotebookItem notebook)
    {
        // Before the run counts as busy, and never throwing: a log folder that cannot be created must not leave the tab
        // busy for good. It falls back to the temp folder, then to no log file with a one-line notice.
        using IRunLog log = _logs.StartOrFallBack("refresh", out string? logNotice);
        LogNotice = logNotice;
        IsBusy = true;
        _cancelRequested = false;
        _pagesWrittenSeen = 0;
        ErrorText = null;
        CancelledText = null;
        InkNotice = null;
        ChangesHeading = null;
        Summary = null;
        Changes.Clear();
        SetupLines.Clear();
        ProgressText = dryRun ? "Previewing changes..." : "Refreshing...";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        RefreshResult result;
        try
        {
            IRefreshOrchestrator orchestrator = _orchestrators(log.Logger, log.Path);
            UiProgress<string> progress = new(_ui, OnProgress);
            result = await Task.Run(() => orchestrator.Run(request, progress, token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The orchestrator contract reports failures in the result; this is the safety net.
            log.Logger.Error(ex, "Refresh failed with an unexpected exception");
            result = new RefreshResult { Success = false, ExitCode = UnexpectedErrorExitCode, Error = ex.Message };
        }
        finally
        {
            IsBusy = false;
        }

        // Only the orchestrator's own cancelled result counts as a cancel: a run that failed or finished after Cancel was
        // pressed (the page in flight hit an error, or Cancel came after the last page) is reported as what it was.
        bool cancelled = _cancelRequested && !result.Success && string.Equals(result.Error, CancelledError, StringComparison.Ordinal);
        Apply(result, dryRun, result.LogPath ?? log.Path, cancelled, notebook);
        if (!dryRun && result.Success && !IsClosing)
        {
            // The list shows the new Last refreshed. The reload is its own task, not part of the run a window close waits
            // for: listing a folder on an unreachable network drive can hang until the network timeout. A closing window
            // lists nothing.
            PendingReload = ReloadAsync();
        }
    }

    partial void OnSelectedNotebookChanged(KnownNotebookItem? value)
    {
        if (_reloading)
        {
            return;
        }

        ClearResults();
        SyncScheduleFromService(value);
    }

    /// <summary>Clears what the last run showed: it was about the notebook selected then.</summary>
    private void ClearResults()
    {
        Changes.Clear();
        SetupLines.Clear();
        Summary = null;
        ErrorText = null;
        CancelledText = null;
        InkNotice = null;
        ChangesHeading = null;
        ScheduleStatus = null;
    }

    partial void OnDailyRefreshEnabledChanged(bool value)
    {
        if (_syncingSchedule || SelectedNotebook is not { } notebook)
        {
            return;
        }

        try
        {
            if (value)
            {
                // --folder lets the 05:00 run find the sidecar of a notebook generated outside Documents\OneNote Notebooks.
                _tasks.Register(notebook.Name, _env.RefreshExecutablePath, Math.Max(0, HorizonDays), NotebooksFolderOf(notebook.Notebook));
                ScheduleStatus = string.Format(CultureInfo.InvariantCulture, "Scheduled: {0} is refreshed every day at 05:00.", notebook.Name);
            }
            else
            {
                _tasks.Remove(notebook.Name);
                ScheduleStatus = "The daily refresh task was removed.";
            }
        }
        catch (Exception ex)
        {
            // Task Scheduler refusals are shown and the checkbox returns to the real state.
            ScheduleStatus = "Could not update the scheduled task: " + ex.Message;
            _syncingSchedule = true;
            try
            {
                DailyRefreshEnabled = !value;
            }
            finally
            {
                _syncingSchedule = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadNotebooksAsync() => ReloadAsync();

    private bool CanReload => !IsBusy && !IsClosing;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PreviewChangesAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshNowAsync() => RunAsync(dryRun: false);

    private bool CanRun => HasSelection && !IsBusy && !IsClosing;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cancelRequested = true;
        ProgressText = CancellingText;
        _cts?.Cancel();
    }

    private void OnProgress(string text)
    {
        // Pages are counted after Cancel too: the page in flight when it was pressed is still written.
        if (text.StartsWith(WritingPagePrefix, StringComparison.Ordinal) && !string.Equals(text, WritePhaseLine, StringComparison.Ordinal))
        {
            _pagesWrittenSeen++;
        }

        if (!_cancelRequested)
        {
            ProgressText = text;
        }
    }

    private void SyncScheduleFromService(KnownNotebookItem? notebook)
    {
        bool registered = false;
        if (notebook is not null)
        {
            try
            {
                registered = _tasks.IsRegistered(notebook.Name);
            }
            catch (Exception ex)
            {
                ScheduleStatus = "Could not read the scheduled task: " + ex.Message;
            }
        }

        _syncingSchedule = true;
        try
        {
            DailyRefreshEnabled = registered;
        }
        finally
        {
            _syncingSchedule = false;
        }
    }

    private void Apply(RefreshResult result, bool dryRun, string logPath, bool cancelled, KnownNotebookItem notebook)
    {
        RefreshSummary? summary = result.Summary;
        if (summary is not null)
        {
            foreach (string line in SetupValueText.Lines(summary.Values))
            {
                SetupLines.Add(line);
            }

            IReadOnlySet<string> inkedTexts = SetupValueText.InkedTexts(summary.Values);
            foreach (PlannedChange change in summary.Changes)
            {
                Changes.Add(new PlannedChangeItem(change, SetupValueText.IsInked(change, inkedTexts)));
            }

            if (summary.Values.AnyInked)
            {
                InkNotice = inkedTexts.Count > 0
                    ? "Values marked Inked came from handwriting recognition; check them before they spread to every page."
                      + (WriteInkedValues ? "" : " They are written only when \"Write values that came from ink\" is ticked.")
                    : "Some Setup values came from handwriting recognition; check the values read from Setup above.";
            }

            ChangesHeading = string.Format(
                CultureInfo.InvariantCulture,
                dryRun ? "{0} changes would be made ({1} blocked)" : "{0} changes planned ({1} blocked)",
                summary.Changes.Count,
                summary.Changes.Count(c => c.Blocked));
            string headline = dryRun
                ? "Preview only: nothing was written."
                : string.Format(CultureInfo.InvariantCulture, "Refreshed {0}.", notebook.Name);
            Summary = new RefreshRunSummary(
                headline,
                summary.PagesTouched,
                summary.ValuesWritten,
                summary.EventsWritten,
                summary.Skipped,
                summary.FailedSources,
                summary.Warnings,
                summary.SidecarRebuilt,
                GenerateRunViewModel.ElapsedTextFor(result.Elapsed));
        }

        if (cancelled)
        {
            CancelledText = CancelledTextFor(dryRun, _pagesWrittenSeen);
            ProgressText = dryRun ? "Preview cancelled." : "Refresh cancelled.";
            return;
        }

        if (!result.Success)
        {
            ErrorText = (result.Error ?? "Refresh failed.") + (string.IsNullOrEmpty(logPath) ? "" : " Log: " + logPath);
        }

        ProgressText = result.Success
            ? (dryRun ? "Preview complete." : "Refresh complete.")
            : (dryRun ? "Preview failed." : "Refresh failed.");
    }
}
