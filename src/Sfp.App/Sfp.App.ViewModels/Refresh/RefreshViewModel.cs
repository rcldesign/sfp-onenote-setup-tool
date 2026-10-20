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
/// inked values marked), Refresh now, the daily 05:00 scheduled task checkbox, and the run summary.
/// </summary>
public sealed partial class RefreshViewModel : ObservableObject
{
    /// <summary>Exit code reported when the orchestrator throws instead of returning a result.</summary>
    private const int UnexpectedErrorExitCode = 5;

    private readonly INotebookRegistry _registry;
    private readonly IScheduledTaskService _tasks;
    private readonly IRunLogFactory _logs;
    private readonly RefreshOrchestratorFactory _orchestrators;
    private readonly IUiThread _ui;
    private readonly AppEnvironment _env;
    private IReadOnlyList<string> _knownFolders = Array.Empty<string>();
    private bool _syncingSchedule;

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
    private bool _isBusy;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _notebooksStatus;

    [ObservableProperty]
    private string? _scheduleStatus;

    [ObservableProperty]
    private string? _inkNotice;

    [ObservableProperty]
    private string? _changesHeading;

    [ObservableProperty]
    private RefreshRunSummary? _summary;

    public RefreshViewModel(
        INotebookRegistry registry,
        IScheduledTaskService tasks,
        IRunLogFactory logs,
        RefreshOrchestratorFactory orchestrators,
        IUiThread ui,
        AppEnvironment env)
    {
        _registry = registry;
        _tasks = tasks;
        _logs = logs;
        _orchestrators = orchestrators;
        _ui = ui;
        _env = env;
        PendingReload = Task.CompletedTask;
    }

    public ObservableCollection<KnownNotebookItem> Notebooks { get; } = new();

    public ObservableCollection<PlannedChangeItem> Changes { get; } = new();

    /// <summary>The values read from Setup, one line each, after a run.</summary>
    public ObservableCollection<string> SetupLines { get; } = new();

    public bool HasSelection => SelectedNotebook is not null;

    /// <summary>The folders the notebook list scans: the known output folders plus the default folder, each once.</summary>
    public IReadOnlyList<string> Folders => _knownFolders
        .Append(_env.DefaultOutputFolder)
        .Where(f => !string.IsNullOrWhiteSpace(f))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The last reload started, for tests.</summary>
    public Task PendingReload { get; private set; }

    /// <summary>Sets the known output folders (from settings or after a Generate) and reloads the list.</summary>
    public void SetKnownFolders(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _knownFolders = folders.ToList();
        OnPropertyChanged(nameof(Folders));
        PendingReload = ReloadAsync();
    }

    /// <summary>Lists the notebooks again, keeping the selection by name when it still exists.</summary>
    public async Task ReloadAsync()
    {
        string? selectedName = SelectedNotebook?.Name;
        IReadOnlyList<string> folders = Folders;
        IReadOnlyList<KnownNotebook> found;
        try
        {
            found = await Task.Run(() => _registry.List(folders));
            NotebooksStatus = found.Count == 0
                ? "No notebooks built by this tool were found under " + string.Join("; ", folders)
                : null;
        }
        catch (Exception ex)
        {
            // A registry that is not wired or a folder that cannot be read is a line, not a crash.
            found = Array.Empty<KnownNotebook>();
            NotebooksStatus = "Could not list notebooks: " + ex.Message;
        }

        Notebooks.Clear();
        foreach (KnownNotebook notebook in found.OrderByDescending(n => n.Quarter.Year).ThenByDescending(n => n.Quarter.Month).ThenBy(n => n.Name, StringComparer.Ordinal))
        {
            Notebooks.Add(new KnownNotebookItem(notebook));
        }

        SelectedNotebook = Notebooks.FirstOrDefault(n => n.Name == selectedName) ?? Notebooks.FirstOrDefault();
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
    public RefreshRequest? ToRequest(bool dryRun)
    {
        if (SelectedNotebook is not { } notebook)
        {
            return null;
        }

        return new RefreshRequest(
            notebook.Name,
            NotebooksFolderOf(notebook.Notebook),
            IncludeCalendar,
            Math.Max(0, HorizonDays),
            dryRun,
            WriteInkedValues,
            _env.CurrentDate());
    }

    /// <summary>Runs a dry run or a real refresh and fills the listing and the summary.</summary>
    public async Task RunAsync(bool dryRun)
    {
        RefreshRequest? request = ToRequest(dryRun);
        if (request is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        InkNotice = null;
        ChangesHeading = null;
        Summary = null;
        Changes.Clear();
        SetupLines.Clear();
        ProgressText = dryRun ? "Previewing changes..." : "Refreshing...";
        using IRunLog log = _logs.Start("refresh");
        RefreshResult result;
        try
        {
            IRefreshOrchestrator orchestrator = _orchestrators(log.Logger, log.Path);
            UiProgress<string> progress = new(_ui, text => ProgressText = text);
            result = await Task.Run(() => orchestrator.Run(request, progress, CancellationToken.None), CancellationToken.None);
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

        Apply(result, dryRun, result.LogPath ?? log.Path);
        if (!dryRun && result.Success)
        {
            await ReloadAsync();
        }
    }

    partial void OnSelectedNotebookChanged(KnownNotebookItem? value)
    {
        Changes.Clear();
        SetupLines.Clear();
        Summary = null;
        ErrorText = null;
        InkNotice = null;
        ChangesHeading = null;
        ScheduleStatus = null;
        SyncScheduleFromService(value);
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

    private bool CanReload => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PreviewChangesAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshNowAsync() => RunAsync(dryRun: false);

    private bool CanRun => HasSelection && !IsBusy;

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

    private void Apply(RefreshResult result, bool dryRun, string logPath)
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
                : string.Format(CultureInfo.InvariantCulture, "Refreshed {0}.", SelectedNotebook?.Name ?? "the notebook");
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

        if (!result.Success)
        {
            ErrorText = (result.Error ?? "Refresh failed.") + " Log: " + logPath;
        }

        ProgressText = result.Success
            ? (dryRun ? "Preview complete." : "Refresh complete.")
            : (dryRun ? "Preview failed." : "Refresh failed.");
    }
}
