using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Sfp.Cli;

/// <summary>
/// sfp2onenote command line. Verbs are added per milestone: plan (M1.8), render-only (M2.10),
/// generate (M3.9), refresh (M4.7), measure (tools). Exit codes follow the PRD:
/// 0 success, 2 validation error, 3 OneNote unavailable, 4 render error, 5 write error, 6 refresh source unreachable.
/// </summary>
public static class Program
{
    /// <summary>The executable's name, used in the usage hint whatever process hosts the parser.</summary>
    public const string ExecutableName = "sfp2onenote";

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>
    /// Parses and runs a command line. A parse error (unknown verb or option, a missing required option, a value of the
    /// wrong type, no verb at all) is printed to <paramref name="error"/> with a usage hint and exits with the PRD's
    /// validation code 2; System.CommandLine's own handling would print help and exit 1. Help and version requests are
    /// not errors and print to <paramref name="output"/>.
    /// </summary>
    /// <param name="args">The arguments after the executable name.</param>
    /// <param name="output">Help and version text.</param>
    /// <param name="error">Parse errors and the usage hint.</param>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ParseResult parsed = BuildRoot().Parse(args);
        if (parsed.Action is ParseErrorAction)
        {
            foreach (ParseError parseError in parsed.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            error.WriteLine(UsageHint(parsed.CommandResult.Command));
            return ExitCodes.ValidationError;
        }

        return parsed.Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    /// <summary>The root command with every verb.</summary>
    public static RootCommand BuildRoot()
    {
        RootCommand root = new("sfp2onenote: builds a dated OneNote notebook from the Structured Focus Planner PDF.");
        root.Subcommands.Add(Verbs.PlanCommand.Build());
        root.Subcommands.Add(Verbs.RenderOnlyCommand.Build());
        root.Subcommands.Add(Verbs.GenerateCommand.Build());
        root.Subcommands.Add(Verbs.RefreshCommand.Build());
        root.Subcommands.Add(Verbs.MeasureCommand.Build());
        return root;
    }

    /// <summary>"Run 'sfp2onenote generate --help' for usage." for the verb the error belongs to (the root when there is none).</summary>
    private static string UsageHint(Command command) =>
        "Run '" + ExecutableName + (command is RootCommand ? "" : " " + command.Name) + " --help' for usage.";
}
