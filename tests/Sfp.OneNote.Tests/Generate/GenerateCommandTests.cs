using System.CommandLine;
using Sfp.Cli;
using Sfp.Cli.Verbs;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>M3.9: the generate verb's argument validation, composition and output, with the orchestrator injected.</summary>
public class GenerateCommandTests
{
    private static readonly string Root = TestPaths.NewTempDir("generate-cli");
    private static readonly string Pdf = CreatePdfPlaceholder();

    private static string CreatePdfPlaceholder()
    {
        string path = Path.Combine(Root, "SFP_No_Dates.pdf");
        File.WriteAllText(path, "%PDF-1.4 placeholder");
        return path;
    }

    private static GenerateArgs Args(string start = "2027-01", string? pdf = null) => new()
    {
        Pdf = pdf ?? Pdf,
        Start = start,
        Templates = TestPaths.TemplatesDir,
        Out = Path.Combine(Root, "out"),
    };

    /// <summary>An orchestrator returning a canned result and keeping the request it was given.</summary>
    private sealed class CannedOrchestrator : IGenerateOrchestrator, IDisposable
    {
        public CannedOrchestrator(GenerateResult result)
        {
            Result = result;
        }

        public GenerateResult Result { get; }

        public GenerateRequest? Request { get; private set; }

        public List<GenerateProgress> Reports { get; } = new();

        public bool Disposed { get; private set; }

        public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
        {
            Request = request;
            progress?.Report(new GenerateProgress(GeneratePhase.CheckingOneNote, 0, 0, null));
            progress?.Report(new GenerateProgress(GeneratePhase.Rendering, 3, 132, "Mon Jan 4"));
            progress?.Report(new GenerateProgress(GeneratePhase.Done, 0, 0, null));
            return Result;
        }

        public void Dispose() => Disposed = true;
    }

    private static (int ExitCode, string Out, string Err, CannedOrchestrator Orchestrator, GenerateRequest? Request, string? LogPath) RunWith(GenerateArgs args, GenerateResult result)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        CannedOrchestrator canned = new(result);
        GenerateRequest? seen = null;
        string? logPath = null;
        int code = GenerateCommand.Run(args, stdout, stderr, (request, _, path) =>
        {
            seen = request;
            logPath = path;
            return canned;
        }, Path.Combine(Root, "logs"));
        return (code, stdout.ToString(), stderr.ToString(), canned, seen, logPath);
    }

    [Fact]
    public void Build_defines_the_prd_command_line()
    {
        Command command = GenerateCommand.Build();

        command.Name.Should().Be("generate");
        command.Options.Select(o => o.Name).Should().BeEquivalentTo(
            "--pdf", "--start", "--name", "--out", "--date-format", "--weeks-remaining", "--dpi", "--render-only", "--manifest", "--quiet",
            "--no-links", "--hide-title", "--group-weeks", "--include-out-of-quarter-days", "--time-blocks", "--previous", "--templates");
        command.Options.Where(o => o.Required).Select(o => o.Name).Should().BeEquivalentTo("--pdf", "--start");
        ParseResult parsed = command.Parse(new[] { "--pdf", "a.pdf", "--start", "2027-01", "--dpi", "144", "--render-only", "png", "--no-links", "--time-blocks", "7-21" });
        parsed.Errors.Should().BeEmpty();
        command.Parse(new[] { "--pdf", "a.pdf" }).Errors.Should().NotBeEmpty("--start is required");
        command.Parse(new[] { "--pdf", "a.pdf", "--start", "2027-01", "--dpi", "x" }).Errors.Should().NotBeEmpty("--dpi is an integer");
    }

    [Theory]
    [InlineData("2027", "YYYY-MM")]
    [InlineData("2027-13", "between 1900-01 and 2199-12")]
    [InlineData("1899-01", "between 1900-01 and 2199-12")]
    [InlineData("Jan 2027", "YYYY-MM")]
    public void A_bad_start_month_is_refused_with_exit_2_before_anything_runs(string start, string expectedText)
    {
        StringWriter stderr = new();

        int code = GenerateCommand.Run(Args(start), new StringWriter(), stderr, (_, _, _) => throw new InvalidOperationException("must not run"));

        code.Should().Be(ExitCodes.ValidationError);
        stderr.ToString().Should().Contain(expectedText);
    }

    [Fact]
    public void A_missing_pdf_is_refused_with_exit_2()
    {
        StringWriter stderr = new();

        int code = GenerateCommand.Run(Args(pdf: Path.Combine(Root, "missing.pdf")), new StringWriter(), stderr, (_, _, _) => throw new InvalidOperationException("must not run"));

        code.Should().Be(ExitCodes.ValidationError);
        stderr.ToString().Should().Contain("PDF not found");
    }

    [Theory]
    [InlineData(150, "DPI must be")]
    public void A_dpi_outside_the_list_is_refused(int dpi, string expectedText)
    {
        StringWriter stderr = new();
        int code = GenerateCommand.Run(Args() with { Dpi = dpi }, new StringWriter(), stderr);
        code.Should().Be(ExitCodes.ValidationError);
        stderr.ToString().Should().Contain(expectedText);
    }

    [Fact]
    public void Bad_date_format_weeks_remaining_time_blocks_manifest_and_templates_are_refused()
    {
        GenerateCommand.Run(Args() with { DateFormat = "yy/mm/dd" }, new StringWriter(), new StringWriter()).Should().Be(2);
        GenerateCommand.Run(Args() with { WeeksRemaining = "printed" }, new StringWriter(), new StringWriter()).Should().Be(2);
        GenerateCommand.Run(Args() with { TimeBlocks = "22-6" }, new StringWriter(), new StringWriter()).Should().Be(2);
        GenerateCommand.Run(Args() with { TimeBlocks = "six to ten" }, new StringWriter(), new StringWriter()).Should().Be(2);
        GenerateCommand.Run(Args() with { Manifest = Path.Combine(Root, "nope.json") }, new StringWriter(), new StringWriter()).Should().Be(2);
        StringWriter stderr = new();
        GenerateCommand.Run(Args() with { Templates = Path.Combine(Root, "no-templates") }, new StringWriter(), stderr).Should().Be(2);
        stderr.ToString().Should().Contain("Templates folder");
    }

    [Fact]
    public void Options_are_built_from_the_arguments()
    {
        GenerateArgs args = Args() with
        {
            Name = " My Quarter ",
            DateFormat = "dd/mm/yy",
            WeeksRemaining = "as-printed",
            Dpi = 300,
            NoLinks = true,
            HideTitle = true,
            GroupWeeks = true,
            IncludeOutOfQuarterDays = true,
            TimeBlocks = "7-21",
            Previous = Path.Combine(Root, "prev"),
            Quiet = true,
        };

        GenerateCommand.TryBuildOptions(args, new StringWriter(), out GenerateOptions? options, out string templates).Should().BeTrue();

        templates.Should().Be(Path.GetFullPath(TestPaths.TemplatesDir));
        options!.Start.Should().Be(new Sfp.Core.Dates.QuarterSpec(2027, 1));
        options.PdfPath.Should().Be(Pdf);
        options.NotebookName.Should().Be("My Quarter");
        options.OutputFolder.Should().Be(Path.Combine(Root, "out"));
        options.DateOrder.Should().Be(DateFieldOrder.DayMonthYear);
        options.PadDateFields.Should().BeTrue();
        options.WeeksRemaining.Should().Be(WeeksRemainingMode.AsPrinted);
        options.Dpi.Should().Be(300);
        options.Links.Should().BeFalse();
        options.HideTitle.Should().BeTrue();
        options.GroupWeeks.Should().BeTrue();
        options.IncludeOutOfQuarterDays.Should().BeTrue();
        options.TimeBlocks.Should().Be(new TimeBlockWindow(7, 21));
        options.PreviousNotebook.Should().Be(Path.Combine(Root, "prev"));
        options.Quiet.Should().BeTrue();

        GenerateCommand.TryBuildOptions(Args() with { DateFormat = "m/d/yy", Out = null }, new StringWriter(), out GenerateOptions? plain, out _).Should().BeTrue();
        plain!.PadDateFields.Should().BeFalse();
        plain.DateOrder.Should().Be(DateFieldOrder.MonthDayYear);
        plain.OutputFolder.Should().Be(OutputFolders.Default());
        plain.NotebookName.Should().BeNull();
        plain.Links.Should().BeTrue();
    }

    [Fact]
    public void A_render_only_run_goes_through_the_injected_orchestrator_and_prints_the_summary()
    {
        string png = Path.Combine(Root, "png");
        GenerateArgs args = Args() with { RenderOnly = png, Dpi = 144 };
        GenerateResult canned = new()
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = png,
            NotebookName = "SFP Q1 2027",
            PagesRendered = 143,
            Elapsed = TimeSpan.FromSeconds(52),
            OutputBytes = 40_000_000,
            Warnings = new[] { "Font 'Bookman Old Style' is not installed; using the bundled 'TeX Gyre Bonum' instead." },
        };

        (int code, string stdout, string stderr, CannedOrchestrator orchestrator, GenerateRequest? request, string? logPath) = RunWith(args, canned);

        code.Should().Be(0);
        stderr.Should().BeEmpty();
        request.Should().NotBeNull();
        request!.RenderOnly.Should().BeTrue();
        request.RenderOutputDir.Should().Be(png);
        request.Options.Dpi.Should().Be(144);
        request.Manifest.TemplateId.Should().Be("sfp-quarterly-2023");
        request.TemplatesDir.Should().Be(Path.GetFullPath(TestPaths.TemplatesDir));
        orchestrator.Request.Should().BeSameAs(request);
        orchestrator.Disposed.Should().BeTrue();
        logPath.Should().StartWith(Path.Combine(Root, "logs")).And.EndWith("-generate.log");
        File.Exists(logPath).Should().BeTrue();
        stdout.Should().Contain("Rendering pages (3 of 132): Mon Jan 4");
        stdout.Should().Contain("Rendered 143 pages to " + png + " in 0:52; pages.json written.");
        stdout.Should().Contain("Warning: Font 'Bookman Old Style'");
        stdout.Should().NotContain("Move Notebook", "render-only needs no OneDrive instruction");
    }

    [Fact]
    public void A_successful_notebook_run_prints_the_summary_and_the_onedrive_move_instruction()
    {
        string notebook = Path.Combine(Root, "out", "SFP Q1 2027");
        GenerateResult canned = new()
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = notebook,
            NotebookName = "SFP Q1 2027",
            PagesRendered = 143,
            PagesWritten = 144,
            Elapsed = TimeSpan.FromSeconds(151),
            OutputBytes = 49_600_000,
            SidecarPath = notebook + ".sfp-state.json",
        };

        (int code, string stdout, string stderr, _, GenerateRequest? request, _) = RunWith(Args(), canned);

        code.Should().Be(0);
        stderr.Should().BeEmpty();
        request!.RenderOnly.Should().BeFalse();
        stdout.Should().Contain("Generated SFP Q1 2027: 144 pages written in 2:31.");
        stdout.Should().Contain("Notebook folder: " + notebook + " (47.3 MB)");
        stdout.Should().Contain("State file: " + notebook + ".sfp-state.json");
        stdout.Should().Contain(GenerateText.OneDriveMoveInstruction);
    }

    [Fact]
    public void A_failed_run_prints_the_failure_block_to_stderr_and_returns_its_exit_code()
    {
        string notebook = Path.Combine(Root, "out", "SFP Q1 2027");
        GenerateResult canned = new()
        {
            Success = false,
            ExitCode = 5,
            Phase = GeneratePhase.WritingPages,
            OutputPath = notebook,
            NotebookName = "SFP Q1 2027",
            FailedPage = "Mon Jan 4",
            Error = "Writing pages, page Mon Jan 4: OneNote rejected the XML (0x80042001)",
        };

        (int code, string stdout, string stderr, _, _, _) = RunWith(Args(), canned);

        code.Should().Be(5);
        stderr.Should().Contain("Generate failed during Writing pages.");
        stderr.Should().Contain("Page: Mon Jan 4");
        stderr.Should().Contain("Error: Writing pages, page Mon Jan 4: OneNote rejected the XML (0x80042001)");
        stderr.Should().Contain("The partial notebook at " + notebook + " was left in place for inspection.");
        stdout.Should().NotContain("Generated");
    }

    [Fact]
    public void A_cancelled_run_says_so_and_returns_zero()
    {
        GenerateResult canned = new() { Success = false, Cancelled = true, ExitCode = 0, Phase = GeneratePhase.Cancelled, Error = "Cancelled" };

        (int code, string stdout, string stderr, _, _, _) = RunWith(Args(), canned);

        code.Should().Be(0);
        stderr.Should().BeEmpty();
        stdout.Should().Contain("Cancelled. The partial notebook was deleted.");
    }

    [Fact]
    public void Quiet_suppresses_progress_and_the_summary_but_not_errors()
    {
        GenerateResult ok = new() { Success = true, ExitCode = 0, Phase = GeneratePhase.Done, OutputPath = "x", NotebookName = "n" };
        (_, string stdout, _, _, _, _) = RunWith(Args() with { Quiet = true }, ok);
        stdout.Should().BeEmpty();

        GenerateResult failed = new() { Success = false, ExitCode = 3, Phase = GeneratePhase.CheckingOneNote, Error = GenerateText.SignInMessage };
        (int code, _, string stderr, _, _, _) = RunWith(Args() with { Quiet = true }, failed);
        code.Should().Be(3);
        stderr.Should().Contain(GenerateText.SignInMessage);
    }

    [Fact]
    public void A_user_manifest_is_merged_over_the_shipped_one()
    {
        string userManifest = Path.Combine(Root, "user.json");
        File.WriteAllText(userManifest, "{ \"onenote\": { \"titleOffsetPt\": 48 } }");
        GenerateResult canned = new() { Success = true, ExitCode = 0, Phase = GeneratePhase.Done, OutputPath = "x", NotebookName = "n" };

        (int code, _, string stderr, _, GenerateRequest? request, _) = RunWith(Args() with { Manifest = userManifest }, canned);

        code.Should().Be(0, stderr);
        request!.Manifest.OneNote.TitleOffsetPt.Should().Be(48);
        request.Options.UserManifestPath.Should().Be(userManifest);

        File.WriteAllText(userManifest, "{ \"onenote\": { \"titleOffsetPtt\": 48 } }");
        (code, _, stderr, _, _, _) = RunWith(Args() with { Manifest = userManifest }, canned);
        code.Should().Be(2);
        stderr.Should().Contain("titleOffsetPtt");
    }

    [Fact]
    public void Parsers_accept_the_documented_spellings()
    {
        GenerateCommand.TryParseDateFormat("MM/DD/YY", out DateFieldOrder order, out bool padded).Should().BeTrue();
        (order, padded).Should().Be((DateFieldOrder.MonthDayYear, true));
        GenerateCommand.TryParseDateFormat("dd/mm/yy", out order, out padded).Should().BeTrue();
        (order, padded).Should().Be((DateFieldOrder.DayMonthYear, true));
        GenerateCommand.TryParseDateFormat("m/d/yy", out order, out padded).Should().BeTrue();
        (order, padded).Should().Be((DateFieldOrder.MonthDayYear, false));
        GenerateCommand.TryParseDateFormat("d/m/yy", out _, out _).Should().BeFalse();

        GenerateCommand.TryParseWeeksRemaining("as-printed", out WeeksRemainingMode mode).Should().BeTrue();
        mode.Should().Be(WeeksRemainingMode.AsPrinted);
        GenerateCommand.TryParseWeeksRemaining("Corrected", out mode).Should().BeTrue();
        mode.Should().Be(WeeksRemainingMode.Corrected);

        GenerateCommand.TryParseTimeBlocks("6-22", out TimeBlockWindow window).Should().BeTrue();
        window.Should().Be(new TimeBlockWindow(6, 22));
        GenerateCommand.TryParseTimeBlocks("0-24", out window).Should().BeTrue();
        window.RowCount.Should().Be(24);
        GenerateCommand.TryParseTimeBlocks("8-8", out _).Should().BeFalse();
        GenerateCommand.TryParseTimeBlocks("6-25", out _).Should().BeFalse();
        GenerateCommand.TryParseTimeBlocks("", out _).Should().BeFalse();
    }

    [Fact]
    public void The_production_composition_runs_on_this_machine_and_reports_what_is_missing()
    {
        // No stubs are injected: the real services are composed. Off Windows the preflight reports OneNote as
        // unavailable (exit 3); render-only skips OneNote and fails in validation because the placeholder PDF is not the planner (exit 2).
        StringWriter stdout = new();
        StringWriter stderr = new();

        int notebookCode = GenerateCommand.Run(Args(), stdout, stderr, null, Path.Combine(Root, "logs"));
        int renderCode = GenerateCommand.Run(Args() with { RenderOnly = Path.Combine(Root, "png2") }, stdout, stderr, null, Path.Combine(Root, "logs"));

        if (OperatingSystem.IsWindows())
        {
            notebookCode.Should().BeOneOf(2, 3);
        }
        else
        {
            notebookCode.Should().Be(3);
            stderr.ToString().Should().Contain("OneNote desktop");
        }

        renderCode.Should().Be(2);
        stderr.ToString().Should().Contain("Could not read PDF");
    }
}
