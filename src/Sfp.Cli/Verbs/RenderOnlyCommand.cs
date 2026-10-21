using System.CommandLine;
using System.Globalization;
using Sfp.Cli.Logging;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using Serilog;

namespace Sfp.Cli.Verbs;

/// <summary>The inputs of the render-only verb, one per command line option.</summary>
/// <param name="Pdf">Path of the planner PDF (required).</param>
/// <param name="Start">Quarter start month as YYYY-MM (required).</param>
/// <param name="Out">Output folder for the PNGs and pages.json (required, created if missing).</param>
/// <param name="Dpi">Render DPI: 144, 220 or 300.</param>
/// <param name="DateFormat">mm/dd/yy (default), dd/mm/yy, m/d/yy or d/m/yy.</param>
/// <param name="WeeksRemaining">corrected (default) or as-printed.</param>
/// <param name="NoLinks">Bake every date and number and emit no link anchors.</param>
/// <param name="TimeBlocks">Time Blocks window as START-END in 24-hour hours, for example 6-22.</param>
/// <param name="IncludeOutOfQuarterDays">Add the out-of-quarter days of the first and last week.</param>
/// <param name="ManifestPath">A user manifest merged over the shipped one, or null.</param>
/// <param name="TemplatesDir">The templates folder, or null to find it beside the executable or above it.</param>
/// <param name="Quiet">Print nothing but errors.</param>
/// <param name="LogDirectory">Folder for the run log, or null for the default (%LOCALAPPDATA%\sfp2onenote\logs).</param>
public sealed record RenderOnlyRequest(
    string Pdf,
    string Start,
    string Out,
    int Dpi = 220,
    string DateFormat = RenderOnlyCommand.DefaultDateFormat,
    string WeeksRemaining = RenderOnlyCommand.DefaultWeeksRemaining,
    bool NoLinks = false,
    string TimeBlocks = RenderOnlyCommand.DefaultTimeBlocks,
    bool IncludeOutOfQuarterDays = false,
    string? ManifestPath = null,
    string? TemplatesDir = null,
    bool Quiet = false,
    string? LogDirectory = null);

/// <summary>
/// sfp2onenote render-only --pdf PATH --start 2027-01 --out DIR [--dpi 220] [--date-format mm/dd/yy]
/// [--weeks-remaining corrected] [--no-links] [--time-blocks 6-22] [--include-out-of-quarter-days]
/// [--manifest user.json] [--templates DIR] [--quiet]: validates the PDF against the manifest, builds the plan and
/// writes every page as a PNG plus pages.json without touching OneNote. This is how overlay positions are tuned and
/// how the suite runs on a machine without Office. Exit codes: 0, 2 (bad option, manifest or PDF mismatch, or no folder,
/// the temp folder included, can hold the run log), 4 (render failure). The integrator adds the command to Program.cs with <see cref="Build"/>.
/// </summary>
public static class RenderOnlyCommand
{
    public const string Verb = "render-only";
    public const string DefaultDateFormat = "mm/dd/yy";
    public const string DefaultWeeksRemaining = "corrected";
    public const string DefaultTimeBlocks = "6-22";

    /// <summary>The accepted --date-format values and what they mean.</summary>
    public static IReadOnlyDictionary<string, (DateFieldOrder Order, bool Padded)> DateFormats { get; } = new Dictionary<string, (DateFieldOrder, bool)>(StringComparer.OrdinalIgnoreCase)
    {
        ["mm/dd/yy"] = (DateFieldOrder.MonthDayYear, true),
        ["dd/mm/yy"] = (DateFieldOrder.DayMonthYear, true),
        ["m/d/yy"] = (DateFieldOrder.MonthDayYear, false),
        ["d/m/yy"] = (DateFieldOrder.DayMonthYear, false),
    };

    /// <summary>The accepted --weeks-remaining values.</summary>
    public static IReadOnlyDictionary<string, WeeksRemainingMode> WeeksRemainingModes { get; } = new Dictionary<string, WeeksRemainingMode>(StringComparer.OrdinalIgnoreCase)
    {
        ["corrected"] = WeeksRemainingMode.Corrected,
        ["as-printed"] = WeeksRemainingMode.AsPrinted,
    };

    public static Command Build()
    {
        Option<string> pdf = new("--pdf") { Description = "Path of the planner PDF.", Required = true };
        Option<string> start = new("--start") { Description = "Quarter start month as YYYY-MM, for example 2027-01.", Required = true };
        Option<string> output = new("--out") { Description = "Output folder for the PNGs and pages.json (created if missing).", Required = true };
        Option<int> dpi = new("--dpi") { Description = "Render DPI: " + string.Join(", ", GenerateOptions.AllowedDpis) + ". Default 220.", DefaultValueFactory = _ => 220 };
        Option<string> dateFormat = new("--date-format") { Description = "Date slot format: " + string.Join(", ", DateFormats.Keys) + ". Default " + DefaultDateFormat + ".", DefaultValueFactory = _ => DefaultDateFormat };
        Option<string> weeksRemaining = new("--weeks-remaining") { Description = "Which number the strip circles: " + string.Join(" or ", WeeksRemainingModes.Keys) + ". Default " + DefaultWeeksRemaining + ".", DefaultValueFactory = _ => DefaultWeeksRemaining };
        Option<bool> noLinks = new("--no-links") { Description = "Bake every date and number into the image and emit no link anchors." };
        Option<string> timeBlocks = new("--time-blocks") { Description = "Time Blocks window as START-END in 24-hour hours. Default " + DefaultTimeBlocks + ".", DefaultValueFactory = _ => DefaultTimeBlocks };
        Option<bool> outOfQuarter = new("--include-out-of-quarter-days") { Description = "Add the days of the first and last week that fall outside the quarter, dated at 40% ink." };
        Option<string> manifest = new("--manifest") { Description = "A user manifest merged key by key over the shipped templates/sfp-quarterly.json." };
        Option<string> templates = new("--templates") { Description = "The templates folder. Default: the one beside the executable, else the nearest one above it." };
        Option<bool> quiet = new("--quiet") { Description = "Print nothing but errors." };

        Command command = new(Verb, "Renders every page of the notebook to PNGs plus pages.json without touching OneNote.");
        command.Options.Add(pdf);
        command.Options.Add(start);
        command.Options.Add(output);
        command.Options.Add(dpi);
        command.Options.Add(dateFormat);
        command.Options.Add(weeksRemaining);
        command.Options.Add(noLinks);
        command.Options.Add(timeBlocks);
        command.Options.Add(outOfQuarter);
        command.Options.Add(manifest);
        command.Options.Add(templates);
        command.Options.Add(quiet);
        command.SetAction(parseResult => Run(
            new RenderOnlyRequest(
                parseResult.GetValue(pdf) ?? "",
                parseResult.GetValue(start) ?? "",
                parseResult.GetValue(output) ?? "",
                parseResult.GetValue(dpi),
                parseResult.GetValue(dateFormat) ?? DefaultDateFormat,
                parseResult.GetValue(weeksRemaining) ?? DefaultWeeksRemaining,
                parseResult.GetValue(noLinks),
                parseResult.GetValue(timeBlocks) ?? DefaultTimeBlocks,
                parseResult.GetValue(outOfQuarter),
                parseResult.GetValue(manifest),
                parseResult.GetValue(templates),
                parseResult.GetValue(quiet)),
            Console.Out,
            Console.Error));
        return command;
    }

    /// <summary>Runs the verb; returns an exit code from <see cref="ExitCodes"/>. Messages go to the writers, details to the run log.</summary>
    public static int Run(RenderOnlyRequest request, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        GenerateOptions? options = ParseOptions(request, stderr);
        if (options is null)
        {
            return ExitCodes.ValidationError;
        }

        string? templatesDir = PlanCommand.FindTemplatesDir(request.TemplatesDir);
        if (templatesDir is null)
        {
            stderr.WriteLine(request.TemplatesDir is null
                ? $"Templates folder not found: no '{PlanCommand.TemplatesFolderName}/{ManifestLoader.ShippedFileName}' beside the executable or above it. Pass --templates."
                : $"Templates folder not found or has no {ManifestLoader.ShippedFileName}: {request.TemplatesDir}");
            return ExitCodes.ValidationError;
        }

        Manifest manifest;
        try
        {
            ManifestLoader loader = new();
            manifest = string.IsNullOrWhiteSpace(request.ManifestPath)
                ? loader.LoadShipped(templatesDir)
                : loader.Merge(Path.Combine(templatesDir, ManifestLoader.ShippedFileName), request.ManifestPath);
        }
        catch (ManifestException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.ValidationError;
        }

        ValidationResult validation = new ManifestValidator().Validate(manifest, request.Pdf);
        if (!validation.IsValid)
        {
            stderr.WriteLine($"The PDF does not match the manifest {manifest.TemplateId}:");
            foreach (string error in validation.Errors)
            {
                stderr.WriteLine("  " + error);
            }

            return ExitCodes.ValidationError;
        }

        // As generate --render-only does: a log folder that cannot be used moves the log to the temp folder with a notice.
        RunLog? started = RunLog.StartWithFallback(Verb, quiet: true, stderr, request.LogDirectory);
        if (started is null)
        {
            return ExitCodes.ValidationError;
        }

        using RunLog log = started;
        ILogger logger = log.Logger;
        logger.Information("render-only {Start} from {Pdf} to {Out} at {Dpi} DPI", request.Start, request.Pdf, request.Out, request.Dpi);
        foreach (string warning in validation.Warnings)
        {
            logger.Warning("{Warning}", warning);
            Say(request, stdout, "Warning: " + warning);
        }

        DateEngine engine = new();
        NotebookPlan plan;
        try
        {
            plan = new PlanBuilder(engine).Build(manifest, engine.Build(options.Start), options);
        }
        catch (Exception ex) when (ex is ManifestException or ArgumentException or InvalidOperationException)
        {
            // A plan that cannot be built (blank or colliding Notes names, a token with no value) is a validation error.
            logger.Warning(ex, "The plan could not be built");
            stderr.WriteLine(ex.Message);
            return ExitCodes.ValidationError;
        }

        try
        {
            using FontService fonts = FontService.Load(manifest.Font, templatesDir);
            foreach (string warning in fonts.Warnings)
            {
                logger.Warning("{Warning}", warning);
                Say(request, stdout, "Warning: " + warning);
            }

            using Rasterizer rasterizer = new(ColorParser.Parse(manifest.Colors.Background));
            PageRenderer renderer = new(manifest, rasterizer, fonts, engine);
            RenderOnlyRunner runner = new(renderer) { NamePageFailures = true };
            RenderOptions renderOptions = new(request.Pdf, request.Dpi, options);
            Progress<RenderProgress> progress = new(p => logger.Debug("Rendered {Done}/{Total} {Key} ({Title})", p.Done, p.Total, p.Key, p.Title));
            RenderOnlyResult result = runner.Run(plan, renderOptions, request.Out, progress);

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Rendered {0} pages of {1}: {2} PNG files and {3} written to {4} in {5:0.0} s.",
                result.PagesRendered, plan.Name, result.PngFiles, RenderOnlyRunner.IndexFileName, Path.GetFullPath(request.Out), result.Elapsed.TotalSeconds);
            logger.Information("{Summary}", summary);
            Say(request, stdout, summary);
            Say(request, stdout, "Log: " + log.Path);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Top-level handler of a CLI verb: any failure while rendering maps to the render exit code with one line. A
            // page that fails to render arrives as a PageRenderException, whose message names the page as Generate does.
            logger.Warning(ex, "Render failed");
            stderr.WriteLine("Render failed: " + ex.Message);
            stderr.WriteLine("Log: " + log.Path);
            return ExitCodes.RenderError;
        }
    }

    /// <summary>Parses "6-22" into a window; null with a message for anything else.</summary>
    public static TimeBlockWindow? ParseTimeBlocks(string text, out string? error)
    {
        error = null;
        string[] parts = (text ?? "").Trim().Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int end))
        {
            error = $"--time-blocks must be START-END in 24-hour hours, for example 6-22; got '{text}'.";
            return null;
        }

        TimeBlockWindow window = new(start, end);
        if (!window.IsValid)
        {
            error = $"--time-blocks window must start at 0 to 23 and end at 1 to 24, after the start; got '{text}'.";
            return null;
        }

        return window;
    }

    private static GenerateOptions? ParseOptions(RenderOnlyRequest request, TextWriter stderr)
    {
        QuarterSpec? spec = ParseStart(request.Start, stderr);
        if (spec is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Pdf) || !File.Exists(request.Pdf))
        {
            stderr.WriteLine($"PDF not found: {request.Pdf}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Out))
        {
            stderr.WriteLine("--out must name the output folder.");
            return null;
        }

        if (!GenerateOptions.AllowedDpis.Contains(request.Dpi))
        {
            stderr.WriteLine($"--dpi must be one of {string.Join(", ", GenerateOptions.AllowedDpis)}; got {request.Dpi.ToString(CultureInfo.InvariantCulture)}.");
            return null;
        }

        if (!DateFormats.TryGetValue(request.DateFormat ?? "", out (DateFieldOrder Order, bool Padded) format))
        {
            stderr.WriteLine($"--date-format must be one of {string.Join(", ", DateFormats.Keys)}; got '{request.DateFormat}'.");
            return null;
        }

        if (!WeeksRemainingModes.TryGetValue(request.WeeksRemaining ?? "", out WeeksRemainingMode mode))
        {
            stderr.WriteLine($"--weeks-remaining must be {string.Join(" or ", WeeksRemainingModes.Keys)}; got '{request.WeeksRemaining}'.");
            return null;
        }

        TimeBlockWindow? window = ParseTimeBlocks(request.TimeBlocks, out string? windowError);
        if (window is null)
        {
            stderr.WriteLine(windowError);
            return null;
        }

        return new GenerateOptions
        {
            Start = spec,
            PdfPath = request.Pdf,
            UserManifestPath = request.ManifestPath,
            DateOrder = format.Order,
            PadDateFields = format.Padded,
            WeeksRemaining = mode,
            Dpi = request.Dpi,
            IncludeOutOfQuarterDays = request.IncludeOutOfQuarterDays,
            Links = !request.NoLinks,
            TimeBlocks = window,
            Quiet = request.Quiet,
        };
    }

    private static QuarterSpec? ParseStart(string start, TextWriter stderr)
    {
        QuarterSpec spec;
        try
        {
            spec = QuarterSpec.Parse(start);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            stderr.WriteLine(ex.Message);
            return null;
        }

        if (spec.Month is < 1 or > 12)
        {
            stderr.WriteLine($"Start month must be 01 to 12; got {spec.Month.ToString(CultureInfo.InvariantCulture)} in '{start}'.");
            return null;
        }

        if (spec.Year < QuarterSpec.MinYear || spec.Year > QuarterSpec.MaxYear)
        {
            stderr.WriteLine($"Start year must be {QuarterSpec.MinYear.ToString(CultureInfo.InvariantCulture)} to {QuarterSpec.MaxYear.ToString(CultureInfo.InvariantCulture)}; got {spec.Year.ToString(CultureInfo.InvariantCulture)} in '{start}'.");
            return null;
        }

        return spec;
    }

    private static void Say(RenderOnlyRequest request, TextWriter stdout, string line)
    {
        if (!request.Quiet)
        {
            stdout.WriteLine(line);
        }
    }
}
