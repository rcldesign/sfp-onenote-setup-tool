using Sfp.App.ViewModels.Generate;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>M6.4: the phased progress bar, Cancel, the success view with Open in OneNote and Copy path, the failure view with Save log.</summary>
public class GenerateRunViewModelTests
{
    private readonly Harness _h = new();

    private GenerateRequest Request() => new(
        new Core.Options.GenerateOptions { Start = new Core.Dates.QuarterSpec(2027, 1), PdfPath = Harness.Pdf },
        TestManifest.Shipped,
        TestPaths.TemplatesDir,
        RenderOnly: false,
        RenderOutputDir: null);

    [Fact]
    public async Task A_successful_run_shows_the_summary_the_path_and_the_OneDrive_paragraph()
    {
        _h.Generator.ProgressToReport.Add(new GenerateProgress(GeneratePhase.CheckingOneNote, 0, 0, null));
        _h.Generator.ProgressToReport.Add(new GenerateProgress(GeneratePhase.WritingPages, 3, 144, "Mon Jan 4"));
        List<string> phases = new();
        using GenerateRunViewModel run = _h.NewRun();
        run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(run.PhaseText))
            {
                phases.Add(run.PhaseText);
            }
        };
        GenerateResult? completed = null;
        run.Completed += (_, r) => completed = r;

        await run.StartAsync(Request());

        phases.Should().ContainInOrder("Starting", "Checking OneNote", "Writing pages (3 of 144)", "Done");
        run.IsRunning.Should().BeFalse();
        run.ShowSuccess.Should().BeTrue();
        run.ShowFailure.Should().BeFalse();
        run.HasResult.Should().BeTrue();
        run.SummaryText.Should().Be("144 pages written to SFP Q1 2027 in 1 min 42 s.");
        run.NotebookPath.Should().Be(@"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027");
        run.ElapsedText.Should().Be("1 min 42 s");
        run.Fraction.Should().Be(1);
        run.LogPath.Should().EndWith("sfp2onenote-generate-1.log", "the run log path stands in when the result carries none");
        completed.Should().NotBeNull();
        completed!.Success.Should().BeTrue();
        GenerateRunViewModel.OneDriveInstruction.Should().Contain("File, Share, Move Notebook").And.Contain("iPad");
        _h.Ui.Posted.Should().Be(2, "every progress report crosses the UI thread");
    }

    [Fact]
    public async Task The_success_view_says_whether_the_previous_notebook_got_its_next_quarter_link()
    {
        _h.Generator.Result = _h.Generator.Result with { NextQuarterLink = "Next quarter link added to the quarterly review of SFP Q4 2026." };
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowSuccess.Should().BeTrue();
        run.NextQuarterLinkText.Should().Be("Next quarter link added to the quarterly review of SFP Q4 2026.");

        _h.Generator.Result = _h.Generator.Result with
        {
            NextQuarterLink = null,
            Warnings = new[] { "The Next quarter link was not written to the previous notebook D:\\Old\\SFP Q4 2026: its state file was not found." },
        };
        await run.StartAsync(Request());

        run.NextQuarterLinkText.Should().BeNull("the link was not written; the warning says why");
        run.WarningsText.Should().Contain("Next quarter link was not written");
    }

    [Fact]
    public async Task Progress_reports_drive_the_bar()
    {
        _h.Generator.ProgressToReport.Add(new GenerateProgress(GeneratePhase.Rendering, 36, 144, "Tue Jan 5"));
        double fractionSeen = -1;
        bool? indeterminateSeen = null;
        using GenerateRunViewModel run = _h.NewRun();
        run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(run.Detail) && run.Detail == "Tue Jan 5")
            {
                fractionSeen = run.Fraction;
                indeterminateSeen = run.IsIndeterminate;
            }
        };

        await run.StartAsync(Request());

        fractionSeen.Should().Be(0.25);
        indeterminateSeen.Should().BeFalse();
        run.Current.Should().Be(36);
        run.Total.Should().Be(144);
    }

    [Fact]
    public async Task A_failed_run_shows_phase_page_and_message_and_keeps_the_partial_notebook_path()
    {
        _h.Generator.Result = new GenerateResult
        {
            Success = false,
            ExitCode = 5,
            Phase = GeneratePhase.WritingPages,
            FailedPage = "Mon Jan 4",
            Error = "Writing pages, page Mon Jan 4: 0x80042001 invalid XML",
            OutputPath = @"D:\Notebooks\SFP Q1 2027",
            LogPath = @"C:\logs\run.log",
            Warnings = new[] { "Fallback font in use" },
        };
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.ShowSuccess.Should().BeFalse();
        run.PhaseText.Should().Be("Failed");
        run.FailureText.Should().Be("Generate failed: Writing pages, page Mon Jan 4: 0x80042001 invalid XML. The partial notebook at D:\\Notebooks\\SFP Q1 2027 is left in place for inspection.");
        run.WarningsText.Should().Be("Fallback font in use");
        run.LogPath.Should().Be(@"C:\logs\run.log");
        run.SaveLogCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task The_orchestrator_is_given_the_run_log_and_the_failure_view_shows_that_file()
    {
        FakeRunLogFactory logs = new();
        Serilog.ILogger? givenLogger = null;
        string? givenPath = null;
        GenerateResult failed = new() { Success = false, ExitCode = 5, Phase = GeneratePhase.WritingPages, Error = "0x80042001 invalid XML" };
        using GenerateRunViewModel run = new(
            logs,
            (logger, logPath) =>
            {
                givenLogger = logger;
                givenPath = logPath;

                // As the production orchestrator does: the result reports the log path it was composed with.
                _h.Generator.Result = failed with { LogPath = logPath };
                return _h.Generator;
            },
            _h.Ui,
            _h.Shell,
            _h.Dialogs);

        await run.StartAsync(Request());

        FakeRunLog log = logs.Started.Should().ContainSingle().Subject;
        givenLogger.Should().BeSameAs(log.Logger);
        givenPath.Should().Be(log.Path, "the orchestrator writes the run's own log file, not a stand-in path");
        run.Result!.LogPath.Should().Be(log.Path);
        run.LogPath.Should().Be(log.Path);
        log.Disposed.Should().BeTrue("the log is closed (flushed) before Save log copies it");

        _h.Dialogs.NextSaveFile = @"C:\Users\me\Desktop\sfp.log";
        run.SaveLogCommand.Execute(null);
        _h.Shell.Copies.Should().Equal((log.Path, @"C:\Users\me\Desktop\sfp.log"));
    }

    [Fact]
    public async Task Save_log_copies_the_run_log_to_the_chosen_path()
    {
        _h.Generator.Result = new GenerateResult { Success = false, ExitCode = 4, Phase = GeneratePhase.Rendering, Error = "boom", LogPath = @"C:\logs\run.log" };
        using GenerateRunViewModel run = _h.NewRun();
        await run.StartAsync(Request());

        _h.Dialogs.NextSaveFile = null;
        run.SaveLogCommand.Execute(null);
        _h.Shell.Copies.Should().BeEmpty();
        _h.Dialogs.LastInitialPath.Should().Be("run.log");

        _h.Dialogs.NextSaveFile = @"C:\Users\me\Desktop\sfp.log";
        run.SaveLogCommand.Execute(null);
        _h.Shell.Copies.Should().Equal((@"C:\logs\run.log", @"C:\Users\me\Desktop\sfp.log"));
        run.ActionMessage.Should().Be(@"Log saved to C:\Users\me\Desktop\sfp.log");

        _h.Shell.Throw = new IOException("disk full");
        run.SaveLogCommand.Execute(null);
        run.ActionMessage.Should().Be("Could not save the log: disk full");
    }

    [Fact]
    public async Task Cancel_stops_the_run_and_shows_the_cancelled_view()
    {
        _h.Generator.WaitForCancel = true;
        using GenerateRunViewModel run = _h.NewRun();

        Task running = run.StartAsync(Request());
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
        run.IsRunning.Should().BeTrue();
        run.CancelCommand.CanExecute(null).Should().BeTrue();
        run.CancelCommand.Execute(null);
        run.PhaseText.Should().Be("Cancelling after the current page");
        await running;

        run.ShowCancelled.Should().BeTrue();
        run.ShowSuccess.Should().BeFalse();
        run.ShowFailure.Should().BeFalse();
        run.SummaryText.Should().Be("Cancelled after 10 pages rendered and 3 written; the partial notebook was deleted.");
        run.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Once_writing_has_started_render_reports_no_longer_move_the_phase_line()
    {
        _h.Generator.ProgressToReport.AddRange(new[]
        {
            new GenerateProgress(GeneratePhase.CreatingNotebook, 144, 144, "Notes"),
            new GenerateProgress(GeneratePhase.WritingPages, 0, 143, null),
            new GenerateProgress(GeneratePhase.Rendering, 4, 143, "Mon Jan 4"),
            new GenerateProgress(GeneratePhase.WritingPages, 1, 143, "Cover"),
            new GenerateProgress(GeneratePhase.Rendering, 5, 143, "Tue Jan 5"),
            new GenerateProgress(GeneratePhase.WritingPages, 2, 143, "Setup"),
            new GenerateProgress(GeneratePhase.BuildingContents, 0, 0, null),
        });
        List<string> phases = new();
        using GenerateRunViewModel run = _h.NewRun();
        run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(run.PhaseText))
            {
                phases.Add(run.PhaseText);
            }
        };

        await run.StartAsync(Request());

        phases.Should().Equal(
            new[] { "Starting", "Creating notebook (144 of 144)", "Writing pages (0 of 143)", "Writing pages (1 of 143)", "Writing pages (2 of 143)", "Building contents", "Done" },
            "the renderers run ahead of the writer on other threads; the line follows the writer");
    }

    [Fact]
    public async Task The_cancelling_line_stays_until_the_run_has_stopped()
    {
        _h.Generator.WaitForCancel = true;
        _h.Generator.ProgressAfterCancel.Add(new GenerateProgress(GeneratePhase.WritingPages, 7, 143, "Thu Jan 7"));
        _h.Generator.ProgressAfterCancel.Add(new GenerateProgress(GeneratePhase.Cancelled, 0, 0, "Cancelled"));
        List<string> phases = new();
        using GenerateRunViewModel run = _h.NewRun();
        run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(run.PhaseText))
            {
                phases.Add(run.PhaseText);
            }
        };

        Task running = run.StartAsync(Request());
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
        run.CancelCommand.Execute(null);
        await running;

        phases.Should().Equal(new[] { "Starting", GenerateRunViewModel.CancellingText, "Cancelled" }, "the page in flight finishing does not replace the Cancelling line");
    }

    [Fact]
    public async Task The_cancelled_view_names_the_pages_reported_and_says_deleted_only_when_the_deletion_worked()
    {
        // The orchestrator's cancelled result carries no page counts; the view takes them from the progress reports.
        string warning = GenerateMessages.PartialNotebookNotDeleted(@"D:\Notebooks\SFP Q1 2027", "The process cannot access the file 'Week 01.one' because it is being used by another process.");
        _h.Generator.WaitForCancel = true;
        _h.Generator.ProgressToReport.Add(new GenerateProgress(GeneratePhase.Rendering, 12, 143, "Thu Jan 7"));
        _h.Generator.ProgressToReport.Add(new GenerateProgress(GeneratePhase.WritingPages, 5, 143, "Fri Jan 1"));
        _h.Generator.CancelledResult = new GenerateResult
        {
            Success = false,
            Cancelled = true,
            ExitCode = 0,
            Phase = GeneratePhase.Cancelled,
            NotebookName = "SFP Q1 2027",
            Warnings = new[] { warning },
        };
        using GenerateRunViewModel run = _h.NewRun();

        Task running = run.StartAsync(Request());
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
        run.CancelCommand.Execute(null);
        await running;

        run.ShowCancelled.Should().BeTrue();
        run.SummaryText.Should().Be("Cancelled after 12 pages rendered and 5 written; the partial notebook could not be deleted (see the note below).");
        run.WarningsText.Should().Be(warning, "the cancelled panel shows the deletion failure");

        _h.Generator.CancelledResult = _h.Generator.CancelledResult with { Warnings = Array.Empty<string>() };
        running = run.StartAsync(Request());
        SpinWait.SpinUntil(() => _h.Generator.Runs == 2, TimeSpan.FromSeconds(5)).Should().BeTrue();
        run.CancelCommand.Execute(null);
        await running;

        run.SummaryText.Should().Be("Cancelled after 12 pages rendered and 5 written; the partial notebook was deleted.");
        run.WarningsText.Should().BeNull();
    }

    [Fact]
    public void Cancelled_text_is_built_from_the_result()
    {
        GenerateResult cancelled = new() { Success = false, Cancelled = true, ExitCode = 0, Phase = GeneratePhase.Cancelled, NotebookName = "SFP Q1 2027" };
        string notDeleted = GenerateMessages.PartialNotebookNotDeleted(@"D:\Notebooks\SFP Q1 2027", "Access to the path is denied.");

        GenerateRunViewModel.CancelledTextFor(cancelled, 3, 1).Should().Be("Cancelled after 3 pages rendered and 1 written; the partial notebook was deleted.");
        GenerateRunViewModel.CancelledTextFor(cancelled with { Warnings = new[] { "Font 'Bookman Old Style' is not installed.", notDeleted } }, 3, 1)
            .Should().Be("Cancelled after 3 pages rendered and 1 written; the partial notebook could not be deleted (see the note below).");
        GenerateRunViewModel.CancelledTextFor(cancelled with { NotebookName = null }, 0, 0)
            .Should().Be("Cancelled before the notebook was created; nothing was written.");
        GenerateRunViewModel.CancelledTextFor(cancelled, 0, 0)
            .Should().Be("Cancelled after 0 pages rendered and 0 written; the partial notebook was deleted.", "the notebook existed, so it was there to delete");
    }

    [Fact]
    public async Task An_orchestrator_that_throws_still_yields_a_failure_view()
    {
        _h.Generator.Throw = new InvalidOperationException("COM is gone");
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.Result!.ExitCode.Should().Be(5);
        run.FailureText.Should().StartWith("Generate failed: COM is gone.");
        run.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Open_in_OneNote_and_Copy_path_use_the_shell_and_report_problems_inline()
    {
        using GenerateRunViewModel run = _h.NewRun();
        run.OpenInOneNoteCommand.CanExecute(null).Should().BeFalse("no notebook yet");
        await run.StartAsync(Request());

        run.OpenInOneNoteCommand.Execute(null);
        _h.Shell.Opened.Should().Equal(@"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027");
        run.ActionMessage.Should().Be("Opening in OneNote.");

        run.CopyPathCommand.Execute(null);
        _h.Shell.Clipboard.Should().Equal(@"C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027");
        run.ActionMessage.Should().Be("Path copied.");

        _h.Shell.Throw = new InvalidOperationException("onenote.exe not found");
        run.OpenInOneNoteCommand.Execute(null);
        run.ActionMessage.Should().Be("Could not open OneNote: onenote.exe not found");
        run.CopyPathCommand.Execute(null);
        run.ActionMessage.Should().Be("Could not copy the path: onenote.exe not found");
    }

    [Fact]
    public async Task A_second_start_while_running_is_ignored()
    {
        _h.Generator.WaitForCancel = true;
        using GenerateRunViewModel run = _h.NewRun();

        Task first = run.StartAsync(Request());
        SpinWait.SpinUntil(() => _h.Generator.Runs == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
        await run.StartAsync(Request());
        _h.Generator.Runs.Should().Be(1);
        run.CancelCommand.Execute(null);
        await first;
    }

    [Theory]
    [InlineData(GeneratePhase.CheckingOneNote, "Checking OneNote")]
    [InlineData(GeneratePhase.Rendering, "Rendering pages")]
    [InlineData(GeneratePhase.CreatingNotebook, "Creating notebook")]
    [InlineData(GeneratePhase.WritingPages, "Writing pages")]
    [InlineData(GeneratePhase.BuildingContents, "Building contents")]
    [InlineData(GeneratePhase.Finishing, "Finishing")]
    public void Phase_names_follow_the_PRD_generate_flow(GeneratePhase phase, string expected)
    {
        GenerateRunViewModel.PhaseName(phase).Should().Be(expected);
    }

    [Fact]
    public void Progress_and_elapsed_text_formats()
    {
        GenerateRunViewModel.ProgressText(new GenerateProgress(GeneratePhase.Rendering, 4, 132, null)).Should().Be("Rendering pages (4 of 132)");
        GenerateRunViewModel.ProgressText(new GenerateProgress(GeneratePhase.Finishing, 0, 0, null)).Should().Be("Finishing");
        GenerateRunViewModel.ElapsedTextFor(TimeSpan.FromSeconds(12)).Should().Be("12 s");
        GenerateRunViewModel.ElapsedTextFor(TimeSpan.FromSeconds(125)).Should().Be("2 min 5 s");
        GenerateRunViewModel.FailureTextFor(new GenerateResult { Success = false, ExitCode = 3, Phase = GeneratePhase.CheckingOneNote })
            .Should().Be("Generate failed: no details were reported.");
    }

    [Fact]
    public void Launch_commands_try_the_executable_then_the_link()
    {
        IReadOnlyList<(string FileName, string Arguments)> commands = OneNoteLaunch.Commands(@"C:\Notebooks\SFP Q1 2027\");

        commands.Should().HaveCount(2);
        commands[0].Should().Be(("onenote.exe", "\"C:\\Notebooks\\SFP Q1 2027\""));
        commands[1].Should().Be((@"onenote:///C:\Notebooks\SFP Q1 2027", ""));
        OneNoteLaunch.Link(@"C:\Notebooks\SFP Q1 2027").Should().Be(@"onenote:///C:\Notebooks\SFP Q1 2027");
    }
}
