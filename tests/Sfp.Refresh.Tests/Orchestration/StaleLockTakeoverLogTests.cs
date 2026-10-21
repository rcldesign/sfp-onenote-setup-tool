using System.Globalization;
using Serilog.Events;
using Sfp.OneNote;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 4: the lock logs that it takes over a stale lock only once the stale file is deleted; a stale lock that cannot be
/// deleted is logged as such and never as taken over (the orchestrator then warns that every run is refused).
/// </summary>
public class StaleLockTakeoverLogTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 4, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_stale_lock_is_logged_as_taken_over_once_it_is_deleted()
    {
        string sidecarPath = SidecarPath();
        string lockPath = WriteStaleLock(sidecarPath);
        CapturingLog log = new();

        using RefreshLock? taken = RefreshLock.TryAcquire(sidecarPath, Now, log.Logger, out string? holder);

        taken.Should().NotBeNull();
        holder.Should().BeNull();
        List<string> warnings = log.Messages(LogEventLevel.Warning).ToList();
        warnings.Should().ContainSingle(m => m.Contains("Taking over the stale Refresh lock", StringComparison.Ordinal) && m.Contains(lockPath, StringComparison.Ordinal) && m.Contains("process 4242 on OTHER-PC", StringComparison.Ordinal));
        warnings.Should().NotContain(m => m.Contains("could not be deleted", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void A_stale_lock_that_cannot_be_deleted_is_logged_as_not_taken_over_and_never_as_taken_over()
    {
        string sidecarPath = SidecarPath();
        string lockPath = WriteStaleLock(sidecarPath);
        using StoppedRunStreakTests.Undeletable? stuck = StoppedRunStreakTests.Undeletable.Make(lockPath);
        Skip.If(stuck is null, "This system cannot make the lock file undeletable (chattr +i needs root off Windows).");
        CapturingLog log = new();

        RefreshLock? taken = RefreshLock.TryAcquire(sidecarPath, Now, log.Logger, out string? holder);

        taken.Should().BeNull();
        holder.Should().Contain("process 4242 on OTHER-PC");
        List<string> warnings = log.Messages(LogEventLevel.Warning).ToList();
        warnings.Should().NotContain(m => m.Contains("Taking over", StringComparison.Ordinal), "the stale lock was not taken over");
        warnings.Should().ContainSingle(m => m.Contains("could not be deleted", StringComparison.Ordinal) && m.Contains(lockPath, StringComparison.Ordinal) && m.Contains("process 4242 on OTHER-PC", StringComparison.Ordinal));
    }

    private static string SidecarPath() => Path.Combine(TestPaths.NewTempDir("stale-lock-log"), "SFP Q1 2027." + Sidecar.FileName);

    /// <summary>A lock taken three hours ago by a process on another machine: stale by age.</summary>
    private static string WriteStaleLock(string sidecarPath)
    {
        string lockPath = RefreshLock.PathBeside(sidecarPath);
        File.WriteAllText(
            lockPath,
            string.Create(CultureInfo.InvariantCulture, $"{{ \"token\": \"other\", \"processId\": 4242, \"processStart\": null, \"machine\": \"OTHER-PC\", \"acquired\": \"{Now.AddHours(-3):O}\" }}"));
        return lockPath;
    }
}
