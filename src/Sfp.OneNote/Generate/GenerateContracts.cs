using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.OneNote.Generate;

/// <summary>Phases of a Generate run, in order, as the progress bar shows them.</summary>
public enum GeneratePhase
{
    CheckingOneNote,
    Validating,
    Rendering,
    CreatingNotebook,
    WritingPages,
    BuildingContents,
    Finishing,
    Done,
    Cancelled,
    Failed,
}

/// <summary>One progress report: the phase, and for the page phases the page counter and the page being handled.</summary>
/// <param name="Phase">Current phase.</param>
/// <param name="Current">Pages done in this phase (0 when not a page phase).</param>
/// <param name="Total">Pages in this phase (0 when not a page phase).</param>
/// <param name="Detail">The page title being rendered or written, or a short note.</param>
public sealed record GenerateProgress(GeneratePhase Phase, int Current, int Total, string? Detail);

/// <summary>Everything a Generate run needs. Built by the CLI or the app from user input and the loaded manifest.</summary>
/// <param name="Options">Generate options (start month, PDF path, name, folder, switches).</param>
/// <param name="Manifest">The merged manifest.</param>
/// <param name="TemplatesDir">The templates folder beside the executable (fonts, holidays).</param>
/// <param name="RenderOnly">Write PNGs and pages.json to <paramref name="RenderOutputDir"/> and never touch OneNote.</param>
/// <param name="RenderOutputDir">Output folder for render-only, else null.</param>
public sealed record GenerateRequest(GenerateOptions Options, Manifest Manifest, string TemplatesDir, bool RenderOnly, string? RenderOutputDir);

/// <summary>Outcome of a Generate run, with everything the summary screen and the exit code need.</summary>
public sealed record GenerateResult
{
    public required bool Success { get; init; }

    public bool Cancelled { get; init; }

    /// <summary>Exit code per the PRD: 0, 2, 3, 4 or 5.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Phase in which the run stopped (Done on success).</summary>
    public required GeneratePhase Phase { get; init; }

    /// <summary>Notebook folder on disk, or the render output folder for render-only.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Notebook name as created.</summary>
    public string? NotebookName { get; init; }

    public int PagesRendered { get; init; }

    public int PagesWritten { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Size of the notebook folder (or PNG folder) in bytes when known.</summary>
    public long? OutputBytes { get; init; }

    /// <summary>Error text for the failure view: phase, page, HRESULT or exception message.</summary>
    public string? Error { get; init; }

    /// <summary>Title of the page that failed, if any.</summary>
    public string? FailedPage { get; init; }

    /// <summary>Path of this run's log file.</summary>
    public string? LogPath { get; init; }

    /// <summary>Non-fatal notes (fallback font in use, OneDrive folder warning, dropped dateTime).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Path of the sidecar written beside the notebook, when one was.</summary>
    public string? SidecarPath { get; init; }

    /// <summary>
    /// The summary line of the --previous step when the previous notebook's Next quarter link was written ("Next quarter
    /// link added to the quarterly review of SFP Q4 2026."); null when no previous notebook was given or the link was not
    /// written, in which case a warning says why.
    /// </summary>
    public string? NextQuarterLink { get; init; }
}

/// <summary>Pre-run checks from the PRD error table: OneNote present and signed in, PDF matches, folder writable, name free.</summary>
/// <param name="Errors">Blocking problems, worded for the user, each paired with the exit code it maps to.</param>
/// <param name="Warnings">Non-blocking notes (output folder under OneDrive, fallback font).</param>
public sealed record PreflightResult(IReadOnlyList<(string Message, int ExitCode)> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Runs the checks before anything is rendered or written. Render-only skips the OneNote checks.</summary>
public interface IGeneratePreflight
{
    PreflightResult Check(GenerateRequest request);
}

/// <summary>
/// Drives one Generate run end to end: preflight, plan, render (parallel), create notebook and sections, create pages,
/// hierarchy, hyperlinks, write pages (single STA thread), Contents page, sidecar, sync, navigate to the cover.
/// Cancellation stops after the current page and deletes the partial notebook. Never throws for expected failures;
/// the result carries the phase, page and message.
/// </summary>
public interface IGenerateOrchestrator
{
    GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken);
}
