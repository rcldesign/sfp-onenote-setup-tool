using Sfp.App.ViewModels.Generate;
using Sfp.OneNote.Generate;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-9: the failure line shows the orchestrator's error as it is behind "Generate failed: ", which already
/// names the phase and the page, never "Failed during failed", and ends the error with exactly one period.
/// </summary>
public class GenerateFailureTextTests
{
    private const string PartialNotebook = @"D:\Notebooks\SFP Q1 2027";

    private readonly Harness _h = new();

    private static GenerateRequest Request() => new(
        new Core.Options.GenerateOptions { Start = new Core.Dates.QuarterSpec(2027, 1), PdfPath = Harness.Pdf },
        TestManifest.Shipped,
        TestPaths.TemplatesDir,
        RenderOnly: false,
        RenderOutputDir: null);

    [UiFact]
    public void An_orchestrator_error_is_shown_as_it_is_with_one_period_and_no_repeated_phase_or_page()
    {
        // Shaped as GenerateOrchestrator.Fail builds it: "{phase}, page {title}: {message}", the message ending in a period.
        GenerateResult result = new()
        {
            Success = false,
            ExitCode = 5,
            Phase = GeneratePhase.WritingPages,
            FailedPage = "Mon Jan 4",
            Error = "Writing pages, page Mon Jan 4: OneNote rejected the XML for page 'Mon Jan 4' (0x80042001): The XML is invalid.",
            OutputPath = PartialNotebook,
        };

        string text = GenerateRunViewModel.FailureTextFor(result);

        text.Should().Be(
            "Generate failed: Writing pages, page Mon Jan 4: OneNote rejected the XML for page 'Mon Jan 4' (0x80042001): The XML is invalid."
            + " The partial notebook at " + PartialNotebook + " is left in place for inspection.");
        text.Should().NotContain("..");
        text.Should().NotContain("Failed during");
    }

    [UiFact]
    public void An_error_without_a_period_gets_one_and_a_missing_error_says_so()
    {
        GenerateResult result = new() { Success = false, ExitCode = 3, Phase = GeneratePhase.CheckingOneNote, Error = "OneNote is not installed" };

        GenerateRunViewModel.FailureTextFor(result).Should().Be("Generate failed: OneNote is not installed.");
        GenerateRunViewModel.FailureTextFor(result with { Error = "Ends with two periods.." }).Should().Be("Generate failed: Ends with two periods..", "only one period is trimmed");
        GenerateRunViewModel.FailureTextFor(result with { Error = null }).Should().Be("Generate failed: no details were reported.");
        GenerateRunViewModel.FailureTextFor(result with { Error = "  " }).Should().Be("Generate failed: no details were reported.");
    }

    [UiTheory]
    [InlineData("OneNote did not close it, so its folder was left in place. Close the notebook in OneNote and delete the folder before running the same command again. OneNote said: OneNote is showing a dialog.")]
    [InlineData("Access to the path 'Week 01.one' is denied.")]
    public async Task A_failure_whose_partial_notebook_could_not_be_deleted_shows_the_warning_and_no_inspection_sentence(string reason)
    {
        // Fix round 4 cliapp-2: a render failure whose notebook OneNote would not close (or whose folder could not be
        // deleted) keeps its path with a warning saying what to do; the failure line still ended "is left in place for
        // inspection", against that warning.
        string warning = Core.Options.GenerateMessages.PartialNotebookNotDeleted(PartialNotebook, reason);
        _h.Generator.Result = new GenerateResult
        {
            Success = false,
            ExitCode = 4,
            Phase = GeneratePhase.Rendering,
            FailedPage = "Cover",
            Error = "Rendering pages, page Cover: Rendering Cover page 'Cover' (PDF page 1) failed: PDFium could not decode the page. The partial notebook was left in place because OneNote did not close it; the warnings say what to do before running the same command again.",
            OutputPath = PartialNotebook,
            Warnings = new[] { warning },
        };
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.FailureText.Should().StartWith("Generate failed: Rendering pages, page Cover: ").And.NotContain("for inspection");
        run.WarningsText.Should().Be(warning, "the failure view shows the warning under the line");
        run.NotebookPath.Should().Be(PartialNotebook, "the path is kept");
    }

    [UiFact]
    public async Task An_exception_the_view_model_caught_uses_the_same_neutral_prefix()
    {
        _h.Generator.Throw = new InsufficientExecutionStackException("Insufficient stack to continue the execution of the program.");
        using GenerateRunViewModel run = _h.NewRun();

        await run.StartAsync(Request());

        run.ShowFailure.Should().BeTrue();
        run.Result!.Phase.Should().Be(GeneratePhase.Failed);
        run.FailureText.Should().Be("Generate failed: Insufficient stack to continue the execution of the program.");
    }
}
