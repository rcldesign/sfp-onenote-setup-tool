using Sfp.App.ViewModels.Generate;
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
            Error = "0x80042001 invalid XML",
            OutputPath = @"D:\Notebooks\SFP Q1 2027",
            LogPath = @"C:\logs\run.log",
            Warnings = new[] { "Fallback font in use" },
        };
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.ShowSuccess.Should().BeFalse();
        run.PhaseText.Should().Be("Failed");
        run.FailureText.Should().Be("Failed during writing pages, page Mon Jan 4: 0x80042001 invalid XML. The partial notebook at D:\\Notebooks\\SFP Q1 2027 is left in place for inspection.");
        run.WarningsText.Should().Be("Fallback font in use");
        run.LogPath.Should().Be(@"C:\logs\run.log");
        run.SaveLogCommand.CanExecute(null).Should().BeTrue();
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
    public async Task An_orchestrator_that_throws_still_yields_a_failure_view()
    {
        _h.Generator.Throw = new InvalidOperationException("COM is gone");
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.Result!.ExitCode.Should().Be(5);
        run.FailureText.Should().StartWith("Failed during failed: COM is gone.");
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
            .Should().Be("Failed during checking onenote: no details were reported.");
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
