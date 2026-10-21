using System.CommandLine;
using Serilog.Events;
using Sfp.Cli;
using Sfp.Cli.Logging;
using Sfp.Cli.Verbs;
using Sfp.Core.Options;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Writer;
using Sfp.Refresh;
using Sfp.Render.Fonts;
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
        private static readonly GenerateProgress[] DefaultReports =
        {
            new(GeneratePhase.CheckingOneNote, 0, 0, null),
            new(GeneratePhase.Rendering, 3, 132, "Mon Jan 4"),
            new(GeneratePhase.Done, 0, 0, null),
        };

        private readonly IReadOnlyList<GenerateProgress> _reports;

        public CannedOrchestrator(GenerateResult result, IReadOnlyList<GenerateProgress>? reports = null)
        {
            Result = result;
            _reports = reports ?? DefaultReports;
        }

        public GenerateResult Result { get; }

        public GenerateRequest? Request { get; private set; }

        public bool Disposed { get; private set; }

        public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
        {
            Request = request;
            foreach (GenerateProgress report in _reports)
            {
                progress?.Report(report);
            }

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

    /// <summary>Fails its preflight the way <see cref="GenerateOrchestrator"/> does: logs the error, reports the Failed phase and returns the failure.</summary>
    private sealed class FailingPreflightOrchestrator : IGenerateOrchestrator
    {
        private readonly Serilog.ILogger _log;
        private readonly string _logPath;
        private readonly string _message;

        public FailingPreflightOrchestrator(Serilog.ILogger log, string logPath, string message)
        {
            _log = log;
            _logPath = logPath;
            _message = message;
        }

        public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new GenerateProgress(GeneratePhase.CheckingOneNote, 0, 0, null));
            _log.Error("Preflight failed: {Errors}", _message);
            progress?.Report(new GenerateProgress(GeneratePhase.Failed, 0, 0, _message));
            return new GenerateResult { Success = false, ExitCode = ExitCodes.OneNoteUnavailable, Phase = GeneratePhase.CheckingOneNote, Error = _message, LogPath = _logPath };
        }
    }

    /// <summary>Runs <paramref name="run"/> with both console streams captured, where the run log's console sink writes, and returns what it wrote there.</summary>
    private static string WithConsole(Action run)
    {
        StringWriter console = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(console);
        Console.SetError(console);
        try
        {
            run();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return console.ToString();
    }

    private static int Occurrences(string text, string value) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(value)).Count;

    /// <summary>
    /// Runs a command line through <see cref="Program.Run"/> as the executable does. The verbs and the run log's console
    /// sink write to the console, so both console streams are captured; the run log goes to <paramref name="logDir"/>
    /// through SFP2ONENOTE_LOGDIR, as a user's override would send it.
    /// </summary>
    private static (int Code, string Out, string Err) RunCli(string logDir, params string[] args)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        string? originalLogDir = Environment.GetEnvironmentVariable(RunLog.LogDirEnvVar);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Environment.SetEnvironmentVariable(RunLog.LogDirEnvVar, logDir);
        try
        {
            int code = Program.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunLog.LogDirEnvVar, originalLogDir);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_start_is_refused_with_exit_2_instead_of_throwing(string start)
    {
        StringWriter stderr = new();
        int code = -1;

        Action run = () => code = GenerateCommand.Run(Args(start), new StringWriter(), stderr, (_, _, _) => throw new InvalidOperationException("must not run"));

        run.Should().NotThrow();
        code.Should().Be(ExitCodes.ValidationError);
        stderr.ToString().Should().Contain("Start month must be YYYY-MM");
    }

    [Fact]
    public void Parse_errors_exit_2_with_the_message_and_a_usage_hint()
    {
        static (int Code, string Out, string Err) Cli(params string[] args)
        {
            StringWriter output = new();
            StringWriter error = new();
            int code = Program.Run(args, output, error);
            return (code, output.ToString(), error.ToString());
        }

        (int code, string stdout, string stderr) = Cli("generate", "--pdf", "a.pdf");
        code.Should().Be(ExitCodes.ValidationError, "a missing required option is a validation error, not System.CommandLine's exit 1");
        stderr.Should().Contain("Option '--start' is required.").And.Contain("Run 'sfp2onenote generate --help' for usage.");
        stdout.Should().BeEmpty();

        Cli("generate", "--pdf", "a.pdf", "--start", "2027-01", "--dpi", "x").Code.Should().Be(ExitCodes.ValidationError, "--dpi takes a number");
        (int unknownCode, _, string unknownErr) = Cli("frobnicate");
        unknownCode.Should().Be(ExitCodes.ValidationError);
        unknownErr.Should().Contain("frobnicate").And.Contain("Run 'sfp2onenote --help' for usage.");
        Cli().Code.Should().Be(ExitCodes.ValidationError, "a verb is required");

        // A blank --start parses (it is a string) and reaches the verb, which refuses it with exit 2.
        Cli("generate", "--pdf", Pdf, "--start", "", "--templates", TestPaths.TemplatesDir).Code.Should().Be(ExitCodes.ValidationError);

        (int helpCode, string help, string helpErr) = Cli("generate", "--help");
        helpCode.Should().Be(0, "help is not an error");
        help.Should().Contain("--pdf");
        helpErr.Should().BeEmpty();
    }

    [Fact]
    public void A_value_starting_with_an_at_sign_is_taken_as_it_is_not_as_a_response_file()
    {
        // GTD-style names such as "@Work Q1 2027" are allowed, and the daily refresh task passes the name as it is. Read
        // as a response file, the value failed every run before the verb started (or spliced a file's contents in).
        string logs = Path.Combine(Root, "logs-at-sign");

        (int refreshCode, _, string refreshErr) = RunCli(logs, "refresh", "--notebook", "@Work Q1 2027", "--calendar", "--horizon", "14", "--today", "2027-01-04", "--templates", TestPaths.TemplatesDir);
        (int generateCode, _, string generateErr) = RunCli(logs, "generate", "--pdf", Pdf, "--start", "2027-01", "--name", "@Home", "--out", Path.Combine(Root, "out"), "--templates", TestPaths.TemplatesDir, "--quiet");

        foreach (string stderr in new[] { refreshErr, generateErr })
        {
            stderr.Should().NotContain("Response file").And.NotContain("for usage.", "the command line parses; only a parse error prints the usage hint");
        }

        string refreshLog = File.ReadAllText(Directory.GetFiles(logs, "*-refresh.log").Should().ContainSingle().Which);
        refreshLog.Should().Contain("refresh --notebook @Work Q1 2027 calendar true horizon 14", "the verb received the name as typed");
        if (!OperatingSystem.IsWindows())
        {
            // Off Windows both verbs get as far as OneNote, which is unavailable here: exit 3, after the arguments were accepted.
            refreshCode.Should().Be(ExitCodes.OneNoteUnavailable, refreshErr);
            generateCode.Should().Be(ExitCodes.OneNoteUnavailable, generateErr);
        }
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
    public void Generate_accepts_the_render_only_date_formats_and_finds_templates_the_same_way()
    {
        foreach ((string format, (DateFieldOrder order, bool padded)) in RenderOnlyCommand.DateFormats)
        {
            GenerateCommand.TryBuildOptions(Args() with { DateFormat = format }, new StringWriter(), out GenerateOptions? options, out _).Should().BeTrue(format);
            (options!.DateOrder, options.PadDateFields).Should().Be((order, padded), format);
        }

        GenerateCommand.TryBuildOptions(Args() with { Templates = null }, new StringWriter(), out _, out string found).Should().BeTrue();
        found.Should().Be(Path.GetFullPath(PlanCommand.FindTemplatesDir()!), "the lookup is PlanCommand.FindTemplatesDir: beside the executable, else above it");

        StringWriter stderr = new();
        string missing = Path.Combine(Root, "no-templates-here");
        GenerateCommand.TryBuildOptions(Args() with { Templates = missing }, stderr, out _, out _).Should().BeFalse();
        stderr.ToString().Should().Contain("Templates folder not found or has no sfp-quarterly.json: " + missing, "the same words as render-only and refresh");
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
            PagesRendered = 144,
            PngFiles = 142,
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
        stdout.Should().Contain("Rendered 144 pages of SFP Q1 2027: 142 PNG files and pages.json written to " + png + " in 52.0 s.", "the render-only verb's sentence: Contents and the blank parent page get no PNG");
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
    public void The_summary_says_whether_the_previous_notebook_got_its_next_quarter_link()
    {
        string notebook = Path.Combine(Root, "out", "SFP Q1 2027");
        GenerateResult linked = new()
        {
            Success = true,
            ExitCode = 0,
            Phase = GeneratePhase.Done,
            OutputPath = notebook,
            NotebookName = "SFP Q1 2027",
            PagesWritten = 144,
            SidecarPath = notebook + ".sfp-state.json",
            NextQuarterLink = "Next quarter link added to the quarterly review of SFP Q4 2026.",
        };
        GenerateResult notLinked = linked with
        {
            NextQuarterLink = null,
            Warnings = new[] { "The Next quarter link was not written to the previous notebook /old/SFP Q4 2026: its state file /old/SFP Q4 2026.sfp-state.json was not found." },
        };

        (int code, string stdout, string stderr, _, GenerateRequest? request, _) = RunWith(Args() with { Previous = Path.Combine(Root, "old", "SFP Q4 2026") }, linked);
        (int code2, string stdout2, _, _, _, _) = RunWith(Args(), notLinked);

        code.Should().Be(0);
        stderr.Should().BeEmpty();
        request!.Options.PreviousNotebook.Should().Be(Path.Combine(Root, "old", "SFP Q4 2026"));
        string[] lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        int state = Array.IndexOf(lines, "State file: " + notebook + ".sfp-state.json");
        lines[state + 1].Should().Be("Next quarter link added to the quarterly review of SFP Q4 2026.", "the link line follows the state file line");
        code2.Should().Be(0);
        stdout2.Should().Contain("Warning: The Next quarter link was not written to the previous notebook /old/SFP Q4 2026: its state file").And.NotContain("Next quarter link added");
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
        // The orchestrator names the notebook in a cancelled result once it had created it (and then deleted it).
        GenerateResult canned = new() { Success = false, Cancelled = true, ExitCode = 0, Phase = GeneratePhase.Cancelled, Error = "Cancelled", NotebookName = "SFP Q1 2027" };

        (int code, string stdout, string stderr, _, _, _) = RunWith(Args(), canned);

        code.Should().Be(0);
        stderr.Should().BeEmpty();
        stdout.Should().Contain("Cancelled. The partial notebook was deleted.");

        (_, string early, _, _, _, _) = RunWith(Args(), canned with { NotebookName = null });
        early.Should().Contain("Cancelled before the notebook was created; nothing was written.");
    }

    [Fact]
    public void A_cancelled_run_never_claims_a_deletion_that_failed()
    {
        string notebook = Path.Combine(Root, "out", "SFP Q1 2027");
        string warning = GenerateMessages.PartialNotebookNotDeleted(notebook, "The process cannot access the file because it is being used by another process.");
        GenerateResult canned = new()
        {
            Success = false,
            Cancelled = true,
            ExitCode = 0,
            Phase = GeneratePhase.Cancelled,
            Error = "Cancelled",
            NotebookName = "SFP Q1 2027",
            Warnings = new[] { "Font 'Bookman Old Style' is not installed; using the bundled 'TeX Gyre Bonum' instead.", warning },
        };

        (int code, string stdout, string stderr, _, _, _) = RunWith(Args(), canned);
        (_, string quietOut, string quietErr, _, _, _) = RunWith(Args() with { Quiet = true }, canned);

        code.Should().Be(0);
        stdout.Should().NotContain("was deleted");
        stderr.Should().Contain("Cancelled, but the partial notebook could not be deleted.").And.Contain("Warning: " + warning);
        stderr.Should().NotContain("Bookman", "only the deletion failure is reported for a cancelled run");
        quietOut.Should().BeEmpty();
        quietErr.Should().Contain(warning, "a notebook left on disk is reported even with --quiet");
    }

    [Fact]
    public void Every_progress_report_is_printed_once_in_the_order_it_arrives()
    {
        // The orchestrator reports each page once, from the render worker that finished it, so counts can arrive out of
        // order (3 before 2 here); every page still gets its line.
        GenerateProgress[] reports =
        {
            new(GeneratePhase.Rendering, 0, 0, null),
            new(GeneratePhase.Rendering, 0, 3, null),
            new(GeneratePhase.Rendering, 1, 3, "Cover"),
            new(GeneratePhase.Rendering, 3, 3, "Mon Jan 4"),
            new(GeneratePhase.Rendering, 2, 3, "Fri Jan 1"),
            new(GeneratePhase.Finishing, 0, 0, null),
        };
        GenerateResult ok = new() { Success = true, ExitCode = 0, Phase = GeneratePhase.Done, OutputPath = Path.Combine(Root, "png-order"), NotebookName = "n", PagesRendered = 3, PngFiles = 3 };
        StringWriter stdout = new();

        int code = GenerateCommand.Run(Args() with { RenderOnly = Path.Combine(Root, "png-order") }, stdout, new StringWriter(), (_, _, _) => new CannedOrchestrator(ok, reports), Path.Combine(Root, "logs"));

        code.Should().Be(0);
        List<string> progress = stdout.ToString().Split('\n').Select(l => l.TrimEnd('\r')).TakeWhile(l => !l.StartsWith("Rendered ", StringComparison.Ordinal)).ToList();
        progress.Should().HaveCount(reports.Length, "one line per report");
        progress.Where(l => l.Contains(" of 3): ", StringComparison.Ordinal)).Should().BeEquivalentTo(
            new[] { "Rendering pages (1 of 3): Cover", "Rendering pages (2 of 3): Fri Jan 1", "Rendering pages (3 of 3): Mon Jan 4" },
            "every page once, in whatever order the workers finished");
        progress.Should().StartWith(new[] { "Rendering pages", "Rendering pages (0 of 3)" }).And.EndWith("Finishing");
    }

    [Fact]
    public void The_console_shows_the_run_logs_warnings_but_not_its_information_lines()
    {
        // The verb prints its own progress and summary; the run log's console sink keeps to warnings and errors so every
        // COM call and rendered page (logged at Information) does not flood the console or repeat the progress lines.
        RunLog.ConsoleShows(LogEventLevel.Warning, quiet: false, printsOwnProgress: true).Should().BeTrue();
        RunLog.ConsoleShows(LogEventLevel.Information, quiet: false, printsOwnProgress: true).Should().BeFalse();
        RunLog.ConsoleShows(LogEventLevel.Error, quiet: false, printsOwnProgress: true).Should().BeFalse("the verb prints its failures itself");
        RunLog.ConsoleShows(LogEventLevel.Warning, quiet: true, printsOwnProgress: true).Should().BeFalse();
        RunLog.ConsoleShows(LogEventLevel.Error, quiet: true, printsOwnProgress: true).Should().BeFalse();
        RunLog.ConsoleShows(LogEventLevel.Error, quiet: true, printsOwnProgress: false).Should().BeTrue();
        RunLog.ConsoleShows(LogEventLevel.Warning, quiet: true, printsOwnProgress: false).Should().BeFalse();
        RunLog.ConsoleShows(LogEventLevel.Information, quiet: false, printsOwnProgress: false).Should().BeTrue();

        string info = "information-" + Guid.NewGuid().ToString("N");
        string warning = "warning-" + Guid.NewGuid().ToString("N");
        GenerateResult ok = new() { Success = true, ExitCode = 0, Phase = GeneratePhase.Done, OutputPath = "x", NotebookName = "n" };
        StringWriter console = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(console);
        Console.SetError(console);
        try
        {
            GenerateCommand.Run(Args(), new StringWriter(), new StringWriter(), (_, log, _) =>
            {
                log.Information("{Marker}", info);
                log.Warning("{Marker}", warning);
                return new CannedOrchestrator(ok);
            }, Path.Combine(Root, "logs"));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        console.ToString().Should().Contain(warning).And.NotContain(info);
    }

    [Fact]
    public void A_failure_the_verb_prints_itself_is_shown_once_and_without_a_stack_trace()
    {
        // The run log's console sink used to repeat the verb's own failure block, even with --quiet: generate printed the
        // OneNote message as a "Failed: ..." progress line, as "[ERR] Preflight failed: ..." and in the block; refresh
        // printed it, then "[ERR] OneNote is unavailable" and the whole stack trace. The errors now go to the log file
        // only, a failure is not a progress line, and the console never shows a stack trace.
        string message = OneNoteUnavailableException.UserMessage;
        foreach (bool quiet in new[] { false, true })
        {
            string generateLogs = Path.Combine(Root, "logs-once-generate-" + quiet);
            StringWriter generateOut = new();
            StringWriter generateErr = new();
            int generateCode = -1;
            string generateConsole = WithConsole(() => generateCode = GenerateCommand.Run(
                Args() with { Quiet = quiet }, generateOut, generateErr, (_, log, logPath) => new FailingPreflightOrchestrator(log, logPath, message), generateLogs));

            string refreshLogs = Path.Combine(Root, "logs-once-refresh-" + quiet);
            StringWriter refreshOut = new();
            StringWriter refreshErr = new();
            int refreshCode = -1;
            string refreshConsole = WithConsole(() => refreshCode = RefreshCommand.Run(
                new RefreshArgs { Notebook = "SFP Q1 2027", Today = "2027-01-04", Templates = TestPaths.TemplatesDir, Quiet = quiet },
                refreshOut,
                refreshErr,
                (_, _, _, _, _) => throw new OneNoteUnavailableException(message + " Refresh runs on Windows only."),
                refreshLogs));

            generateCode.Should().Be(ExitCodes.OneNoteUnavailable);
            refreshCode.Should().Be(ExitCodes.OneNoteUnavailable);
            string generateAll = generateConsole + generateOut + generateErr;
            string refreshAll = refreshConsole + refreshOut + refreshErr;
            Occurrences(generateAll, message).Should().Be(1, "generate shows the message once, in its failure block (quiet {0}): {1}", quiet, generateAll);
            Occurrences(refreshAll, message).Should().Be(1, "refresh shows the message once (quiet {0}): {1}", quiet, refreshAll);
            generateErr.ToString().Should().Contain("Error: " + message);
            refreshErr.ToString().Should().Contain(message);
            generateAll.Should().NotContain("   at ").And.NotContain("[ERR]");
            refreshAll.Should().NotContain("   at ").And.NotContain("[ERR]");

            // The log files keep the errors, the refresh one with its exception and stack trace.
            File.ReadAllText(Directory.GetFiles(generateLogs, "*-generate.log").Should().ContainSingle().Which).Should().Contain("[ERR] Preflight failed: " + message);
            File.ReadAllText(Directory.GetFiles(refreshLogs, "*-refresh.log").Should().ContainSingle().Which)
                .Should().Contain("[ERR] OneNote is unavailable").And.Contain(nameof(OneNoteUnavailableException)).And.Contain("   at ");
        }
    }

    [Fact]
    public void The_next_quarter_link_warning_the_verb_prints_is_logged_below_what_its_console_shows()
    {
        // Fix round 3 app-4: the warning travels in the run's warnings, which the verb prints as a "Warning: ..." line. The
        // writer logged it at Warning too, which the console sink shows for this verb, so it appeared twice; it is logged at
        // Information now (as the font fallback is), and the log file still keeps it.
        Support.CapturingSink sink = new();
        using FakeOneNoteApp app = new();
        Sfp.Core.Manifests.Manifest manifest = Support.TestManifest.Load();
        string missing = Path.Combine(TestPaths.NewTempDir("previous-missing"), "SFP Q4 2026");
        Sfp.OneNote.Writer.NextQuarterLinkWriter writer = new(app, new SidecarStore(), manifest, Xml.MonJan4.Options(manifest), sink.CreateLogger());

        Sfp.OneNote.Writer.NextQuarterLinkResult result = writer.Link(missing, new NotebookHandle("{nb:next}{1}{B0}", "/out/SFP Q1 2027", "SFP Q1 2027"), "onenote:#Cover&end");

        result.Warning.Should().NotBeNullOrEmpty("the verb prints it as a Warning line");
        LogEvent logged = sink.Events.Should().ContainSingle(e => e.MessageTemplate.Text.StartsWith("Next quarter link not written", StringComparison.Ordinal)).Subject;
        logged.Level.Should().Be(LogEventLevel.Information);
        RunLog.ConsoleShows(logged.Level, quiet: false, printsOwnProgress: true).Should().BeFalse("the verb's own Warning line is the one the user sees");
    }

    [Fact]
    public void A_log_folder_that_cannot_be_created_moves_the_log_to_the_temp_folder_and_the_run_goes_on()
    {
        // SFP2ONENOTE_LOGDIR under a file: the folder can never be created. The verbs used to stop with exit 1 and an
        // unhandled DirectoryNotFoundException, so the unattended daily refresh never ran. Now one line says so, even
        // with --quiet, and the run goes on with its log in the temp folder.
        string file = Path.Combine(Root, "not-a-folder");
        File.WriteAllText(file, "");
        string logs = Path.Combine(file, "logs");
        string[] refresh = { "refresh", "--notebook", "SFP Q1 2027", "--today", "2027-01-04", "--templates", TestPaths.TemplatesDir, "--quiet" };
        string[] generate = { "generate", "--pdf", Pdf, "--start", "2027-01", "--out", Path.Combine(Root, "out"), "--templates", TestPaths.TemplatesDir, "--quiet" };
        foreach (string[] args in new[] { refresh, generate })
        {
            (int code, _, string stderr) = RunCli(logs, args);

            stderr.Should().NotContain("Unhandled exception");
            code.Should().NotBe(1, stderr);
            string notice = stderr.Split('\n').Select(l => l.TrimEnd('\r')).Should().ContainSingle(l => l.StartsWith("Could not use the log folder ", StringComparison.Ordinal)).Which;
            notice.Should().StartWith("Could not use the log folder " + logs + ": ").And.EndWith(" This run logs to " + RunLog.FallbackDirectory + " instead.");
            if (!OperatingSystem.IsWindows())
            {
                code.Should().Be(ExitCodes.OneNoteUnavailable, "the run went on to OneNote, which is unavailable here");
            }

            if (args == generate)
            {
                stderr.Should().Contain("Log: " + RunLog.FallbackDirectory + Path.DirectorySeparatorChar, "the failure block names the log in the temp folder");
            }
        }
    }

    [SkippableFact]
    public void A_relative_log_folder_override_gives_runs_a_full_log_path()
    {
        // Fix round 3 app-5: SFP2ONENOTE_LOGDIR=rel-logs gave "Log: rel-logs/...", which means nothing once the current
        // folder changes (the app's Save log copies the file after a file dialog that may change it). The folder is now
        // resolved to a full path before any file path is built.
        string folder = Path.Combine(Root, "relative-logs");
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, folder);
        Skip.If(Path.IsPathRooted(relative), "The test folder is on another drive than the current folder.");
        string[] renderOnly = { "generate", "--pdf", Pdf, "--start", "2027-01", "--render-only", Path.Combine(Root, "png-relative-log"), "--templates", TestPaths.TemplatesDir };

        (int code, string stdout, string stderr) = RunCli(relative, renderOnly);

        code.Should().Be(ExitCodes.ValidationError, "the placeholder PDF is not the planner: {0}", stderr);
        string logLine = (stdout + stderr).Split('\n').Select(l => l.TrimEnd('\r')).Should().ContainSingle(l => l.StartsWith("Log: ", StringComparison.Ordinal)).Which;
        string logPath = logLine["Log: ".Length..];
        Path.IsPathFullyQualified(logPath).Should().BeTrue(logLine);
        logPath.Should().StartWith(folder + Path.DirectorySeparatorChar);
        File.Exists(logPath).Should().BeTrue();
    }

    [Fact]
    public void The_run_log_falls_back_to_one_folder_and_gives_up_in_one_line_when_neither_works()
    {
        string root = Path.Combine(Root, "log-fallback");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "a-file");
        File.WriteAllText(file, "");
        string underFile = Path.Combine(file, "logs");
        string fallback = Path.Combine(root, "temp-logs");
        DateTimeOffset now = new(2027, 1, 4, 9, 0, 0, TimeSpan.Zero);

        // A folder that works: no notice.
        StringWriter fineErr = new();
        using (RunLog? log = RunLog.StartWithFallback("refresh", quiet: true, fineErr, Path.Combine(root, "logs"), fallbackDirectory: fallback, now: now))
        {
            log!.Path.Should().Be(Path.Combine(root, "logs", "20270104-090000-refresh.log"));
        }

        fineErr.ToString().Should().BeEmpty();

        // A folder that cannot be created: one line naming it and the reason; the log, which keeps the reason, starts in the fallback.
        StringWriter movedErr = new();
        using (RunLog? log = RunLog.StartWithFallback("refresh", quiet: true, movedErr, underFile, fallbackDirectory: fallback, now: now))
        {
            log!.Path.Should().Be(Path.Combine(fallback, "20270104-090000-refresh.log"));
        }

        movedErr.ToString().Should().StartWith("Could not use the log folder " + underFile + ": ").And.EndWith(" This run logs to " + fallback + " instead." + Environment.NewLine);
        movedErr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        File.ReadAllText(Path.Combine(fallback, "20270104-090000-refresh.log")).Should().Contain("The log folder " + underFile + " could not be used: ");

        // A folder that exists but cannot hold the file (a folder already has its name): the file sink would log nothing
        // and the run would name a log that does not exist, so this falls back too.
        string blocked = Path.Combine(root, "blocked");
        Directory.CreateDirectory(Path.Combine(blocked, "20270104-090000-generate.log"));
        StringWriter blockedErr = new();
        using (RunLog? log = RunLog.StartWithFallback("generate", quiet: true, blockedErr, blocked, fallbackDirectory: fallback, now: now))
        {
            log!.Path.Should().Be(Path.Combine(fallback, "20270104-090000-generate.log"));
        }

        blockedErr.ToString().Should().StartWith("Could not use the log folder " + blocked + ": ");

        // Neither folder works: one line naming both with their reasons, and no log; the verb exits 2.
        string fallbackUnderFile = Path.Combine(file, "temp");
        StringWriter failedErr = new();
        RunLog? none = RunLog.StartWithFallback("refresh", quiet: true, failedErr, underFile, fallbackDirectory: fallbackUnderFile, now: now);
        none.Should().BeNull();
        failedErr.ToString().Should().StartWith("Could not start the run log in " + underFile + ": ").And.Contain(" Nor in " + fallbackUnderFile + ": ");
        failedErr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
    }

    [SkippableFact]
    public void Without_onenote_each_verb_prints_the_message_once_and_no_stack_trace()
    {
        // The executable's own path, through Program.Run with the production composition: off Windows OneNote is
        // unavailable, which is the PRD message, printed once, with or without --quiet.
        Skip.If(OperatingSystem.IsWindows(), "On Windows the production composition starts OneNote.");
        string logs = Path.Combine(Root, "logs-no-onenote");
        string[] refresh = { "refresh", "--notebook", "SFP Q1 2027", "--today", "2027-01-04", "--templates", TestPaths.TemplatesDir };
        string[] generate = { "generate", "--pdf", Pdf, "--start", "2027-01", "--out", Path.Combine(Root, "out"), "--templates", TestPaths.TemplatesDir };
        foreach (string[] verb in new[] { refresh, generate })
        {
            foreach (string[] args in new[] { verb, verb.Append("--quiet").ToArray() })
            {
                (int code, string stdout, string stderr) = RunCli(logs, args);

                string shown = stdout + stderr;
                code.Should().Be(ExitCodes.OneNoteUnavailable, shown);
                Occurrences(shown, OneNoteUnavailableException.UserMessage).Should().Be(1, "{0} shows the OneNote message once: {1}", string.Join(' ', args), shown);
                stderr.Should().Contain(OneNoteUnavailableException.UserMessage);
                shown.Should().NotContain("   at ", "a stack trace belongs in the log file");
            }
        }
    }

    [Fact]
    public void The_fallback_font_warning_reaches_the_user_once()
    {
        // The production composition: without Bookman Old Style (always the case on Linux) the font service falls back
        // and warns. The warning travels in the run's warnings, printed with the result; the log must not echo it too.
        StringWriter console = new();
        StringWriter stdout = new();
        StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(console);
        Console.SetError(console);
        try
        {
            GenerateCommand.Run(Args() with { RenderOnly = Path.Combine(Root, "png-font") }, stdout, stderr, null, Path.Combine(Root, "logs"));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        int shown = System.Text.RegularExpressions.Regex.Matches(console.ToString() + stdout + stderr, "is not installed; using the bundled").Count;
        if (OperatingSystem.IsLinux())
        {
            shown.Should().Be(1, "this build has no Bookman Old Style, so the fallback warning shows, once");
        }
        else
        {
            shown.Should().BeLessThanOrEqualTo(1);
        }
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
        GenerateCommand.TryParseDateFormat(" D/M/YY ", out order, out padded).Should().BeTrue("generate accepts every format render-only and the app accept");
        (order, padded).Should().Be((DateFieldOrder.DayMonthYear, false));
        GenerateCommand.TryParseDateFormat("yy/mm/dd", out _, out _).Should().BeFalse();
        GenerateCommand.TryParseDateFormat(null, out _, out _).Should().BeFalse();

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
    public void The_production_orchestrator_reports_the_log_path_it_was_composed_with()
    {
        // The app composes this orchestrator with its run log's path (finding 2); the failure view shows and Save log
        // copies whatever path the result reports, so it must be the one passed in.
        string png = Path.Combine(Root, "png-logpath");
        string logPath = Path.Combine(Root, "logs", "20261020-093000-generate.log");
        GenerateCommand.TryBuildOptions(Args() with { RenderOnly = png }, new StringWriter(), out GenerateOptions? options, out string templates).Should().BeTrue();
        GenerateRequest request = new(options!, TestManifest.Shipped, templates, RenderOnly: true, RenderOutputDir: png);

        IGenerateOrchestrator orchestrator = GenerateCommand.CreateProductionOrchestrator(request, Serilog.Core.Logger.None, logPath);
        GenerateResult result;
        try
        {
            result = orchestrator.Run(request, null, CancellationToken.None);
        }
        finally
        {
            (orchestrator as IDisposable)?.Dispose();
        }

        result.ExitCode.Should().Be(ExitCodes.ValidationError, "the placeholder PDF is not the planner");
        result.LogPath.Should().Be(logPath);
    }

    [Fact]
    public void The_refresh_composition_loads_fonts_before_it_opens_onenote()
    {
        // RefreshCommand.Compose (the CLI's tests live in this file): a missing fallback font must fail before a COM
        // session, with its STA thread inside OneNote, exists; on success the orchestrator owns the session.
        RefreshRequest request = new("SFP Q1 2027", null, false, 14, true, false, new DateOnly(2027, 1, 4));
        FakeOneNoteApp app = new();
        int opened = 0;
        IOneNoteApp Open()
        {
            opened++;
            return app;
        }

        Action fontsMissing = () => RefreshCommand.Compose(
            request, TestManifest.Shipped, TestPaths.TemplatesDir, Serilog.Core.Logger.None, "run.log",
            () => throw new FileNotFoundException("Fallback font file not found: TeXGyreBonum-Regular.otf"),
            Open);

        fontsMissing.Should().Throw<FileNotFoundException>();
        opened.Should().Be(0, "the fonts load first, so a font failure never opens a COM session");

        IRefreshOrchestrator orchestrator = RefreshCommand.Compose(
            request, TestManifest.Shipped, TestPaths.TemplatesDir, Serilog.Core.Logger.None, "run.log",
            () => FontService.Load(TestManifest.Shipped.Font, TestPaths.TemplatesDir),
            Open);
        opened.Should().Be(1);
        app.Disposed.Should().BeFalse();
        ((IDisposable)orchestrator).Dispose();
        app.Disposed.Should().BeTrue("the orchestrator owns the session it was composed with");

        Action sessionFails = () => RefreshCommand.Compose(
            request, TestManifest.Shipped, TestPaths.TemplatesDir, Serilog.Core.Logger.None, "run.log",
            () => FontService.Load(TestManifest.Shipped.Font, TestPaths.TemplatesDir),
            () => throw new InvalidOperationException("OneNote could not be started"));
        sessionFails.Should().Throw<InvalidOperationException>("the fonts are released and the failure reaches the verb");
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
