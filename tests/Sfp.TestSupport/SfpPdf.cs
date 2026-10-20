using Xunit;

namespace Sfp.TestSupport;

/// <summary>
/// The owner's SFP_No_Dates.pdf is never committed. Tests that need it read SFP_PDF_PATH and skip with a clear message
/// when it is absent, so the suite stays green on any machine.
/// </summary>
public static class SfpPdf
{
    public const string EnvVar = "SFP_PDF_PATH";

    /// <summary>The configured path, or null.</summary>
    public static string? Path
    {
        get
        {
            string? p = Environment.GetEnvironmentVariable(EnvVar);
            return string.IsNullOrWhiteSpace(p) || !File.Exists(p) ? null : p;
        }
    }

    public static bool IsAvailable => Path is not null;

    /// <summary>Skips the current [SkippableFact] test when the PDF is not configured, and returns the path otherwise.</summary>
    public static string RequireOrSkip()
    {
        Skip.If(!IsAvailable, $"{EnvVar} is not set or does not point at SFP_No_Dates.pdf; skipping a test that needs the real planner PDF.");
        return Path!;
    }
}
