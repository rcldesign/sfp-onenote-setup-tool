using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Counts consecutive failed Refresh runs of one notebook, persisted beside its sidecar as
/// "{NotebookFolder}.sfp-refresh-failures.json" so the count survives between scheduled runs and two notebooks in one
/// folder keep their own streaks: a run in which every Setup calendar source failed, or one that OneNote failed (exit 3
/// or 5). <see cref="ShouldToast"/> turns on at <see cref="ToastThreshold"/>; a run that succeeds resets it (the
/// orchestrator decides once per run, from how the run ended). The file is written atomically (a temp file in the same
/// folder, then moved over it). Timestamps are passed in; nothing here reads the clock.
/// </summary>
public sealed class FailureCounter
{
    /// <summary>The counter file's suffix after the notebook name.</summary>
    public const string FileName = "sfp-refresh-failures.json";

    /// <summary>Consecutive total failures that raise the toast (PRD: three).</summary>
    public const int ToastThreshold = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private FailureCounterState state;

    private FailureCounter(string path, FailureCounterState state)
    {
        Path = path;
        this.state = state;
    }

    /// <summary>Where the counter is saved.</summary>
    public string Path { get; }

    public int ConsecutiveTotalFailures => state.ConsecutiveTotalFailures;

    public DateTimeOffset? LastTotalFailure => state.LastTotalFailure;

    public DateTimeOffset? LastSuccess => state.LastSuccess;

    /// <summary>Summary of the last total failure, for the toast and the summary line.</summary>
    public string? LastMessage => state.LastMessage;

    /// <summary>True once the count reaches <see cref="ToastThreshold"/>.</summary>
    public bool ShouldToast => state.ConsecutiveTotalFailures >= ToastThreshold;

    /// <summary>The counter file of the notebook a sidecar file belongs to: same folder, named after the notebook.</summary>
    public static string PathBeside(string sidecarPath) => Sfp.OneNote.SidecarStore.CompanionPath(sidecarPath, FileName);

    /// <summary>Loads the counter at <paramref name="path"/>; a missing or unreadable file starts at zero.</summary>
    public static FailureCounter Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new FailureCounter(path, new FailureCounterState());
        }

        try
        {
            FailureCounterState? loaded = JsonSerializer.Deserialize<FailureCounterState>(File.ReadAllText(path), JsonOptions);
            return new FailureCounter(path, loaded ?? new FailureCounterState());
        }
        catch (JsonException)
        {
            return new FailureCounter(path, new FailureCounterState());
        }
    }

    /// <summary>Loads the counter that lives beside a sidecar file.</summary>
    public static FailureCounter LoadBeside(string sidecarPath) => Load(PathBeside(sidecarPath));

    /// <summary>Records a failed run: every calendar source failed, or OneNote failed.</summary>
    public void RecordTotalFailure(DateTimeOffset when, string? message = null)
    {
        state = state with
        {
            ConsecutiveTotalFailures = state.ConsecutiveTotalFailures + 1,
            LastTotalFailure = when,
            LastMessage = message,
        };
    }

    /// <summary>Records a run that succeeded (for the calendar: got events from at least one source), clearing the streak.</summary>
    public void Reset(DateTimeOffset when)
    {
        state = state with { ConsecutiveTotalFailures = 0, LastSuccess = when, LastMessage = null };
    }

    /// <summary>
    /// Writes the counter file, creating its folder when needed: to a temp file in the same folder first, then moved over
    /// the counter, so a reader never sees a half-written file and a crash leaves the previous count.
    /// </summary>
    public void Save()
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tmp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    /// <summary>The persisted shape.</summary>
    private sealed record FailureCounterState
    {
        public int ConsecutiveTotalFailures { get; init; }

        public DateTimeOffset? LastTotalFailure { get; init; }

        public DateTimeOffset? LastSuccess { get; init; }

        public string? LastMessage { get; init; }
    }
}
