using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Serilog;
using Sfp.Cli.Logging;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;

namespace Sfp.Cli.Verbs;

/// <summary>The parsed command line of the generate verb, before validation.</summary>
public sealed record GenerateArgs
{
    public required string Pdf { get; init; }

    public required string Start { get; init; }

    public string? Name { get; init; }

    public string? Out { get; init; }

    public string DateFormat { get; init; } = "mm/dd/yy";

    public string WeeksRemaining { get; init; } = "corrected";

    public int Dpi { get; init; } = 220;

    public string? RenderOnly { get; init; }

    public string? Manifest { get; init; }

    public bool Quiet { get; init; }

    public bool NoLinks { get; init; }

    public bool HideTitle { get; init; }

    public bool GroupWeeks { get; init; }

    public bool IncludeOutOfQuarterDays { get; init; }

    public string TimeBlocks { get; init; } = "6-22";

    public string? Previous { get; init; }

    public string? Templates { get; init; }
}

/// <summary>
/// sfp2onenote generate --pdf PATH --start YYYY-MM [--name N] [--out DIR] [--date-format mm/dd/yy|dd/mm/yy|m/d/yy|d/m/yy]
/// [--weeks-remaining corrected|as-printed] [--dpi 144|220|300] [--render-only DIR] [--manifest FILE] [--quiet]
/// [--no-links] [--hide-title] [--group-weeks] [--include-out-of-quarter-days] [--time-blocks 6-22] [--previous PATH]
/// [--templates DIR]. Validates the arguments (exit 2), loads the manifest, composes the services and runs the
/// orchestrator, printing progress lines, then the PRD summary or failure block. The integrator adds the command to
/// Program.cs with <see cref="Build"/>.
/// </summary>
public static class GenerateCommand
{
    /// <summary>Creates the orchestrator for a run: the production composition, or a test double.</summary>
    /// <param name="request">The run's request.</param>
    /// <param name="log">The run log.</param>
    /// <param name="logPath">The run's log file path.</param>
    public delegate IGenerateOrchestrator OrchestratorFactory(GenerateRequest request, ILogger log, string logPath);

    public static Command Build()
    {
        Option<string> pdf = new("--pdf") { Description = "Path of the SFP planner PDF.", Required = true };
        Option<string> start = new("--start") { Description = "First month of the quarter as YYYY-MM, for example 2027-01.", Required = true };
        Option<string?> name = new("--name") { Description = "Notebook name (default SFP {Qn} {YYYY}, for example SFP Q1 2027)." };
        Option<string?> output = new("--out") { Description = "Output folder (default Documents\\OneNote Notebooks)." };
        Option<string> dateFormat = new("--date-format") { Description = "Date slot format: " + string.Join(", ", RenderOnlyCommand.DateFormats.Keys) + ". Default " + RenderOnlyCommand.DefaultDateFormat + ".", DefaultValueFactory = _ => RenderOnlyCommand.DefaultDateFormat };
        Option<string> weeksRemaining = new("--weeks-remaining") { Description = "Weeks-remaining strip: corrected (default) or as-printed.", DefaultValueFactory = _ => "corrected" };
        Option<int> dpi = new("--dpi") { Description = "Render DPI: 144, 220 (default) or 300.", DefaultValueFactory = _ => 220 };
        Option<string?> renderOnly = new("--render-only") { Description = "Write the PNG files and pages.json to this folder and do not touch OneNote." };
        Option<string?> manifest = new("--manifest") { Description = "A user manifest merged over the shipped templates/sfp-quarterly.json." };
        Option<bool> quiet = new("--quiet") { Description = "Suppress console output except errors." };
        Option<bool> noLinks = new("--no-links") { Description = "Write no link anchors (a purely locked sheet)." };
        Option<bool> hideTitle = new("--hide-title") { Description = "Create pages without a title area and place the sheet at y = 0." };
        Option<bool> groupWeeks = new("--group-weeks") { Description = "Put the week sections inside one Weeks section group." };
        Option<bool> outOfQuarter = new("--include-out-of-quarter-days") { Description = "Add the days of the first and last week that fall outside the quarter." };
        Option<string> timeBlocks = new("--time-blocks") { Description = "Time Blocks window as START-END in 24-hour clock (default 6-22).", DefaultValueFactory = _ => "6-22" };
        Option<string?> previous = new("--previous") { Description = "Path of the previous quarter's notebook, for its Next quarter link." };
        Option<string?> templates = new("--templates") { Description = "The templates folder. Default: the one beside the executable, else the nearest one above it." };

        Command command = new("generate", "Builds the dated OneNote notebook for a quarter from the planner PDF.");
        foreach (Option option in new Option[] { pdf, start, name, output, dateFormat, weeksRemaining, dpi, renderOnly, manifest, quiet, noLinks, hideTitle, groupWeeks, outOfQuarter, timeBlocks, previous, templates })
        {
            command.Options.Add(option);
        }

        command.SetAction(parseResult => Run(
            new GenerateArgs
            {
                Pdf = parseResult.GetValue(pdf) ?? "",
                Start = parseResult.GetValue(start) ?? "",
                Name = parseResult.GetValue(name),
                Out = parseResult.GetValue(output),
                DateFormat = parseResult.GetValue(dateFormat) ?? RenderOnlyCommand.DefaultDateFormat,
                WeeksRemaining = parseResult.GetValue(weeksRemaining) ?? "corrected",
                Dpi = parseResult.GetValue(dpi),
                RenderOnly = parseResult.GetValue(renderOnly),
                Manifest = parseResult.GetValue(manifest),
                Quiet = parseResult.GetValue(quiet),
                NoLinks = parseResult.GetValue(noLinks),
                HideTitle = parseResult.GetValue(hideTitle),
                GroupWeeks = parseResult.GetValue(groupWeeks),
                IncludeOutOfQuarterDays = parseResult.GetValue(outOfQuarter),
                TimeBlocks = parseResult.GetValue(timeBlocks) ?? "6-22",
                Previous = parseResult.GetValue(previous),
                Templates = parseResult.GetValue(templates),
            },
            Console.Out,
            Console.Error));
        return command;
    }

    /// <summary>Runs the verb and returns the exit code from <see cref="ExitCodes"/>.</summary>
    /// <param name="args">The parsed arguments.</param>
    /// <param name="stdout">Progress and the summary.</param>
    /// <param name="stderr">Validation messages and the failure block.</param>
    /// <param name="orchestratorFactory">The orchestrator to run; null composes the production services.</param>
    /// <param name="logDirectory">Log folder override (tests); null uses the default folder.</param>
    /// <param name="cancellationToken">Cancels the run between pages (Ctrl+C is also honored).</param>
    public static int Run(
        GenerateArgs args,
        TextWriter stdout,
        TextWriter stderr,
        OrchestratorFactory? orchestratorFactory = null,
        string? logDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (!TryBuildOptions(args, stderr, out GenerateOptions? options, out string? templatesDir))
        {
            return ExitCodes.ValidationError;
        }

        Manifest manifest;
        try
        {
            ManifestLoader loader = new();
            manifest = string.IsNullOrWhiteSpace(args.Manifest)
                ? loader.LoadShipped(templatesDir)
                : loader.Merge(Path.Combine(templatesDir, ManifestLoader.ShippedFileName), args.Manifest);
        }
        catch (ManifestException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.ValidationError;
        }

        bool renderOnly = !string.IsNullOrWhiteSpace(args.RenderOnly);
        GenerateRequest request = new(options, manifest, templatesDir, renderOnly, renderOnly ? Path.GetFullPath(args.RenderOnly!) : null);

        // The verb prints its own progress, summary, warnings and failure block, so the log's console sink shows nothing
        // (RunLog.ConsoleShows): those lines are the only console output and the log file keeps every event. A log folder
        // that cannot be used moves the log to the temp folder with a one-line notice instead of stopping the run.
        RunLog? started = RunLog.StartWithFallback("generate", args.Quiet, stderr, logDirectory, printsOwnProgress: true);
        if (started is null)
        {
            return ExitCodes.ValidationError;
        }

        using RunLog log = started;
        log.Logger.Information("generate --pdf {Pdf} --start {Start} --dpi {Dpi} render-only {RenderOnly}", options.PdfPath, options.Start, options.Dpi, renderOnly);
        IGenerateOrchestrator orchestrator;
        try
        {
            orchestrator = (orchestratorFactory ?? ProductionOrchestrator.Create)(request, log.Logger, log.Path);
        }
        catch (Exception ex) when (ex is FontSubstitutionException or FileNotFoundException or DirectoryNotFoundException or InvalidDataException or ManifestException)
        {
            // Setup problems (a missing fallback font, a damaged templates folder) are validation errors, not crashes.
            stderr.WriteLine(ex.Message);
            log.Logger.Error(ex, "Setup failed");
            return ExitCodes.ValidationError;
        }

        GenerateResult result;
        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            void OnCancelKey(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cts.Cancel();
            }

            Console.CancelKeyPress += OnCancelKey;
            try
            {
                result = orchestrator.Run(request, new ProgressPrinter(stdout, args.Quiet), cts.Token);
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKey;
            }
        }
        finally
        {
            (orchestrator as IDisposable)?.Dispose();
        }

        Print(result, request, stdout, stderr, args.Quiet);
        return result.ExitCode;
    }

    /// <summary>
    /// Validates the arguments into <see cref="GenerateOptions"/>; writes the first problem to <paramref name="stderr"/>.
    /// The date formats and the templates lookup are the render-only and refresh verbs' own
    /// (<see cref="RenderOnlyCommand.DateFormats"/>, <see cref="PlanCommand.FindTemplatesDir"/>), so the verbs accept the
    /// same values and find the same folder.
    /// </summary>
    public static bool TryBuildOptions(GenerateArgs args, TextWriter stderr, [NotNullWhen(true)] out GenerateOptions? options, out string templatesDir)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stderr);
        options = null;
        string? foundTemplates = PlanCommand.FindTemplatesDir(args.Templates);
        templatesDir = foundTemplates is not null ? Path.GetFullPath(foundTemplates) : args.Templates ?? "";

        QuarterSpec start;
        try
        {
            start = QuarterSpec.Parse(args.Start);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // A blank --start ("" or spaces) reaches the parser as an ArgumentException; it is the same mistake as a
            // malformed month, so it gets the same kind of line and exit 2 instead of escaping as an exception.
            stderr.WriteLine(ex is FormatException
                ? ex.Message
                : "Start month must be YYYY-MM, for example 2027-01; got '" + args.Start + "'.");
            return false;
        }

        if (start.Year is < QuarterSpec.MinYear or > QuarterSpec.MaxYear || start.Month is < 1 or > 12)
        {
            stderr.WriteLine(GenerateText.StartMonthOutOfRange(start.Year, start.Month));
            return false;
        }

        if (string.IsNullOrWhiteSpace(args.Pdf) || !File.Exists(args.Pdf))
        {
            stderr.WriteLine("PDF not found: " + args.Pdf);
            return false;
        }

        if (!GenerateOptions.AllowedDpis.Contains(args.Dpi))
        {
            stderr.WriteLine(string.Format(CultureInfo.InvariantCulture, "DPI must be one of {0}; got {1}.", string.Join(", ", GenerateOptions.AllowedDpis), args.Dpi));
            return false;
        }

        if (!TryParseDateFormat(args.DateFormat, out DateFieldOrder order, out bool padded))
        {
            stderr.WriteLine("Date format must be one of " + string.Join(", ", RenderOnlyCommand.DateFormats.Keys) + "; got '" + args.DateFormat + "'.");
            return false;
        }

        if (!TryParseWeeksRemaining(args.WeeksRemaining, out WeeksRemainingMode weeks))
        {
            stderr.WriteLine("Weeks remaining must be corrected or as-printed; got '" + args.WeeksRemaining + "'.");
            return false;
        }

        if (!TryParseTimeBlocks(args.TimeBlocks, out TimeBlockWindow window))
        {
            stderr.WriteLine("Time blocks must be START-END with hours 0 to 24 and END after START, for example 6-22; got '" + args.TimeBlocks + "'.");
            return false;
        }

        if (foundTemplates is null)
        {
            stderr.WriteLine(string.IsNullOrWhiteSpace(args.Templates)
                ? $"Templates folder not found: no '{PlanCommand.TemplatesFolderName}/{ManifestLoader.ShippedFileName}' beside the executable or above it. Pass --templates."
                : $"Templates folder not found or has no {ManifestLoader.ShippedFileName}: {args.Templates}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(args.Manifest) && !File.Exists(args.Manifest))
        {
            stderr.WriteLine("Manifest file not found: " + args.Manifest);
            return false;
        }

        if (args.RenderOnly is not null && string.IsNullOrWhiteSpace(args.RenderOnly))
        {
            stderr.WriteLine("--render-only needs a folder path.");
            return false;
        }

        options = new GenerateOptions
        {
            Start = start,
            PdfPath = Path.GetFullPath(args.Pdf),
            NotebookName = string.IsNullOrWhiteSpace(args.Name) ? null : args.Name.Trim(),
            OutputFolder = string.IsNullOrWhiteSpace(args.Out) ? OutputFolders.Default() : Path.GetFullPath(args.Out),
            UserManifestPath = string.IsNullOrWhiteSpace(args.Manifest) ? null : Path.GetFullPath(args.Manifest),
            DateOrder = order,
            PadDateFields = padded,
            WeeksRemaining = weeks,
            Dpi = args.Dpi,
            IncludeOutOfQuarterDays = args.IncludeOutOfQuarterDays,
            GroupWeeks = args.GroupWeeks,
            Links = !args.NoLinks,
            HideTitle = args.HideTitle,
            TimeBlocks = window,
            PreviousNotebook = string.IsNullOrWhiteSpace(args.Previous) ? null : Path.GetFullPath(args.Previous),
            Quiet = args.Quiet,
        };
        return true;
    }

    /// <summary>
    /// One of <see cref="RenderOnlyCommand.DateFormats"/>, case-insensitive: "mm/dd/yy" (padded, month first),
    /// "dd/mm/yy" (padded, day first), "m/d/yy" (unpadded, month first) or "d/m/yy" (unpadded, day first), the four the
    /// app's dropdown offers too.
    /// </summary>
    public static bool TryParseDateFormat(string? text, out DateFieldOrder order, out bool padded)
    {
        if (RenderOnlyCommand.DateFormats.TryGetValue(text?.Trim() ?? "", out (DateFieldOrder Order, bool Padded) format))
        {
            (order, padded) = format;
            return true;
        }

        order = DateFieldOrder.MonthDayYear;
        padded = true;
        return false;
    }

    public static bool TryParseWeeksRemaining(string? text, out WeeksRemainingMode mode)
    {
        mode = WeeksRemainingMode.Corrected;
        switch (text?.Trim().ToUpperInvariant())
        {
            case "CORRECTED":
                return true;
            case "AS-PRINTED":
            case "ASPRINTED":
                mode = WeeksRemainingMode.AsPrinted;
                return true;
            default:
                return false;
        }
    }

    /// <summary>"6-22" to the window with StartHour 6 and EndHour 22.</summary>
    public static bool TryParseTimeBlocks(string? text, out TimeBlockWindow window)
    {
        window = TimeBlockWindow.Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int startHour)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int endHour))
        {
            return false;
        }

        TimeBlockWindow parsed = new(startHour, endHour);
        if (!parsed.IsValid)
        {
            return false;
        }

        window = parsed;
        return true;
    }

    /// <summary>
    /// The success summary, the cancelled line or the failure block per the PRD Generate flow. With --previous the
    /// summary says whether the previous notebook got its Next quarter link; when it did not, the reason is one of the
    /// warning lines.
    /// </summary>
    public static void Print(GenerateResult result, GenerateRequest request, TextWriter stdout, TextWriter stderr, bool quiet)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (result.Cancelled)
        {
            // The partial notebook is said to be deleted only when the run reports no failed deletion. A notebook left
            // on disk is reported even with --quiet: the user has to remove it before the name can be used again.
            List<string> notDeleted = result.Warnings.Where(GenerateMessages.IsPartialNotebookNotDeleted).ToList();
            if (notDeleted.Count > 0)
            {
                stderr.WriteLine("Cancelled, but the partial notebook could not be deleted.");
                foreach (string warning in notDeleted)
                {
                    stderr.WriteLine("Warning: " + warning);
                }

                return;
            }

            if (!quiet)
            {
                stdout.WriteLine(request.RenderOnly
                    ? "Cancelled. The rendered files so far were left in " + request.RenderOutputDir + "."
                    : result.NotebookName is null
                        ? "Cancelled before the notebook was created; nothing was written."
                        : "Cancelled. The partial notebook was deleted.");
            }

            return;
        }

        if (!result.Success)
        {
            stderr.WriteLine("Generate failed during " + GenerateOrchestrator.PhaseLabel(result.Phase) + ".");
            if (!string.IsNullOrEmpty(result.FailedPage))
            {
                stderr.WriteLine("Page: " + result.FailedPage);
            }

            stderr.WriteLine("Error: " + result.Error);
            foreach (string warning in result.Warnings)
            {
                stderr.WriteLine("Warning: " + warning);
            }

            if (!string.IsNullOrEmpty(result.LogPath))
            {
                stderr.WriteLine("Log: " + result.LogPath);
            }

            if (!request.RenderOnly && !string.IsNullOrEmpty(result.OutputPath))
            {
                stderr.WriteLine("The partial notebook at " + result.OutputPath + " was left in place for inspection.");
            }

            return;
        }

        if (quiet)
        {
            return;
        }

        if (request.RenderOnly)
        {
            // The render-only verb's own sentence: pages counts every page of the plan, PNG files only those with an image.
            stdout.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Rendered {0} pages of {1}: {2} PNG files and {3} written to {4} in {5:0.0} s.",
                result.PagesRendered,
                result.NotebookName,
                result.PngFiles,
                RenderOnlyRunner.IndexFileName,
                result.OutputPath,
                result.Elapsed.TotalSeconds));
        }
        else
        {
            stdout.WriteLine(string.Format(CultureInfo.InvariantCulture, "Generated {0}: {1} pages written in {2}.", result.NotebookName, result.PagesWritten, Elapsed(result.Elapsed)));
            stdout.WriteLine("Notebook folder: " + result.OutputPath + (result.OutputBytes is long bytes ? string.Format(CultureInfo.InvariantCulture, " ({0:0.0} MB)", bytes / 1048576.0) : ""));
            if (!string.IsNullOrEmpty(result.SidecarPath))
            {
                stdout.WriteLine("State file: " + result.SidecarPath);
            }

            if (!string.IsNullOrEmpty(result.NextQuarterLink))
            {
                stdout.WriteLine(result.NextQuarterLink);
            }
        }

        if (!string.IsNullOrEmpty(result.LogPath))
        {
            stdout.WriteLine("Log: " + result.LogPath);
        }

        foreach (string warning in result.Warnings)
        {
            stdout.WriteLine("Warning: " + warning);
        }

        if (!request.RenderOnly)
        {
            stdout.WriteLine();
            stdout.WriteLine(GenerateText.OneDriveMoveInstruction);
        }
    }

    private static string Elapsed(TimeSpan elapsed) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);

    /// <summary>
    /// Prints one line per progress report, in the order the reports arrive, synchronously (reports arrive from render
    /// workers too). The orchestrator reports each page once, from the worker that finished it, so page counts can
    /// arrive out of order; none is dropped. The Failed report is not printed: the failure block on stderr says it once.
    /// </summary>
    private sealed class ProgressPrinter : IProgress<GenerateProgress>
    {
        private readonly TextWriter _out;
        private readonly bool _quiet;
        private readonly object _gate = new();

        public ProgressPrinter(TextWriter output, bool quiet)
        {
            _out = output;
            _quiet = quiet;
        }

        public void Report(GenerateProgress value)
        {
            if (_quiet || value.Phase == GeneratePhase.Failed)
            {
                return;
            }

            string detail = string.IsNullOrEmpty(value.Detail) ? "" : ": " + value.Detail;
            string line = value.Total > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} ({1} of {2}){3}", GenerateOrchestrator.PhaseLabel(value.Phase), value.Current, value.Total, detail)
                : GenerateOrchestrator.PhaseLabel(value.Phase) + detail;
            lock (_gate)
            {
                _out.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// The production composition: manifest font through FontService (fallback with a warning), PDFium rasterizer,
    /// the page renderer, the date engine and plan builder, the OneNote locator and the COM writer, the preflight and
    /// the orchestrator. Owns the disposable services for the run.
    /// </summary>
    /// <summary>
    /// The production composition (fonts, rasterizer, COM session, writer, orchestrator) for one run. Public so the app's
    /// Generate tab composes the same services; the returned orchestrator is disposable and owns the COM session.
    /// </summary>
    public static IGenerateOrchestrator CreateProductionOrchestrator(GenerateRequest request, ILogger log, string logPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);
        return ProductionOrchestrator.Create(request, log, logPath);
    }

    private sealed class ProductionOrchestrator : IGenerateOrchestrator, IDisposable
    {
        private readonly GenerateOrchestrator _inner;
        private readonly FontService _fonts;
        private readonly Rasterizer _rasterizer;
        private readonly OneNoteLocator _oneNote;

        private ProductionOrchestrator(GenerateOrchestrator inner, FontService fonts, Rasterizer rasterizer, OneNoteLocator oneNote)
        {
            _inner = inner;
            _fonts = fonts;
            _rasterizer = rasterizer;
            _oneNote = oneNote;
        }

        public static IGenerateOrchestrator Create(GenerateRequest request, ILogger log, string logPath)
        {
            Manifest manifest = request.Manifest;
            FontService fonts = FontService.Load(manifest.Font, request.TemplatesDir);
            Rasterizer? rasterizer = null;
            OneNoteLocator? oneNote = null;
            try
            {
                // The fallback-font warning reaches the user once, in the run's warnings (the preflight carries
                // fonts.Warnings) printed with the summary; the log file keeps it here.
                foreach (string warning in fonts.Warnings)
                {
                    log.Information("Font fallback (reported with the summary): {Warning}", warning);
                }

                rasterizer = new Rasterizer(ColorParser.Parse(manifest.Colors.Background));
                oneNote = new OneNoteLocator(log);
                DateEngine dates = new();
                IPlanBuilder plans = new PlanBuilder(dates);
                IPageRenderer renderer = new PageRenderer(manifest, rasterizer, fonts, dates);
                SidecarStore sidecars = new();
                GeneratePreflight preflight = new(new ManifestValidator(), oneNote, new PreflightFileSystem(), sidecars, dates, fonts.Warnings);
                PageXmlOptions xml = PageXmlOptions.From(manifest, request.Options, fonts.FamilyInUse, TimeZoneInfo.Local);
                OneNoteLocator locator = oneNote;
                GenerateOrchestrator inner = new(
                    preflight,
                    dates,
                    plans,
                    renderer,
                    () => new ComNotebookWriter(locator.Open(), xml, log),
                    sidecars,
                    new GenerateOrchestratorOptions { FontFamily = fonts.FamilyInUse, TimeZone = TimeZoneInfo.Local, LogPath = logPath },
                    log,
                    () => new NextQuarterLinkWriter(locator.Open(), sidecars, manifest, xml, log));
                return new ProductionOrchestrator(inner, fonts, rasterizer, oneNote);
            }
            catch
            {
                oneNote?.Dispose();
                rasterizer?.Dispose();
                fonts.Dispose();
                throw;
            }
        }

        public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken) =>
            _inner.Run(request, progress, cancellationToken);

        public void Dispose()
        {
            _oneNote.Dispose();
            _rasterizer.Dispose();
            _fonts.Dispose();
        }
    }
}
