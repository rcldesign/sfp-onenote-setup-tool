using Serilog;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Orchestration;

/// <summary>Creates the propagator for a run, bound to the run's slot catalog and page reader (the production one is <see cref="Propagator"/>; tests inject a stub).</summary>
public delegate IPropagator PropagatorFactory(SlotCatalog catalog, Func<PageKey, PageXml?> pageReader);

/// <summary>
/// Everything a Refresh run is composed from. The CLI and the app build one per run (the COM session is per run) and
/// hand it to <see cref="RefreshOrchestrator"/>; tests swap in fakes. No DI container: constructor injection through this record.
/// </summary>
public sealed record RefreshServices
{
    public required IOneNoteApp App { get; init; }

    public required ISidecarStore Sidecars { get; init; }

    public required Manifest Manifest { get; init; }

    public required IDateEngine Dates { get; init; }

    public required IPlanBuilder Plans { get; init; }

    public required ISetupReader SetupReader { get; init; }

    public required PropagatorFactory Propagators { get; init; }

    public required IAnchorWriter Writer { get; init; }

    /// <summary>Text width in points at a size, for the legend truncation and the calendar placement.</summary>
    public required TextWidthMeasure Measure { get; init; }

    public required ILogger Log { get; init; }

    /// <summary>Creates calendar readers for the Setup sources; null means calendar import is unavailable (reported as a warning when asked for).</summary>
    public ICalendarSourceFactory? CalendarSources { get; init; }

    /// <summary>Raises the toast after three total calendar failures; null logs only.</summary>
    public ISourceFailureNotifier? Notifier { get; init; }

    /// <summary>Folders searched for the notebook's sidecar when the request names no folder.</summary>
    public IReadOnlyList<string> SearchFolders { get; init; } = Array.Empty<string>();

    /// <summary>The wall clock for LastRefresh and the failure counter; tests pass a fixed one.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.Now;

    /// <summary>Recorded in a rebuilt sidecar.</summary>
    public string ToolVersion { get; init; } = "unknown";

    /// <summary>Path of this run's log file, copied into the result.</summary>
    public string? LogPath { get; init; }

    /// <summary>How long to wait for a notebook to open; null uses the opener's default.</summary>
    public TimeSpan? OpenTimeout { get; init; }

    /// <summary>How to wait while a notebook opens; null sleeps.</summary>
    public Action<TimeSpan>? Sleep { get; init; }

    /// <summary>Add the bundled US holiday list to the Setup sources when none of them is a holidays source.</summary>
    public bool IncludeBundledHolidays { get; init; } = true;
}
