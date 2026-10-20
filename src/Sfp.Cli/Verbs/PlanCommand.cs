using System.CommandLine;
using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Cli.Verbs;

/// <summary>The inputs of the plan verb, one per command line option.</summary>
/// <param name="Start">Quarter start month as YYYY-MM (required).</param>
/// <param name="Name">Notebook name override, or null for the manifest pattern.</param>
/// <param name="Notes">Notes page names, or null or empty for the defaults.</param>
/// <param name="IncludeOutOfQuarterDays">Add the out-of-quarter days of the first and last week.</param>
/// <param name="GroupWeeks">Put the week sections in a "Weeks" section group.</param>
/// <param name="NoLinks">Emit no link anchors.</param>
/// <param name="Json">Print JSON instead of the tree.</param>
/// <param name="ManifestPath">A user manifest merged over the shipped one, or null.</param>
/// <param name="TemplatesDir">The templates folder, or null to find it beside the executable or above it.</param>
public sealed record PlanRequest(
    string Start,
    string? Name = null,
    IReadOnlyList<string>? Notes = null,
    bool IncludeOutOfQuarterDays = false,
    bool GroupWeeks = false,
    bool NoLinks = false,
    bool Json = false,
    string? ManifestPath = null,
    string? TemplatesDir = null);

/// <summary>
/// sfp2onenote plan --start 2027-01 [--name NAME] [--notes A B C] [--include-out-of-quarter-days] [--group-weeks]
/// [--no-links] [--json] [--manifest user.json] [--templates DIR]: prints the notebook plan as an indented tree
/// (section, then pages indented by level with title, key, PDF page and date, then a link summary) or as JSON with
/// the <see cref="PlanJson"/> schema. Touches nothing: no OneNote, no PDF, no log file. Program.cs adds the command
/// with <see cref="Build"/>.
/// </summary>
public static class PlanCommand
{
    /// <summary>Name of the templates folder beside the executable (and in the repository root).</summary>
    public const string TemplatesFolderName = "templates";

    public static Command Build()
    {
        Option<string> start = new("--start") { Description = "Quarter start month as YYYY-MM, for example 2027-01.", Required = true };
        Option<string> name = new("--name") { Description = "Notebook name. Default: the manifest pattern, \"SFP Q1 2027\"." };
        Option<string[]> notes = new("--notes")
        {
            Description = "Names of the ruled pages in the Notes section. Default: Backlog Ideas Notes Contacts.",
            AllowMultipleArgumentsPerToken = true,
        };
        Option<bool> outOfQuarter = new("--include-out-of-quarter-days") { Description = "Add the days of the first and last week that fall outside the quarter." };
        Option<bool> groupWeeks = new("--group-weeks") { Description = "Put the week sections inside one \"Weeks\" section group." };
        Option<bool> noLinks = new("--no-links") { Description = "Emit no link anchors (a purely locked sheet)." };
        Option<bool> json = new("--json") { Description = "Print the plan as JSON (name, quarter, sections, pages, links) instead of a tree." };
        Option<string> manifest = new("--manifest") { Description = "A user manifest merged key by key over the shipped templates/sfp-quarterly.json." };
        Option<string> templates = new("--templates") { Description = "The templates folder. Default: the one beside the executable, else the nearest one above it." };

        Command command = new("plan", "Prints the notebook plan for a quarter (sections, pages, PDF pages, dates and links) without touching OneNote.");
        command.Options.Add(start);
        command.Options.Add(name);
        command.Options.Add(notes);
        command.Options.Add(outOfQuarter);
        command.Options.Add(groupWeeks);
        command.Options.Add(noLinks);
        command.Options.Add(json);
        command.Options.Add(manifest);
        command.Options.Add(templates);
        command.SetAction(parseResult => Run(
            new PlanRequest(
                parseResult.GetValue(start) ?? "",
                parseResult.GetValue(name),
                parseResult.GetValue(notes),
                parseResult.GetValue(outOfQuarter),
                parseResult.GetValue(groupWeeks),
                parseResult.GetValue(noLinks),
                parseResult.GetValue(json),
                parseResult.GetValue(manifest),
                parseResult.GetValue(templates)),
            Console.Out,
            Console.Error));
        return command;
    }

    /// <summary>
    /// Runs the verb; returns an exit code from <see cref="ExitCodes"/>: 2 with one message on stderr and nothing on
    /// stdout for a bad month, folder or manifest, or for a manifest and options that cannot make a plan.
    /// </summary>
    public static int Run(PlanRequest request, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        QuarterSpec? spec = ParseStart(request.Start, stderr);
        if (spec is null)
        {
            return ExitCodes.ValidationError;
        }

        string? templatesDir = FindTemplatesDir(request.TemplatesDir);
        if (templatesDir is null)
        {
            stderr.WriteLine(request.TemplatesDir is null
                ? $"Templates folder not found: no '{TemplatesFolderName}/{ManifestLoader.ShippedFileName}' beside the executable or above it. Pass --templates."
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

        GenerateOptions options = new()
        {
            Start = spec,
            NotebookName = request.Name,
            IncludeOutOfQuarterDays = request.IncludeOutOfQuarterDays,
            GroupWeeks = request.GroupWeeks,
            Links = !request.NoLinks,
        };
        if (request.Notes is { Count: > 0 })
        {
            options = options with { NotesPages = request.Notes };
        }

        DateEngine engine = new();
        NotebookPlan plan;
        try
        {
            plan = new PlanBuilder(engine).Build(manifest, engine.Build(spec), options);
        }
        catch (Exception ex) when (ex is ManifestException or InvalidOperationException or ArgumentException)
        {
            // The manifest loaded, but it cannot make a plan with these options: a page the template repeats, an index
            // outside its instances, a week page outside an eachWeek section, blank or colliding --notes names.
            stderr.WriteLine(ex.Message);
            return ExitCodes.ValidationError;
        }

        if (request.Json)
        {
            stdout.WriteLine(PlanJson.From(plan).Serialize());
        }
        else
        {
            PlanText.WriteTree(plan, stdout);
            stdout.WriteLine(LinkSummary(plan));
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// The templates folder: <paramref name="explicitDir"/> when given (null when it holds no manifest), else the
    /// first folder named templates holding the shipped manifest beside the executable (AppContext.BaseDirectory) or
    /// in any folder above it, which reaches the repository's templates folder from a build output. Null when none.
    /// </summary>
    public static string? FindTemplatesDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return HasManifest(explicitDir) ? explicitDir : null;
        }

        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, TemplatesFolderName);
            if (HasManifest(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>"585 links: annualObjective 8, calendarDay 90, ..." with the anchor types in alphabetical order.</summary>
    public static string LinkSummary(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        IEnumerable<string> parts = plan.Links
            .GroupBy(l => l.AnchorType, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture));
        string count = plan.Links.Count.ToString(CultureInfo.InvariantCulture);
        return plan.Links.Count == 0 ? "0 links" : count + " links: " + string.Join(", ", parts);
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

    private static bool HasManifest(string dir) => File.Exists(Path.Combine(dir, ManifestLoader.ShippedFileName));
}
