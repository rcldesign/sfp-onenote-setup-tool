using Sfp.App.ViewModels;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Refresh;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-7 (app half): a run whose log cannot be started falls back to a log in the temp folder, else runs
/// without a log file and shows one line; it never leaves the tab running, and a failed close preparation allows the
/// close with a notice instead of crashing.
/// </summary>
public class RunLogFallbackTests
{
    private const string FolderError = @"Could not find a part of the path 'Z:\logs'.";
    private const string Folder = Harness.DefaultFolder + @"\SFP Q1 2027";

    private readonly Harness _h = new();

    private static GenerateRequest Request() => new(
        new Core.Options.GenerateOptions { Start = new QuarterSpec(2027, 1), PdfPath = Harness.Pdf },
        TestManifest.Shipped,
        TestPaths.TemplatesDir,
        RenderOnly: false,
        RenderOutputDir: null);

    [UiFact]
    public async Task A_log_folder_that_cannot_be_used_falls_back_to_a_log_in_the_temp_folder()
    {
        FailingRunLogFactory logs = new() { FallbackWorks = true };
        string? givenPath = null;
        _h.Generator.Result = new GenerateResult { Success = false, ExitCode = 5, Phase = GeneratePhase.WritingPages, Error = "Writing pages: boom" };
        using GenerateRunViewModel run = new(logs, (_, path) => { givenPath = path; return _h.Generator; }, _h.Ui, _h.Shell, _h.Dialogs);

        await run.StartAsync(Request());

        FakeRunLog fallback = logs.Fallbacks.Should().ContainSingle().Subject;
        givenPath.Should().Be(fallback.Path, "the orchestrator writes the fallback log");
        run.IsRunning.Should().BeFalse();
        run.ShowFailure.Should().BeTrue();
        run.LogPath.Should().Be(fallback.Path);
        run.SaveLogCommand.CanExecute(null).Should().BeTrue();
        run.LogNotice.Should().BeNull("the run has a log file, only in another folder");
        fallback.Disposed.Should().BeTrue();
    }

    [UiFact]
    public async Task A_generate_whose_log_cannot_be_started_anywhere_runs_without_a_log_file_and_says_so()
    {
        using GenerateRunViewModel run = new(new FailingRunLogFactory(), (_, _) => _h.Generator, _h.Ui, _h.Shell, _h.Dialogs);

        await run.StartAsync(Request());

        _h.Generator.Runs.Should().Be(1);
        run.IsRunning.Should().BeFalse("the tab never stays running");
        run.ShowSuccess.Should().BeTrue();
        run.LogNotice.Should().Be("The run log could not be started (" + FolderError + "); this run has no log file.");
        run.SaveLogCommand.CanExecute(null).Should().BeFalse("there is no log file to save");
        run.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [UiFact]
    public async Task A_refresh_whose_log_cannot_be_started_anywhere_runs_and_names_no_log()
    {
        _h.Registry.Notebooks.Add(new KnownNotebook("SFP Q1 2027", Folder, new QuarterSpec(2027, 1), null, null, Folder + ".sfp-state.json"));
        _h.Refresher.Result = new RefreshResult { Success = false, ExitCode = 6, Error = "All calendar sources failed" };
        using RefreshViewModel vm = new(_h.Registry, _h.Tasks, new FailingRunLogFactory(), (_, _) => _h.Refresher, _h.Dialogs, _h.Ui, _h.Env);
        vm.SetKnownFolders(Array.Empty<string>());
        await vm.PendingReload;

        await vm.RefreshNowCommand.ExecuteAsync(null);

        _h.Refresher.Runs.Should().Be(1);
        vm.IsBusy.Should().BeFalse("the tab never stays busy");
        vm.RefreshNowCommand.CanExecute(null).Should().BeTrue();
        vm.LogNotice.Should().Be("The run log could not be started (" + FolderError + "); this run has no log file.");
        vm.ErrorText.Should().Be("All calendar sources failed", "there is no log to point at");
    }

    [UiFact]
    public async Task The_window_closes_normally_after_runs_without_a_log()
    {
        _h.Registry.Notebooks.Add(new KnownNotebook("SFP Q1 2027", Folder, new QuarterSpec(2027, 1), null, null, Folder + ".sfp-state.json"));
        FailingRunLogFactory logs = new();
        RefreshViewModel refresh = new(_h.Registry, _h.Tasks, logs, (_, _) => _h.Refresher, _h.Dialogs, _h.Ui, _h.Env);
        GenerateViewModel generate = new(
            _h.Manifest,
            _h.Dates,
            _h.Validator,
            _h.NewPreviews(),
            new GenerateRunViewModel(logs, (_, _) => _h.Generator, _h.Ui, _h.Shell, _h.Dialogs),
            _h.Dialogs,
            _h.Ui,
            _h.Env);
        using MainViewModel main = new(generate, refresh, _h.Settings);
        main.Initialize();
        await main.Generate.PendingPdfCheck;
        await main.Refresh.PendingReload;
        await main.Generate.Run.StartAsync(Request());
        await main.Refresh.RefreshNowCommand.ExecuteAsync(null);

        Task<bool> closing = main.PrepareToCloseAsync();

        closing.IsCompletedSuccessfully.Should().BeTrue("nothing is left running or faulted");
        MainViewModel.MayClose(closing, out string? notice).Should().BeTrue();
        notice.Should().BeNull();
    }

    [UiFact]
    public void A_close_preparation_that_failed_allows_the_close_with_a_notice()
    {
        MainViewModel.MayClose(Task.FromException<bool>(new IOException(FolderError)), out string? faulted).Should().BeTrue();
        faulted.Should().Be("The runs could not be stopped cleanly before closing: " + FolderError);

        MainViewModel.MayClose(Task.FromCanceled<bool>(new CancellationToken(canceled: true)), out string? cancelled).Should().BeTrue();
        cancelled.Should().Be("The runs could not be stopped cleanly before closing: it was cancelled");

        MainViewModel.MayClose(Task.FromResult(false), out string? kept).Should().BeFalse("the user kept the run going");
        kept.Should().BeNull();
        MainViewModel.MayClose(Task.FromResult(true), out string? closed).Should().BeTrue();
        closed.Should().BeNull();
        FluentActions.Invoking(() => MainViewModel.MayClose(new TaskCompletionSource<bool>().Task, out _)).Should().Throw<InvalidOperationException>();
    }

    /// <summary>A log folder that cannot be created; the temp-folder fallback works only when <see cref="FallbackWorks"/> is set.</summary>
    private sealed class FailingRunLogFactory : IRunLogFactory
    {
        public bool FallbackWorks { get; init; }

        public List<FakeRunLog> Fallbacks { get; } = new();

        public IRunLog Start(string verb) => throw new DirectoryNotFoundException(FolderError);

        public IRunLog StartFallback(string verb)
        {
            if (!FallbackWorks)
            {
                throw new UnauthorizedAccessException("The temp folder is read-only.");
            }

            FakeRunLog log = new(@"C:\Users\me\AppData\Local\Temp\sfp2onenote\logs\20261020-093000-" + verb + ".log");
            Fallbacks.Add(log);
            return log;
        }
    }
}
