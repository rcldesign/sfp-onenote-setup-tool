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

    private readonly ISettingsStore _settings;
    private readonly List<string> _knownFolders = new();
    private string? _lastSavedJson;
    private bool _initialized;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>A startup note (settings file reset, save failure), or null.</summary>
    [ObservableProperty]
    private string? _notice;

    public MainViewModel(GenerateViewModel generate, RefreshViewModel refresh, ISettingsStore settings)
    {
        Generate = generate;
        Refresh = refresh;
        _settings = settings;
    }

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
    /// until both tabs are idle. Completes at once when nothing runs.
    /// </summary>
    public async Task<bool> PrepareToCloseAsync()
    {
        if (!Refresh.ConfirmClose() || !Generate.ConfirmClose())
        {
            return false;
        }

        Task stopped = StopRunsAsync();
        if (!stopped.IsCompleted)
        {
            Generate.BeginClosing();
            Refresh.BeginClosing();
        }

        await stopped;
        return true;
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
