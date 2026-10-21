using System.Diagnostics;
using System.Globalization;
using Serilog.Events;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-4 d): a run holds a lock file beside the sidecar for its whole length; a second run of the same
/// notebook meanwhile is refused with exit 5 and a clear message and leaves the streak alone; a stale lock (its process
/// gone, or taken more than two hours ago) is taken over with a logged note; the lock is released however the run ends.
/// </summary>
public class RefreshLockTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    [Fact]
    public void A_second_run_while_one_is_running_is_refused_and_the_first_one_records_everything()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        RefreshResult? second = null;
        bool lockedMeanwhile = false;
        h.App.BeforeWrite = _ =>
        {
            if (second is null)
            {
                lockedMeanwhile = File.Exists(RefreshLock.PathBeside(h.SidecarPath));
                second = h.Run(h.Request());
            }
        };

        RefreshResult first = h.Run();

        lockedMeanwhile.Should().BeTrue("the lock is held while the run writes");
        second.Should().NotBeNull();
        second!.Success.Should().BeFalse();
        second.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        second.Error.Should().StartWith("Another Refresh of 'SFP Q1 2027' is running (process " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + " on ")
            .And.EndWith("this run was not started. Try again when it has finished.");
        first.Success.Should().BeTrue(first.Error);
        first.Summary!.ValuesWritten.Should().Be(107);
        File.Exists(RefreshLock.PathBeside(h.SidecarPath)).Should().BeFalse("the lock is released when the run ends");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(0, "a refused run is not a failure of the notebook");
        h.App.BeforeWrite = null;
        h.Run(today: RefreshHarness.Jan4.AddDays(1)).Summary!.Changes.Should().BeEmpty("only one run wrote, and it recorded every outline");
    }

    [Fact]
    public void A_lock_whose_process_is_gone_is_taken_over_with_a_logged_note()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        WriteLock(h, processId: int.MaxValue, Environment.MachineName, RefreshHarness.Now.AddMinutes(-5));

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        h.Log.Messages(LogEventLevel.Warning).Should().Contain(m => m.Contains("Taking over the stale Refresh lock", StringComparison.Ordinal) && m.Contains("no longer running", StringComparison.Ordinal));
        File.Exists(RefreshLock.PathBeside(h.SidecarPath)).Should().BeFalse();
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
    }

    [Fact]
    public void A_lock_from_another_machine_holds_until_it_is_two_hours_old()
    {
        RefreshHarness h = new();
        WriteLock(h, processId: 4242, "OTHER-PC", RefreshHarness.Now.AddMinutes(-30));

        RefreshResult refused = h.Run(dryRun: true);

        refused.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode, "a preview takes the lock too");
        refused.Error.Should().Contain("process 4242 on OTHER-PC");
        File.Exists(RefreshLock.PathBeside(h.SidecarPath)).Should().BeTrue("another run's lock is left alone");

        WriteLock(h, processId: 4242, "OTHER-PC", RefreshHarness.Now.AddHours(-3));

        RefreshResult taken = h.Run();

        taken.Success.Should().BeTrue(taken.Error);
        h.Log.Messages(LogEventLevel.Warning).Should().Contain(m => m.Contains("more than 2 hours ago", StringComparison.Ordinal));
        File.Exists(RefreshLock.PathBeside(h.SidecarPath)).Should().BeFalse();
    }

    [Fact]
    public void A_lock_of_this_process_is_honoured_and_one_that_cannot_be_read_waits_for_its_grace()
    {
        RefreshHarness h = new();
        using (Process self = Process.GetCurrentProcess())
        {
            WriteLock(h, Environment.ProcessId, Environment.MachineName, RefreshHarness.Now.AddMinutes(-1), self.StartTime.ToUniversalTime());
        }

        h.Run().Error.Should().StartWith("Another Refresh of 'SFP Q1 2027' is running", "the process that holds it is alive");
        WriteLock(h, Environment.ProcessId, Environment.MachineName, RefreshHarness.Now.AddMinutes(-1), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        h.Run().Success.Should().BeTrue("the ID now belongs to another process than the one that took the lock");

        string path = RefreshLock.PathBeside(h.SidecarPath);
        File.WriteAllText(path, "{ not json");
        h.Run().Error.Should().Contain("a run whose lock file cannot be read", "it may be being written right now");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        h.Run().Success.Should().BeTrue("an unreadable lock older than its grace is stale");
    }

    [Fact]
    public void The_lock_is_released_when_the_run_fails()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.App.UpdatePageContentFailure = _ => new OneNoteException("UpdatePageContent", OneNoteHResults.PageReadOnly, "The page is read-only.", null);

        h.Run().ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);

        File.Exists(RefreshLock.PathBeside(h.SidecarPath)).Should().BeFalse();
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1);
        RefreshLock.PathBeside(h.SidecarPath).Should().Be(Path.Combine(h.Folder, "SFP Q1 2027.sfp-refresh.lock"));
    }

    private static void WriteLock(RefreshHarness h, int processId, string machine, DateTimeOffset acquired, DateTime? processStart = null)
    {
        string start = processStart is DateTime started ? "\"" + started.ToString("O", CultureInfo.InvariantCulture) + "\"" : "null";
        File.WriteAllText(
            RefreshLock.PathBeside(h.SidecarPath),
            string.Create(CultureInfo.InvariantCulture, $"{{ \"token\": \"other\", \"processId\": {processId}, \"processStart\": {start}, \"machine\": \"{machine}\", \"acquired\": \"{acquired:O}\" }}"));
    }
}
