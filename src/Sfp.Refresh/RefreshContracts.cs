using Sfp.Core.Dates;

namespace Sfp.Refresh;

/// <summary>Inputs of one Refresh run.</summary>
/// <param name="NotebookName">Name of a notebook the tool built (matched against sidecars, then OneNote's hierarchy).</param>
/// <param name="NotebooksFolder">Folder holding the notebook and its sidecar, or null to search the known notebooks.</param>
/// <param name="Calendar">Also import calendar events.</param>
/// <param name="HorizonDays">Days ahead of today to write calendar events for (default 14).</param>
/// <param name="DryRun">List every change without writing.</param>
/// <param name="WriteInkedValues">Propagate values that came from ink without confirmation.</param>
/// <param name="Today">The run date (injected so tests do not read the clock).</param>
public sealed record RefreshRequest(
    string NotebookName,
    string? NotebooksFolder,
    bool Calendar,
    int HorizonDays,
    bool DryRun,
    bool WriteInkedValues,
    DateOnly Today);

/// <summary>What a Refresh run read, planned and did, for the summary screen and the dry-run listing.</summary>
public sealed record RefreshSummary
{
    public required SetupValues Values { get; init; }

    /// <summary>Every planned change, including blocked ones.</summary>
    public required IReadOnlyList<PlannedChange> Changes { get; init; }

    public int PagesTouched { get; init; }

    public int ValuesWritten { get; init; }

    public int EventsWritten { get; init; }

    /// <summary>Changes skipped because a user object was in the way or the value was inked and unconfirmed.</summary>
    public int Skipped { get; init; }

    public IReadOnlyList<string> FailedSources { get; init; } = Array.Empty<string>();

    /// <summary>True when the sidecar was missing or stale and was rebuilt from the notebook's page keys.</summary>
    public bool SidecarRebuilt { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>Outcome of a Refresh run.</summary>
public sealed record RefreshResult
{
    public required bool Success { get; init; }

    /// <summary>Exit code per the PRD: 0, 2, 3, 5 or 6.</summary>
    public required int ExitCode { get; init; }

    public RefreshSummary? Summary { get; init; }

    public string? Error { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string? LogPath { get; init; }
}

/// <summary>Runs Refresh: open the notebook, recover the sidecar if needed, read Setup, plan propagation and calendar changes, write or list them, sync.</summary>
public interface IRefreshOrchestrator
{
    RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken);
}

/// <summary>A notebook the tool built, as listed on the Refresh tab from its sidecar.</summary>
/// <param name="Name">Notebook name.</param>
/// <param name="Path">Notebook folder path.</param>
/// <param name="Quarter">Quarter start month.</param>
/// <param name="Generated">When it was generated.</param>
/// <param name="LastRefresh">When it was last refreshed, or null.</param>
/// <param name="SidecarPath">Path of its sfp-state.json.</param>
public sealed record KnownNotebook(string Name, string Path, QuarterSpec Quarter, DateTimeOffset? Generated, DateTimeOffset? LastRefresh, string SidecarPath);

/// <summary>Finds the notebooks the tool built by scanning for sidecar files under the output folders it knows.</summary>
public interface INotebookRegistry
{
    IReadOnlyList<KnownNotebook> List(IEnumerable<string> folders);
}

/// <summary>Registers and removes the daily 05:00 Windows Task Scheduler task that runs refresh --calendar.</summary>
public interface IScheduledTaskService
{
    bool IsRegistered(string notebookName);

    void Register(string notebookName, string executablePath, int horizonDays);

    void Remove(string notebookName);
}
