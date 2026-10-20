using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;

namespace Sfp.App.ViewModels.Generate;

/// <summary>
/// Creates the orchestrator for one run, writing to that run's log. <paramref name="logPath"/> is the run log's file,
/// which the orchestrator reports as <see cref="GenerateResult.LogPath"/> (shown and copied by Save log) and beside
/// which it saves rejected page XML.
/// </summary>
/// <param name="logger">The run log's logger.</param>
/// <param name="logPath">The run log's file path.</param>
public delegate IGenerateOrchestrator GenerateOrchestratorFactory(ILogger logger, string logPath);

/// <summary>
/// The Generate flow below the fields: the phased progress bar with Cancel while a run is on, then the success view
/// (pages written, notebook path, elapsed, Open in OneNote, the OneDrive move paragraph with Copy path) or the failure
/// view (phase, page, HRESULT or message, Save log). The orchestrator runs on a background thread; progress is
/// marshaled to the UI thread through <see cref="IUiThread"/>.
/// </summary>
public sealed partial class GenerateRunViewModel : ObservableObject, IDisposable
{
    /// <summary>The PRD's one-paragraph instruction for moving the notebook to OneDrive.</summary>
    public const string OneDriveInstruction =
        "To use the notebook on the iPad, move it to OneDrive from inside OneNote: open the notebook, then choose File, Share, "
        + "Move Notebook and pick your OneDrive. OneNote desktop cannot create a notebook directly in OneDrive and refuses local "
        + "notebooks placed inside the OneDrive sync folder, which is why the notebook was created at the path above. After the move, "
        + "open OneNote on the iPad and the notebook appears under your account.";

    /// <summary>The phase line from the moment Cancel is pressed until the run has stopped.</summary>
    public const string CancellingText = "Cancelling after the current page";

    /// <summary>Exit code reported when the orchestrator throws instead of returning a result (the contract says it never does).</summary>
    private const int UnexpectedErrorExitCode = 5;

    private readonly IRunLogFactory _logs;
    private readonly GenerateOrchestratorFactory _orchestrators;
    private readonly IUiThread _ui;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    /// <summary>The run has reached its write phase: render reports from the workers ahead of the writer no longer drive the bar.</summary>
    private bool _writingStarted;

    /// <summary>Cancel was pressed: the phase line keeps saying so until the run stops.</summary>
    private bool _cancelRequested;

    /// <summary>The highest page counts the run reported, for the cancelled summary (the orchestrator's cancelled result carries none).</summary>
    private int _pagesRenderedSeen;

    private int _pagesWrittenSeen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _phaseText = "";

    [ObservableProperty]
    private int _current;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private double _fraction;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private GenerateResult? _result;

    [ObservableProperty]
    private bool _showSuccess;

    [ObservableProperty]
    private bool _showFailure;

    [ObservableProperty]
    private bool _showCancelled;

    [ObservableProperty]
    private string? _summaryText;

    /// <summary>
    /// The success summary's line about the previous notebook ("Next quarter link added to the quarterly review of SFP Q4
    /// 2026."), or null when no previous notebook was given or the link was not written (a warning then says why).
    /// </summary>
    [ObservableProperty]
    private string? _nextQuarterLinkText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInOneNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private string? _notebookPath;

    [ObservableProperty]
    private string? _elapsedText;

    [ObservableProperty]
    private string? _warningsText;

    [ObservableProperty]
    private string? _failureText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLogCommand))]
    private string? _logPath;

    /// <summary>Feedback of the last button press: "Path copied", "Log saved to ...", or the error.</summary>
    [ObservableProperty]
    private string? _actionMessage;

    public GenerateRunViewModel(
        IRunLogFactory logs,
        GenerateOrchestratorFactory orchestrators,
        IUiThread ui,
        IShellService shell,
        IDialogService dialogs)
    {
        _logs = logs;
        _orchestrators = orchestrators;
        _ui = ui;
        _shell = shell;
        _dialogs = dialogs;
    }

    /// <summary>Raised on the thread the run finished on, after the views were updated.</summary>
    public event EventHandler<GenerateResult>? Completed;

    /// <summary>True while a result of any kind is shown.</summary>
    public bool HasResult => ShowSuccess || ShowFailure || ShowCancelled;

    /// <summary>The phase name for the progress bar: "Rendering pages", "Writing pages", and so on.</summary>
    public static string PhaseName(GeneratePhase phase) => phase switch
    {
        GeneratePhase.CheckingOneNote => "Checking OneNote",
        GeneratePhase.Validating => "Validating",
        GeneratePhase.Rendering => "Rendering pages",
        GeneratePhase.CreatingNotebook => "Creating notebook",
        GeneratePhase.WritingPages => "Writing pages",
        GeneratePhase.BuildingContents => "Building contents",
        GeneratePhase.Finishing => "Finishing",
        GeneratePhase.Done => "Done",
        GeneratePhase.Cancelled => "Cancelled",
        GeneratePhase.Failed => "Failed",
        _ => phase.ToString(),
    };

    /// <summary>"Rendering pages (4 of 144)" for a page phase, the phase name alone otherwise.</summary>
    public static string ProgressText(GenerateProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        string name = PhaseName(progress.Phase);
        return progress.Total > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} ({1} of {2})", name, progress.Current, progress.Total)
            : name;
    }

    /// <summary>"1 min 42 s", "12 s".</summary>
    public static string ElapsedTextFor(TimeSpan elapsed)
    {
        int minutes = (int)elapsed.TotalMinutes;
        int seconds = elapsed.Seconds;
        return minutes > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} min {1} s", minutes, seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0} s", seconds);
    }

    /// <summary>Runs the request to completion; returns when the result views are set. A second call while running does nothing.</summary>
    public async Task StartAsync(GenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsRunning)
        {
            return;
        }

        ResetViews();
        IsRunning = true;
        IsIndeterminate = true;
        PhaseText = "Starting";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        using IRunLog log = _logs.Start("generate");
        GenerateResult result;
        try
        {
            IGenerateOrchestrator orchestrator = _orchestrators(log.Logger, log.Path);
            UiProgress<GenerateProgress> progress = new(_ui, OnProgress);
            result = await Task.Run(() => orchestrator.Run(request, progress, token), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            result = new GenerateResult { Success = false, Cancelled = true, ExitCode = 0, Phase = GeneratePhase.Cancelled };
        }
        catch (Exception ex)
        {
            // The orchestrator contract says it never throws for expected failures; anything else still gets a failure view.
            log.Logger.Error(ex, "Generate failed with an unexpected exception");
            result = new GenerateResult
            {
                Success = false,
                ExitCode = UnexpectedErrorExitCode,
                Phase = GeneratePhase.Failed,
                Error = ex.Message,
            };
        }
        finally
        {
            IsRunning = false;
        }

        if (result.LogPath is null)
        {
            result = result with { LogPath = log.Path };
        }

        Apply(result);
        Completed?.Invoke(this, result);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancelRequested = true;
        PhaseText = CancellingText;
        _cts?.Cancel();
    }

    private bool HasNotebookPath => !string.IsNullOrEmpty(NotebookPath);

    [RelayCommand(CanExecute = nameof(HasNotebookPath))]
    private void OpenInOneNote()
    {
        try
        {
            _shell.OpenInOneNote(NotebookPath!);
            ActionMessage = "Opening in OneNote.";
        }
        catch (Exception ex)
        {
            // A missing onenote.exe or a refused link is reported, not thrown through the button.
            ActionMessage = "Could not open OneNote: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasNotebookPath))]
    private void CopyPath()
    {
        try
        {
            _shell.CopyToClipboard(NotebookPath!);
            ActionMessage = "Path copied.";
        }
        catch (Exception ex)
        {
            ActionMessage = "Could not copy the path: " + ex.Message;
        }
    }

    private bool HasLog => !string.IsNullOrEmpty(LogPath);

    [RelayCommand(CanExecute = nameof(HasLog))]
    private void SaveLog()
    {
        string source = LogPath!;
        string? target = _dialogs.PickSaveFile("Save the run log", "Log files (*.log)|*.log|All files (*.*)|*.*", PathText.FileNameOf(source));
        if (target is null)
        {
            return;
        }

        try
        {
            _shell.CopyFile(source, target);
            ActionMessage = "Log saved to " + target;
        }
        catch (Exception ex)
        {
            ActionMessage = "Could not save the log: " + ex.Message;
        }
    }

    private void OnProgress(GenerateProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        // Page counts for the cancelled summary, whichever report drives the bar.
        if (progress.Phase == GeneratePhase.Rendering)
        {
            _pagesRenderedSeen = Math.Max(_pagesRenderedSeen, progress.Current);
        }
        else if (progress.Phase == GeneratePhase.WritingPages)
        {
            _pagesWrittenSeen = Math.Max(_pagesWrittenSeen, progress.Current);
            _writingStarted = true;
        }

        // Pages render on other threads ahead of the writer. Once writing has started the bar follows the writer only;
        // it would otherwise flip between "Rendering pages (40 of 143)" and "Writing pages (12 of 143)".
        if (_writingStarted && progress.Phase == GeneratePhase.Rendering)
        {
            return;
        }

        if (!_cancelRequested)
        {
            PhaseText = ProgressText(progress);
        }

        Current = progress.Current;
        Total = progress.Total;
        IsIndeterminate = progress.Total <= 0;
        Fraction = progress.Total > 0 ? Math.Clamp((double)progress.Current / progress.Total, 0, 1) : 0;
        Detail = progress.Detail;
    }

    private void ResetViews()
    {
        _writingStarted = false;
        _cancelRequested = false;
        _pagesRenderedSeen = 0;
        _pagesWrittenSeen = 0;
        Result = null;
        ShowSuccess = false;
        ShowFailure = false;
        ShowCancelled = false;
        SummaryText = null;
        NextQuarterLinkText = null;
        NotebookPath = null;
        ElapsedText = null;
        WarningsText = null;
        FailureText = null;
        LogPath = null;
        ActionMessage = null;
        Current = 0;
        Total = 0;
        Fraction = 0;
        Detail = null;
        OnPropertyChanged(nameof(HasResult));
    }

    private void Apply(GenerateResult result)
    {
        Result = result;
        LogPath = result.LogPath;
        ElapsedText = ElapsedTextFor(result.Elapsed);
        WarningsText = result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : null;
        Detail = null;
        IsIndeterminate = false;
        if (result.Success)
        {
            PhaseText = PhaseName(GeneratePhase.Done);
            Fraction = 1;
            NotebookPath = result.OutputPath;
            SummaryText = string.Format(
                CultureInfo.InvariantCulture,
                "{0} pages written to {1} in {2}.",
                result.PagesWritten,
                result.NotebookName ?? result.OutputPath ?? "the notebook",
                ElapsedText);
            NextQuarterLinkText = string.IsNullOrWhiteSpace(result.NextQuarterLink) ? null : result.NextQuarterLink;
            ShowSuccess = true;
        }
        else if (result.Cancelled)
        {
            PhaseText = PhaseName(GeneratePhase.Cancelled);
            SummaryText = CancelledTextFor(
                result,
                Math.Max(result.PagesRendered, _pagesRenderedSeen),
                Math.Max(result.PagesWritten, _pagesWrittenSeen));
            ShowCancelled = true;
        }
        else
        {
            PhaseText = PhaseName(GeneratePhase.Failed);
            NotebookPath = result.OutputPath;
            FailureText = FailureTextFor(result);
            ShowFailure = true;
        }

        OnPropertyChanged(nameof(HasResult));
    }

    /// <summary>
    /// The cancelled view's line, from the result: it says the partial notebook was deleted only when the result carries
    /// no warning that the deletion failed (that warning is shown under the line), and says nothing was written when the
    /// run stopped before the notebook existed.
    /// </summary>
    /// <param name="result">The cancelled result.</param>
    /// <param name="pagesRendered">Pages rendered before the cancel (the result's count, or the progress reports').</param>
    /// <param name="pagesWritten">Pages written before the cancel.</param>
    public static string CancelledTextFor(GenerateResult result, int pagesRendered, int pagesWritten)
    {
        ArgumentNullException.ThrowIfNull(result);
        string after = string.Format(CultureInfo.InvariantCulture, "Cancelled after {0} pages rendered and {1} written", pagesRendered, pagesWritten);
        if (result.Warnings.Any(GenerateMessages.IsPartialNotebookNotDeleted))
        {
            return after + "; the partial notebook could not be deleted (see the note below).";
        }

        if (result.NotebookName is null && pagesRendered == 0 && pagesWritten == 0)
        {
            return "Cancelled before the notebook was created; nothing was written.";
        }

        return after + "; the partial notebook was deleted.";
    }

    /// <summary>"Failed while writing pages, page Mon Jan 4: 0x80042001 invalid XML. The partial notebook at ... is left in place for inspection."</summary>
    public static string FailureTextFor(GenerateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string phase = PhaseName(result.Phase);
        string text = "Failed during " + phase.ToLowerInvariant();
        if (!string.IsNullOrEmpty(result.FailedPage))
        {
            text += ", page " + result.FailedPage;
        }

        text += ": " + (result.Error ?? "no details were reported") + ".";
        if (!string.IsNullOrEmpty(result.OutputPath))
        {
            text += " The partial notebook at " + result.OutputPath + " is left in place for inspection.";
        }

        return text;
    }
}
