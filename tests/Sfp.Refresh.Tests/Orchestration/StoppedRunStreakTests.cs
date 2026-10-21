using Sfp.OneNote;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Serilog.Events;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-6: a run the opener stops (exit 2 with a Problem) and a run refused by a stale lock that cannot be
/// taken over count toward the failure streak, so the 05:00 task does not stop silently; a run refused because another
/// run really holds the lock, and a notebook that is simply not found, still leave the streak alone.
/// </summary>
public class StoppedRunStreakTests
{
    [Fact]
    public void Three_mornings_stopped_by_another_notebook_of_the_same_name_raise_the_toast_with_the_reason()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("streak-trial"));
        RefreshHarness h = new(app: trial.App);
        h.App.Close(h.Notebook.NotebookId);

        for (int day = 0; day < 3; day++)
        {
            RefreshResult stopped = h.Run(today: RefreshHarness.Jan4.AddDays(day), folder: h.Folder);

            stopped.ExitCode.Should().Be(RefreshOrchestrator.NotFoundExitCode);
            stopped.Error.Should().Contain(trial.NotebookPath);
            FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(day + 1, "a stop by the opener is a failure of the notebook");
        }

        h.Notifier.Notifications.Should().ContainSingle("the third stop in a row raises the toast");
        h.Notifier.Notifications[0].Title.Should().Be(RefreshOrchestrator.RefreshFailureToastTitle);
        h.Notifier.Notifications[0].Message.Should().Contain("failed 3 runs in a row").And.Contain(h.Notebook.NotebookPath).And.Contain(trial.NotebookPath);
    }

    [Fact]
    public void A_notebook_that_is_simply_not_found_leaves_the_streak_alone()
    {
        RefreshHarness h = new();
        h.App.DeleteHierarchy(h.Notebook.NotebookId, false);

        h.Run(folder: h.Folder).ExitCode.Should().Be(RefreshOrchestrator.NotFoundExitCode);

        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(0, "no other notebook is involved and the opener names no problem");
    }

    [Fact]
    public void Runs_refused_by_a_live_lock_leave_the_streak_alone()
    {
        RefreshHarness h = new();
        using (Process self = Process.GetCurrentProcess())
        {
            WriteLock(h, Environment.ProcessId, Environment.MachineName, RefreshHarness.Now.AddMinutes(-1), self.StartTime.ToUniversalTime());
        }

        for (int day = 0; day < 3; day++)
        {
            h.Run(today: RefreshHarness.Jan4.AddDays(day)).Error.Should().StartWith("Another run that uses 'SFP Q1 2027' is in progress").And.NotContain("is stale");
        }

        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(0, "another run really holds the lock");
        h.Notifier.Notifications.Should().BeEmpty();
        RefreshLock.IsStuck(h.SidecarPath, RefreshHarness.Now).Should().BeFalse();
        File.Delete(RefreshLock.PathBeside(h.SidecarPath));
        RefreshLock.IsStuck(h.SidecarPath, RefreshHarness.Now).Should().BeFalse("no lock file is there");
    }

    [SkippableFact]
    public void Runs_refused_by_a_stale_lock_that_cannot_be_deleted_count_and_the_third_raises_the_toast()
    {
        RefreshHarness h = new();
        WriteLock(h, processId: 4242, "OTHER-PC", RefreshHarness.Now.AddHours(-3));
        string lockPath = RefreshLock.PathBeside(h.SidecarPath);
        using Undeletable? stuck = Undeletable.Make(lockPath);
        Skip.If(stuck is null, "This system cannot make the lock file undeletable (chattr +i needs root off Windows).");

        for (int day = 0; day < 3; day++)
        {
            RefreshResult refused = h.Run(today: RefreshHarness.Jan4.AddDays(day));

            refused.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
            refused.Error.Should().Contain("process 4242 on OTHER-PC")
                .And.EndWith("this run was not started. Try again when it has finished. The lock file " + lockPath + " is stale and could not be removed; delete it by hand.");
            FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(day + 1, "the stale lock could not be taken over");
        }

        File.Exists(lockPath).Should().BeTrue();
        RefreshLock.IsStuck(h.SidecarPath, RefreshHarness.Now).Should().BeTrue();
        h.Notifier.Notifications.Should().ContainSingle().Which.Message.Should().Contain("failed 3 runs in a row").And.Contain("process 4242 on OTHER-PC");
        h.Log.Messages(LogEventLevel.Warning).Should().Contain(m => m.Contains("is stale but could not be taken over", StringComparison.Ordinal) && m.Contains(lockPath, StringComparison.Ordinal));
    }

    private static void WriteLock(RefreshHarness h, int processId, string machine, DateTimeOffset acquired, DateTime? processStart = null)
    {
        string start = processStart is DateTime started ? "\"" + started.ToString("O", CultureInfo.InvariantCulture) + "\"" : "null";
        File.WriteAllText(
            RefreshLock.PathBeside(h.SidecarPath),
            string.Create(CultureInfo.InvariantCulture, $"{{ \"token\": \"other\", \"processId\": {processId}, \"processStart\": {start}, \"machine\": \"{machine}\", \"acquired\": \"{acquired:O}\" }}"));
    }

    /// <summary>
    /// Keeps a file from being deleted until disposed, as a sync client holding it open or an access rule does: on Windows
    /// a handle open without delete sharing; elsewhere the immutable attribute (chattr +i), which needs root.
    /// </summary>
    private sealed class Undeletable : IDisposable
    {
        private readonly string path;
        private readonly FileStream? handle;

        private Undeletable(string path, FileStream? handle)
        {
            this.path = path;
            this.handle = handle;
        }

        /// <summary>The file made undeletable, or null when this system cannot do it.</summary>
        public static Undeletable? Make(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return new Undeletable(path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            }

            return Chattr("+i", path) ? new Undeletable(path, null) : null;
        }

        public void Dispose()
        {
            if (handle is not null)
            {
                handle.Dispose();
            }
            else
            {
                Chattr("-i", path);
            }
        }

        private static bool Chattr(string flag, string path)
        {
            ProcessStartInfo start = new("chattr") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(flag);
            start.ArgumentList.Add(path);
            try
            {
                using Process process = Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
