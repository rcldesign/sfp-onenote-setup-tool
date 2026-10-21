using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Serilog;
using Sfp.OneNote;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// The lock file a Refresh run holds beside the notebook's sidecar ("{NotebookFolder}.sfp-refresh.lock") for the whole
/// run, so two runs of one notebook (the 05:00 task and "Refresh now") never interleave their reads and sidecar saves.
/// It records the holder's process ID and start time, the machine and when it was taken. A lock is stale, and is taken
/// over with a logged note, when its process is gone (on this machine; an ID that now belongs to another process counts
/// as gone) or when it was taken more than <see cref="StaleAfter"/> ago; a lock file that cannot be read is stale once it
/// is older than <see cref="UnreadableGrace"/>. Releasing deletes the file only while it is still this run's lock.
/// </summary>
public sealed class RefreshLock : IDisposable
{
    /// <summary>The lock file's suffix after the notebook name.</summary>
    public const string FileSuffix = "sfp-refresh.lock";

    /// <summary>A lock taken longer ago than this is stale whatever its process does (a run takes minutes).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <summary>A lock file that cannot be read is stale once it is older than this (its writer would have finished).</summary>
    public static readonly TimeSpan UnreadableGrace = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string token;
    private readonly ILogger log;
    private bool released;

    private RefreshLock(string path, string token, ILogger log)
    {
        Path = path;
        this.token = token;
        this.log = log;
    }

    /// <summary>Where the lock file is.</summary>
    public string Path { get; }

    /// <summary>The lock file of the notebook a sidecar file belongs to: same folder, named after the notebook.</summary>
    public static string PathBeside(string sidecarPath) => SidecarStore.CompanionPath(sidecarPath, FileSuffix);

    /// <summary>
    /// Takes the lock beside <paramref name="sidecarPath"/>. Returns it, or null: with <paramref name="holder"/> naming the
    /// run that holds it when a live run does (the caller refuses to run), or with <paramref name="holder"/> null when the
    /// lock file cannot be written at all (logged; the run goes ahead unlocked, its sidecar save will report the folder).
    /// </summary>
    public static RefreshLock? TryAcquire(string sidecarPath, DateTimeOffset now, ILogger log, out string? holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        ArgumentNullException.ThrowIfNull(log);
        string path = PathBeside(sidecarPath);
        for (int attempt = 1; ; attempt++)
        {
            string token = Guid.NewGuid().ToString("N");
            try
            {
                if (TryCreate(path, token, now))
                {
                    holder = null;
                    return new RefreshLock(path, token, log);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning(ex, "The Refresh lock {Path} could not be written; the run goes ahead without it", path);
                holder = null;
                return null;
            }

            LockOwner? owner = Read(path);
            string? stale = StaleReason(path, owner, now);
            if (stale is null || attempt == 2)
            {
                holder = Describe(owner);
                return null;
            }

            if (owner is not null)
            {
                log.Warning("Taking over the stale Refresh lock {Path} held by {Holder}: {Reason}", path, Describe(owner), stale);
            }

            TryDelete(path);
        }
    }

    /// <summary>Deletes the lock file while it is still this run's lock (a lock taken over after it went stale is another run's).</summary>
    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;
        if (Read(Path) is { } owner && string.Equals(owner.Token, token, StringComparison.Ordinal))
        {
            TryDelete(Path);
        }
        else
        {
            log.Warning("The Refresh lock {Path} was taken over by another run and is left to it", Path);
        }
    }

    /// <summary>Creates the lock file with this process as its owner; false when one exists already.</summary>
    private static bool TryCreate(string path, string token, DateTimeOffset now)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }

        using (stream)
        {
            JsonSerializer.Serialize(stream, new LockOwner(token, Environment.ProcessId, CurrentProcessStart(), Environment.MachineName, now), JsonOptions);
        }

        return true;
    }

    private static LockOwner? Read(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<LockOwner>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Why the lock at <paramref name="path"/> is stale, or null when its run may still be going.</summary>
    private static string? StaleReason(string path, LockOwner? owner, DateTimeOffset now)
    {
        if (owner is null)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return "it was released";
                }

                return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > UnreadableGrace ? "its file cannot be read" : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (now - owner.Acquired > StaleAfter)
        {
            return string.Format(CultureInfo.InvariantCulture, "it was taken more than {0:0} hours ago", StaleAfter.TotalHours);
        }

        if (string.Equals(owner.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase) && !IsRunning(owner.ProcessId, owner.ProcessStart))
        {
            return "its process is no longer running";
        }

        return null;
    }

    /// <summary>True when the process runs and, when its start time was recorded, is the same process (not a reused ID).</summary>
    private static bool IsRunning(int processId, DateTimeOffset? started)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            return started is not DateTimeOffset recorded || Math.Abs((process.StartTime.ToUniversalTime() - recorded.UtcDateTime).TotalSeconds) < 1;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            // The process cannot be inspected (another user's, say): it is taken to be the run that holds the lock.
            return true;
        }
    }

    private static DateTimeOffset? CurrentProcessStart()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            return new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Describe(LockOwner? owner) => owner is null
        ? "a run whose lock file cannot be read"
        : string.Format(CultureInfo.InvariantCulture, "process {0} on {1}, since {2:yyyy-MM-dd HH:mm}", owner.ProcessId, owner.Machine, owner.Acquired);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A lock file left behind goes stale with its process.
        }
    }

    /// <summary>The persisted shape of the lock file.</summary>
    private sealed record LockOwner(string Token, int ProcessId, DateTimeOffset? ProcessStart, string Machine, DateTimeOffset Acquired);
}
