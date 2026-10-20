using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Serilog;
using Sfp.Cli.Logging;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Writer;
using Sfp.Render.Fonts;

namespace Sfp.Cli.Verbs;

/// <summary>The parsed command line of the refresh verb, before validation.</summary>
public sealed record RefreshArgs
{
    public required string Notebook { get; init; }

    public string? Folder { get; init; }

    public bool Calendar { get; init; }

    public int Horizon { get; init; } = 14;

    public bool DryRun { get; init; }

    public bool WriteInked { get; init; }

    public bool Quiet { get; init; }

    /// <summary>The run date as YYYY-MM-DD (tests); null reads the clock.</summary>
    public string? Today { get; init; }

    public string? Manifest { get; init; }

    public string? Templates { get; init; }
}

/// <summary>
/// sfp2onenote refresh --notebook NAME [--folder DIR] [--calendar] [--horizon 14] [--dry-run] [--write-inked] [--quiet]
/// [--today YYYY-MM-DD] [--manifest FILE] [--templates DIR]: reads the notebook's Setup page and propagates its values
/// (and calendar events with --calendar) into the tool-owned objects, or lists every change with --dry-run. Composes
/// OneNoteApp, SidecarStore, SetupReader, Propagator, AnchorWriter and RefreshOrchestrator; the integrator adds the
/// command to Program.cs with <see cref="Build"/>. Exit codes: 0, 2 (arguments or notebook not found), 3 (OneNote
/// unavailable), 5 (write failure), 6 (every calendar source failed).
/// </summary>
public static class RefreshCommand
{
    /// <summary>Creates the orchestrator for a run: the production composition, or a test double.</summary>
    /// <param name="request">The run's request.</param>
    /// <param name="manifest">The merged manifest.</param>
    /// <param name="templatesDir">The templates folder (fonts, holidays).</param>
    /// <param name="log">The run log.</param>
    /// <param name="logPath">The run's log file path.</param>
    public delegate IRefreshOrchestrator OrchestratorFactory(RefreshRequest request, Manifest manifest, string templatesDir, ILogger log, string logPath);

    public static Command Build()
    {
        Option<string> notebook = new("--notebook") { Description = "Name of the notebook the tool generated, for example \"SFP Q1 2027\".", Required = true };
        Option<string?> folder = new("--folder") { Description = "Folder that holds the notebook and its sidecar (default: Documents\\OneNote Notebooks and the current folder)." };
        Option<bool> calendar = new("--calendar") { Description = "Also import calendar events from the Setup page's sources." };
        Option<int> horizon = new("--horizon") { Description = "Days ahead of today to write calendar events for (default 14).", DefaultValueFactory = _ => 14 };
        Option<bool> dryRun = new("--dry-run") { Description = "List every change without writing." };
        Option<bool> writeInked = new("--write-inked") { Description = "Write values that came from ink recognition without confirmation." };
        Option<bool> quiet = new("--quiet") { Description = "Suppress console output except errors." };
        Option<string?> today = new("--today") { Description = "The run date as YYYY-MM-DD (tests and replays); default: the machine's date." };
        Option<string?> manifest = new("--manifest") { Description = "A user manifest merged over the shipped templates/sfp-quarterly.json." };
        Option<string?> templates = new("--templates") { Description = "The templates folder. Default: the one beside the executable, else the nearest one above it." };

        Command command = new("refresh", "Propagates the Setup page's values (and calendar events) into a generated notebook, touching only the tool's own objects.");
        foreach (Option option in new Option[] { notebook, folder, calendar, horizon, dryRun, writeInked, quiet, today, manifest, templates })
        {
            command.Options.Add(option);
        }

        command.SetAction(parseResult => Run(
            new RefreshArgs
            {
                Notebook = parseResult.GetValue(notebook) ?? "",
                Folder = parseResult.GetValue(folder),
                Calendar = parseResult.GetValue(calendar),
                Horizon = parseResult.GetValue(horizon),
                DryRun = parseResult.GetValue(dryRun),
                WriteInked = parseResult.GetValue(writeInked),
                Quiet = parseResult.GetValue(quiet),
                Today = parseResult.GetValue(today),
                Manifest = parseResult.GetValue(manifest),
                Templates = parseResult.GetValue(templates),
            },
            Console.Out,
            Console.Error));
        return command;
    }

    /// <summary>Runs the verb and returns the exit code from <see cref="ExitCodes"/>.</summary>
    /// <param name="args">The parsed arguments.</param>
    /// <param name="stdout">Progress, the dry-run listing or the summary.</param>
    /// <param name="stderr">Validation messages and failures.</param>
    /// <param name="orchestratorFactory">The orchestrator to run; null composes the production services.</param>
    /// <param name="logDirectory">Log folder override (tests); null uses the default folder.</param>
    /// <param name="cancellationToken">Cancels the run between pages.</param>
    public static int Run(
        RefreshArgs args,
        TextWriter stdout,
        TextWriter stderr,
        OrchestratorFactory? orchestratorFactory = null,
        string? logDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (!TryBuildRequest(args, stderr, out RefreshRequest? request))
        {
            return ExitCodes.ValidationError;
        }

        string? templatesDir = PlanCommand.FindTemplatesDir(args.Templates);
        if (templatesDir is null)
        {
            stderr.WriteLine(args.Templates is null
                ? $"Templates folder not found: no '{PlanCommand.TemplatesFolderName}/{ManifestLoader.ShippedFileName}' beside the executable or above it. Pass --templates."
                : $"Templates folder not found or has no {ManifestLoader.ShippedFileName}: {args.Templates}");
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

        // The verb prints its own progress, listing and summary, so the log's console sink keeps to warnings and errors.
        using RunLog log = RunLog.Start("refresh", args.Quiet, logDirectory, printsOwnProgress: true);
        log.Logger.Information("refresh --notebook {Notebook} calendar {Calendar} horizon {Horizon} dry-run {DryRun} today {Today}", request.NotebookName, request.Calendar, request.HorizonDays, request.DryRun, request.Today);
        IRefreshOrchestrator orchestrator;
        try
        {
            orchestrator = (orchestratorFactory ?? CreateProductionOrchestrator)(request, manifest, templatesDir, log.Logger, log.Path);
        }
        catch (OneNoteUnavailableException ex)
        {
            stderr.WriteLine(ex.Message);
            log.Logger.Error(ex, "OneNote is unavailable");
            return ExitCodes.OneNoteUnavailable;
        }
        catch (Exception ex) when (ex is FontSubstitutionException or FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            // Setup problems (a missing fallback font, a damaged templates folder) are validation errors, not crashes.
            stderr.WriteLine(ex.Message);
            log.Logger.Error(ex, "Setup failed");
            return ExitCodes.ValidationError;
        }

        RefreshResult result;
        try
        {
            result = orchestrator.Run(request, new ProgressPrinter(stdout, args.Quiet), cancellationToken);
        }
        finally
        {
            (orchestrator as IDisposable)?.Dispose();
        }

        Print(result, request, stdout, stderr, args.Quiet);
        return result.ExitCode;
    }

    /// <summary>Validates the arguments into a <see cref="RefreshRequest"/>; writes the first problem to <paramref name="stderr"/>.</summary>
    public static bool TryBuildRequest(RefreshArgs args, TextWriter stderr, [NotNullWhen(true)] out RefreshRequest? request)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stderr);
        request = null;
        if (string.IsNullOrWhiteSpace(args.Notebook))
        {
            stderr.WriteLine("--notebook needs the name of a generated notebook, for example \"SFP Q1 2027\".");
            return false;
        }

        if (args.Horizon < 0)
        {
            stderr.WriteLine(string.Format(CultureInfo.InvariantCulture, "--horizon must be 0 or more days; got {0}.", args.Horizon));
            return false;
        }

        if (!string.IsNullOrWhiteSpace(args.Folder) && !Directory.Exists(args.Folder))
        {
            stderr.WriteLine("Folder not found: " + args.Folder);
            return false;
        }

        DateOnly today;
        if (string.IsNullOrWhiteSpace(args.Today))
        {
            today = DateOnly.FromDateTime(DateTime.Now);
        }
        else if (!DateOnly.TryParseExact(args.Today.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
        {
            stderr.WriteLine("--today must be YYYY-MM-DD; got '" + args.Today + "'.");
            return false;
        }

        request = new RefreshRequest(
            args.Notebook.Trim(),
            string.IsNullOrWhiteSpace(args.Folder) ? null : Path.GetFullPath(args.Folder),
            args.Calendar,
            args.Horizon,
            args.DryRun,
            args.WriteInked,
            today);
        return true;
    }

    /// <summary>The dry-run listing, the summary, or the failure block.</summary>
    public static void Print(RefreshResult result, RefreshRequest request, TextWriter stdout, TextWriter stderr, bool quiet)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (result.Summary is null)
        {
            stderr.WriteLine("Refresh failed: " + (result.Error ?? "unknown error"));
            if (!string.IsNullOrEmpty(result.LogPath))
            {
                stderr.WriteLine("Log: " + result.LogPath);
            }

            return;
        }

        if (request.DryRun)
        {
            foreach (string line in RefreshText.DryRunLines(result.Summary))
            {
                stdout.WriteLine(line);
            }

            return;
        }

        if (!result.Success)
        {
            foreach (string line in RefreshText.SummaryLines(result, request.NotebookName))
            {
                stderr.WriteLine(line);
            }

            return;
        }

        if (quiet)
        {
            return;
        }

        foreach (string line in RefreshText.SummaryLines(result, request.NotebookName))
        {
            stdout.WriteLine(line);
        }
    }

    /// <summary>Folders searched for a sidecar when --folder is not given: Documents\OneNote Notebooks and the current folder.</summary>
    public static IReadOnlyList<string> DefaultSearchFolders()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        List<string> folders = new();
        if (!string.IsNullOrEmpty(documents))
        {
            folders.Add(Path.Combine(documents, "OneNote Notebooks"));
        }

        folders.Add(Directory.GetCurrentDirectory());
        return folders;
    }

    /// <summary>
    /// The production composition: the COM session, the sidecar store, the font-measured widths, the calendar sources,
    /// the toast notifier and the orchestrator. Public so the Refresh tab composes the same services; the returned
    /// orchestrator is disposable and owns the COM session and the fonts. Throws <see cref="OneNoteUnavailableException"/> off Windows.
    /// </summary>
    public static IRefreshOrchestrator CreateProductionOrchestrator(RefreshRequest request, Manifest manifest, string templatesDir, ILogger log, string logPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDir);
        ArgumentNullException.ThrowIfNull(log);
        if (!OperatingSystem.IsWindows())
        {
            throw new OneNoteUnavailableException(OneNoteUnavailableException.UserMessage + " Refresh runs on Windows only.");
        }

        OneNoteApp app = new(log);
        FontService fonts = FontService.Load(manifest.Font, templatesDir);
        foreach (string warning in fonts.Warnings)
        {
            log.Warning("{Warning}", warning);
        }

        PageXmlOptions xml = new()
        {
            OneNote = manifest.OneNote,
            SheetWidthPt = manifest.PageWidthPt,
            SheetHeightPt = manifest.PageHeightPt,
            FontFamily = fonts.FamilyInUse,
            InkHex = manifest.Colors.Ink,
            BackgroundHex = manifest.Colors.Background,
            TimeZone = TimeZoneInfo.Local,
        };
        TextWidthMeasure measure = (text, sizePt) => fonts.TextWidth(text, sizePt);
        DateEngine dates = new();
        RefreshServices services = new()
        {
            App = app,
            Sidecars = new SidecarStore(),
            Manifest = manifest,
            Dates = dates,
            Plans = new PlanBuilder(dates),
            SetupReader = new SetupReader(),
            Propagators = (catalog, reader) => new Propagator(catalog, reader, measure),
            Writer = new AnchorWriter(app, xml, log),
            Measure = measure,
            Log = log,
            CalendarSources = new CalendarSourceFactory(templatesDir),
            Notifier = new WindowsToastNotifier(log),
            SearchFolders = DefaultSearchFolders(),
            ToolVersion = typeof(RefreshCommand).Assembly.GetName().Version?.ToString() ?? "unknown",
            LogPath = logPath,
        };
        return new DisposingOrchestrator(new RefreshOrchestrator(services), app, fonts);
    }

    /// <summary>Prints progress lines unless quiet.</summary>
    private sealed class ProgressPrinter : IProgress<string>
    {
        private readonly TextWriter stdout;
        private readonly bool quiet;

        public ProgressPrinter(TextWriter stdout, bool quiet)
        {
            this.stdout = stdout;
            this.quiet = quiet;
        }

        public void Report(string value)
        {
            if (!quiet)
            {
                stdout.WriteLine(value);
            }
        }
    }

    /// <summary>Runs the orchestrator and disposes the COM session and the fonts afterwards.</summary>
    private sealed class DisposingOrchestrator : IRefreshOrchestrator, IDisposable
    {
        private readonly IRefreshOrchestrator inner;
        private readonly IDisposable[] owned;

        public DisposingOrchestrator(IRefreshOrchestrator inner, params IDisposable[] owned)
        {
            this.inner = inner;
            this.owned = owned;
        }

        public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken) => inner.Run(request, progress, cancellationToken);

        public void Dispose()
        {
            foreach (IDisposable d in owned)
            {
                d.Dispose();
            }
        }
    }
}
