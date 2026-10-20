using Xunit;

namespace Sfp.TestSupport;

/// <summary>
/// The owner's SFP_No_Dates.pdf is never committed. Tests that need it read SFP_PDF_PATH and skip with a clear message
/// when it is absent, so the suite stays green on any machine.
/// </summary>
public static class SfpPdf
{
    public const string EnvVar = "SFP_PDF_PATH";

    /// <summary>The owner's copy inside the repository (docs/SFP No Dates.pdf), used when the variable is unset.</summary>
    public const string RepositoryRelativePath = "docs/SFP No Dates.pdf";

    /// <summary>The configured path, else the repository copy when present, else null.</summary>
    public static string? Path
    {
        get
        {
            string? p = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrWhiteSpace(p))
            {
                return File.Exists(p) ? p : null;
            }

            string repoCopy = System.IO.Path.Combine(TestPaths.RepoRoot, RepositoryRelativePath);
            return File.Exists(repoCopy) ? repoCopy : null;
        }
    }

    public static bool IsAvailable => Path is not null;

    /// <summary>Skips the current [SkippableFact] test when the PDF is not configured, and returns the path otherwise.</summary>
    public static string RequireOrSkip()
    {
        Skip.If(!IsAvailable, $"{EnvVar} is not set and {RepositoryRelativePath} is absent; skipping a test that needs the real planner PDF.");
        return Path!;
    }
}
