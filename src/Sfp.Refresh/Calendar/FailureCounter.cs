using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Counts consecutive runs in which every calendar source failed, persisted as sfp-refresh-failures.json next to the
/// sidecar so the count survives between scheduled runs. <see cref="ShouldToast"/> turns on at <see cref="ToastThreshold"/>;
/// a run that gets any events resets it. Timestamps are passed in; nothing here reads the clock.
/// </summary>
public sealed class FailureCounter
{
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

    /// <summary>The counter file beside a sidecar file (same folder).</summary>
    public static string PathBeside(string sidecarPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sidecarPath)) ?? ".", FileName);

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

    /// <summary>Records a run in which every source failed.</summary>
    public void RecordTotalFailure(DateTimeOffset when, string? message = null)
    {
        state = state with
        {
            ConsecutiveTotalFailures = state.ConsecutiveTotalFailures + 1,
            LastTotalFailure = when,
            LastMessage = message,
        };
    }

    /// <summary>Records a run that got events from at least one source, clearing the streak.</summary>
    public void Reset(DateTimeOffset when)
    {
        state = state with { ConsecutiveTotalFailures = 0, LastSuccess = when, LastMessage = null };
    }

    /// <summary>Writes the counter file, creating its folder when needed.</summary>
    public void Save()
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(state, JsonOptions));
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
