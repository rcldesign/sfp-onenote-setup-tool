namespace Sfp.App.ViewModels;

/// <summary>
/// Everything the view models need from the machine, gathered once by the composition root so no view model reads
/// the clock, the environment or special folders itself.
/// </summary>
/// <param name="Today">The date the app started on; <see cref="CurrentDate"/> falls back to it when no <see cref="Clock"/> is set.</param>
/// <param name="TemplatesDir">The templates folder beside the executable (manifest, fonts, holidays).</param>
/// <param name="DefaultOutputFolder">Documents\OneNote Notebooks.</param>
/// <param name="OneDriveRoots">The OneDrive sync folders from the OneDrive environment variables, or empty.</param>
/// <param name="RefreshExecutablePath">The sfp2onenote.exe the daily scheduled task runs.</param>
public sealed record AppEnvironment(
    DateOnly Today,
    string TemplatesDir,
    string DefaultOutputFolder,
    IReadOnlyList<string> OneDriveRoots,
    string RefreshExecutablePath)
{
    /// <summary>
    /// Reads the date at the moment it is needed. The window can stay open past midnight, and Refresh freezes every day
    /// before "today", so each Refresh request and each settings load asks the clock instead of using the start date.
    /// Null means <see cref="Today"/> always (tests that do not care about the clock).
    /// </summary>
    public Func<DateOnly>? Clock { get; init; }

    /// <summary>
    /// The font service's warnings (the bundled fallback is in use because the template typeface is not installed),
    /// shown as a banner on the Generate tab before anything is generated. Empty when the typeface is installed.
    /// </summary>
    public IReadOnlyList<string> FontWarnings { get; init; } = Array.Empty<string>();

    /// <summary>The date now: the <see cref="Clock"/> when one is set, else <see cref="Today"/>.</summary>
    public DateOnly CurrentDate() => Clock?.Invoke() ?? Today;
}
