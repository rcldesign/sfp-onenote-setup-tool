namespace Sfp.App.ViewModels;

/// <summary>
/// Everything the view models need from the machine, gathered once by the composition root so no view model reads
/// the clock, the environment or special folders itself.
/// </summary>
/// <param name="Today">The run date (the default start month and Refresh's "today" derive from it).</param>
/// <param name="TemplatesDir">The templates folder beside the executable (manifest, fonts, holidays).</param>
/// <param name="DefaultOutputFolder">Documents\OneNote Notebooks.</param>
/// <param name="OneDriveRoots">The OneDrive sync folders from the OneDrive environment variables, or empty.</param>
/// <param name="RefreshExecutablePath">The sfp2onenote.exe the daily scheduled task runs.</param>
public sealed record AppEnvironment(
    DateOnly Today,
    string TemplatesDir,
    string DefaultOutputFolder,
    IReadOnlyList<string> OneDriveRoots,
    string RefreshExecutablePath);
