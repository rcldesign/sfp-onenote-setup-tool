using CommunityToolkit.Mvvm.ComponentModel;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;

namespace Sfp.App.ViewModels;

/// <summary>
/// The window: a Generate tab and a Refresh tab over the same services the CLI uses. Loads the last-used values at
/// startup, saves them whenever a field changes and again on exit, and tells the Refresh tab about every output folder
/// a Generate wrote to.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string WindowTitle = "SFP to OneNote";

    /// <summary>The title of the question asked when a cancelled run does not stop while the window waits to close.</summary>
    public const string CloseAnywayTitle = "The run has not stopped";

    private readonly ISettingsStore _settings;
    private readonly IDialogService? _dialogs;
    private readonly List<string> _knownFolders = new();
    private readonly object _closeRequestsGate = new();
    private string? _lastSavedJson;
    private bool _initialized;

    /// <summary>True while a close waits for cancelled runs; close requests are counted only then.</summary>
    private bool _closeWaiting;

    /// <summary>Close requests not yet answered with the close-anyway question.</summary>
    private int _closeRequests;

    /// <summary>Completed by the next close request while the wait has none counted.</summary>
    private TaskCompletionSource? _closeRequestArmed;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>A startup note (settings file reset, save failure), or null.</summary>
    [ObservableProperty]
    private string? _notice;

    /// <param name="generate">The Generate tab.</param>
    /// <param name="refresh">The Refresh tab.</param>
    /// <param name="settings">Where the last-used values are kept.</param>
    /// <param name="dialogs">
    /// Asks whether to close anyway when a cancelled run does not stop (<see cref="CloseAnywayDelay"/>,
    /// <see cref="RequestCloseAgain"/>). Without it the close keeps waiting for the runs.
    /// </param>
    public MainViewModel(GenerateViewModel generate, RefreshViewModel refresh, ISettingsStore settings, IDialogService? dialogs = null)
    {
        Generate = generate;
        Refresh = refresh;
        _settings = settings;
        _dialogs = dialogs;
    }

    /// <summary>How long a close waits for cancelled runs before it asks whether to close anyway (30 seconds).</summary>
    public TimeSpan CloseAnywayDelay { get; init; } = TimeSpan.FromSeconds(30);

    public GenerateViewModel Generate { get; }

    public RefreshViewModel Refresh { get; }

    public string Title => WindowTitle;

    /// <summary>The output folders Generate has written to, as saved in settings.</summary>
    public IReadOnlyList<string> KnownOutputFolders => _knownFolders;

    /// <summary>Number of saves that reached the store (for tests).</summary>
    public int SaveCount { get; private set; }

    /// <summary>
    /// Loads settings into both tabs and starts saving on change. Call once after construction. Saving starts only once
    /// this has succeeded: after a failed start the exit save would write half-loaded defaults over the user's file.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        AppSettings settings;
        string? problem = null;
        try
        {
            settings = _settings is SettingsStore store ? store.Load(out problem) : _settings.Load();
        }
        catch (Exception ex)
        {
            // A store that throws (locked file, bad folder) must not stop the window from opening.
            settings = new AppSettings();
            problem = "Settings could not be loaded; defaults are used. " + ex.Message;
        }

        // SettingsStore already replaced null members; any other store gets the same treatment.
        settings = settings.WithDefaultsForNulls();
        Notice = StartupNotice(problem, settings);
        _knownFolders.Clear();
        _knownFolders.AddRange(settings.KnownOutputFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase));
        Generate.LoadFrom(settings);
        Refresh.SetKnownFolders(_knownFolders);
        _lastSavedJson = SettingsStore.Serialize(Current());
        Generate.InputChanged += OnGenerateInputChanged;
        Generate.Run.Completed += OnGenerateCompleted;
        _initialized = true;
    }

    /// <summary>Saves the current values (on exit). Returns false and sets <see cref="Notice"/> when the store refused.</summary>
    public bool SaveSettings() => Save(force: true);

    /// <summary>The settings the current fields describe.</summary>
    public AppSettings Current() => Generate.ToSettings(_knownFolders.ToList());

    /// <summary>
    /// The window's Closing handler asks this first; true when the window may close. A run on either tab is cancelled
    /// after the user agrees, and awaited before the window closes: a Generate run so its partial notebook is deleted, a
    /// Refresh run so it stops after the current page with what it wrote saved in the sidecar. The preview render is
    /// stopped before the renderer is disposed. Both questions are asked before anything is cancelled (asking changes
    /// nothing), the Refresh one first, so a No to either keeps the window open and every run going. Once the close is
    /// accepted and has to wait, both tabs are closing (<see cref="GenerateViewModel.BeginClosing"/>,
    /// <see cref="RefreshViewModel.BeginClosing"/>): nothing new starts, and the runs are checked again after the wait
    /// until both tabs are idle. A cancelled run that never returns (a OneNote call that hangs) would keep the window
    /// open for good, so on a second close request (<see cref="RequestCloseAgain"/>) or once
    /// <see cref="CloseAnywayDelay"/> has passed the user is asked whether to close anyway; yes closes now, leaving the
    /// run cut off, and no keeps waiting (the delay asks once, every later close request asks again). Completes at once
    /// when nothing runs.
    /// </summary>
    public async Task<bool> PrepareToCloseAsync()
    {
        if (!Refresh.ConfirmClose() || !Generate.ConfirmClose())
        {
            return false;
        }

        Task stopped = StopRunsAsync();
        if (stopped.IsCompleted)
        {
            await stopped;
            return true;
        }

        Generate.BeginClosing();
        Refresh.BeginClosing();
        return await WaitForRunsAsync(stopped);
    }

    /// <summary>The window's close button was pressed again while the close waits for runs: ask whether to close anyway.</summary>
    public void RequestCloseAgain()
    {
        TaskCompletionSource? wake;
        lock (_closeRequestsGate)
        {
            if (!_closeWaiting)
            {
                return;
            }

            _closeRequests++;
            wake = _closeRequestArmed;
        }

        wake?.TrySetResult();
    }

    /// <summary>
    /// The close-anyway question, naming what is left behind: the partial notebook of a Generate run and a refresh cut
    /// off in the middle of a page.
    /// </summary>
    /// <param name="generateRunning">A cancelled Generate run has not stopped.</param>
    /// <param name="partialNotebook">The notebook folder that run creates, or null when it is not known.</param>
    /// <param name="refreshRunning">A cancelled refresh has not stopped.</param>
    public static string CloseAnywayQuestion(bool generateRunning, string? partialNotebook, bool refreshRunning)
    {
        string text = (generateRunning, refreshRunning) switch
        {
            (true, true) => "The cancelled Generate run and refresh have not stopped yet.",
            (true, false) => "The cancelled Generate run has not stopped yet.",
            _ => "The cancelled refresh has not stopped yet.",
        };
        text += " Close the window anyway?";
        if (generateRunning)
        {
            text += string.IsNullOrEmpty(partialNotebook)
                ? " The partial notebook is left behind; delete it before you generate that notebook again."
                : " The partial notebook at " + partialNotebook + " is left behind; delete it before you generate that notebook again.";
        }

        if (refreshRunning)
        {
            text += " The refresh is cut off in the middle of a page; run Refresh again afterwards.";
        }

        return text;
    }

    /// <summary>
    /// Whether the window may close once <paramref name="prepare"/> (a completed <see cref="PrepareToCloseAsync"/>) has
    /// finished, and the notice to show. A close preparation that failed must neither crash the app nor keep the window
    /// open for good, so it allows the close and <paramref name="notice"/> says what went wrong (the fault is observed).
    /// </summary>
    public static bool MayClose(Task<bool> prepare, out string? notice)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (!prepare.IsCompleted)
        {
            throw new InvalidOperationException("The close preparation has not finished yet.");
        }

        if (prepare.IsCompletedSuccessfully)
        {
            notice = null;
            return prepare.Result;
        }

        string reason = prepare.Exception?.GetBaseException().Message ?? "it was cancelled";
        notice = "The runs could not be stopped cleanly before closing: " + reason;
        return true;
    }

    public void Dispose()
    {
        Generate.InputChanged -= OnGenerateInputChanged;
        Generate.Run.Completed -= OnGenerateCompleted;
        Generate.Dispose();
        Refresh.Dispose();
    }

    /// <summary>
    /// The startup notice: why the defaults were loaded, that the file comes from a newer version, and where the store
    /// keeps the file before its first save replaces it. Null when there is nothing to say.
    /// </summary>
    private string? StartupNotice(string? problem, AppSettings settings)
    {
        List<string> parts = new();
        if (!string.IsNullOrEmpty(problem))
        {
            parts.Add(problem);
        }

        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            parts.Add($"Settings file {_settings.Path} was written by a newer version of SFP to OneNote; the settings this version knows are used.");
        }

        if (_settings is SettingsStore { BackupPending: true } store)
        {
            parts.Add($"The file is copied to {store.BackupPath} before it is overwritten.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>
    /// Waits for <paramref name="stopped"/>; true once it completed, or earlier when the user agreed to close anyway after
    /// a close request or <see cref="CloseAnywayDelay"/>.
    /// </summary>
    private async Task<bool> WaitForRunsAsync(Task stopped)
    {
        using CancellationTokenSource waitEnds = new();
        lock (_closeRequestsGate)
        {
            _closeWaiting = true;
            _closeRequests = 0;
        }

        try
        {
            Task request = NextCloseRequestAsync();
            Task delay = Task.Delay(_dialogs is null ? Timeout.InfiniteTimeSpan : CloseAnywayDelay, waitEnds.Token);
            while (true)
            {
                Task woke = await Task.WhenAny(stopped, request, delay);
                if (stopped.IsCompleted)
                {
                    break;
                }

                if (woke == delay)
                {
                    // The delay asks once; after a no, only another close request asks again.
                    delay = Task.Delay(Timeout.InfiniteTimeSpan, waitEnds.Token);
                }
                else
                {
                    request = NextCloseRequestAsync();
                }

                if (ConfirmCloseAnyway())
                {
                    return true;
                }
            }
        }
        finally
        {
            lock (_closeRequestsGate)
            {
                _closeWaiting = false;
                _closeRequests = 0;
                _closeRequestArmed = null;
            }

            waitEnds.Cancel();
        }

        await stopped;
        return true;
    }

    /// <summary>Completes on the next close request, taking one counted request if there is one.</summary>
    private async Task NextCloseRequestAsync()
    {
        while (true)
        {
            TaskCompletionSource armed;
            lock (_closeRequestsGate)
            {
                if (_closeRequests > 0)
                {
                    _closeRequests--;
                    return;
                }

                armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeRequestArmed = armed;
            }

            await armed.Task;
        }
    }

    /// <summary>Asks whether to close anyway while a run still has not stopped; false without a dialog service or once both are idle.</summary>
    private bool ConfirmCloseAnyway()
    {
        bool generating = Generate.Run.IsRunning;
        bool refreshing = Refresh.IsBusy;
        if (_dialogs is null || (!generating && !refreshing))
        {
            return false;
        }

        return _dialogs.Confirm(CloseAnywayTitle, CloseAnywayQuestion(generating, Generate.Run.RunningNotebookPath, refreshing));
    }

    /// <summary>Cancels both tabs' runs and waits for them, then looks again, until neither tab runs anything.</summary>
    private async Task StopRunsAsync()
    {
        do
        {
            Task generateStopped = Generate.StopForCloseAsync();
            Task refreshStopped = Refresh.CancelAndWaitAsync();
            await generateStopped;
            await refreshStopped;
        }
        while (Generate.Run.IsRunning || Refresh.IsBusy);
    }

    private void OnGenerateInputChanged(object? sender, EventArgs e) => Save(force: false);

    private void OnGenerateCompleted(object? sender, GenerateResult result)
    {
        if (!result.Success || string.IsNullOrEmpty(result.OutputPath))
        {
            return;
        }

        string? folder = PathText.ParentOf(result.OutputPath);
        if (string.IsNullOrEmpty(folder) || _knownFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _knownFolders.Add(folder);
        OnPropertyChanged(nameof(KnownOutputFolders));
        Refresh.SetKnownFolders(_knownFolders);
        Save(force: false);
    }

    private bool Save(bool force)
    {
        if (!_initialized)
        {
            return false;
        }

        try
        {
            // Building and serializing the settings is inside the try too: a save runs from field setters and the Browse
            // commands, where an exception would end the app.
            AppSettings current = Current();
            string json = SettingsStore.Serialize(current);
            if (!force && json == _lastSavedJson)
            {
                return true;
            }

            _settings.Save(current);
            _lastSavedJson = json;
            SaveCount++;
            return true;
        }
        catch (Exception ex)
        {
            Notice = "Settings could not be saved to " + _settings.Path + ": " + ex.Message;
            return false;
        }
    }
}
