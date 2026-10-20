using System.CommandLine;

namespace Sfp.Cli;

/// <summary>
/// sfp2onenote command line. Verbs are added per milestone: plan (M1.8), render-only (M2.10),
/// generate (M3.9), refresh (M4.7), measure (tools). Exit codes follow the PRD:
/// 0 success, 2 validation error, 3 OneNote unavailable, 4 render error, 5 write error, 6 refresh source unreachable.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        RootCommand root = new("sfp2onenote: builds a dated OneNote notebook from the Structured Focus Planner PDF.");
        return root.Parse(args).Invoke();
    }
}
