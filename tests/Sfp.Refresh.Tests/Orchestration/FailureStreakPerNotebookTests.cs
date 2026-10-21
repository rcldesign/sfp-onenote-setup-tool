using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-8): the failure streak is kept per notebook, so a finished quarter's daily task that succeeds does
/// not reset the streak of the next quarter's notebook in the same folder, whose dead feed then raises the toast.
/// </summary>
public class FailureStreakPerNotebookTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;

    [Fact]
    public void Two_notebooks_in_one_folder_keep_their_own_failure_streaks()
    {
        StubCalendars dead = new() { Failure = new TimeoutException("The request timed out after 30 s.") };
        RefreshHarness q1 = new(calendars: dead);
        RefreshHarness q2 = new(calendars: dead, folder: q1.Folder, app: q1.App, start: new QuarterSpec(2027, 4));
        q1.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        q2.Type(S.CalendarSources[0], "Work | https://example.com/cal.ics");
        FailureCounter.PathBeside(q1.SidecarPath).Should().NotBe(FailureCounter.PathBeside(q2.SidecarPath));

        for (int day = 0; day < 3; day++)
        {
            DateOnly today = new DateOnly(2027, 4, 5).AddDays(day);
            q2.Run(calendar: true, today: today).ExitCode.Should().Be(6, "Q2's only feed is dead");
            q1.Run(calendar: true, today: today).ExitCode.Should().Be(0, "Q1's quarter is over, so it has no calendar days");
            FailureCounter.LoadBeside(q2.SidecarPath).ConsecutiveTotalFailures.Should().Be(day + 1, "Q1's success does not reset Q2's streak");
        }

        q2.Notifier.Notifications.Should().ContainSingle("the third failure in a row raises Q2's toast");
        FailureCounter.LoadBeside(q1.SidecarPath).ConsecutiveTotalFailures.Should().Be(0);
        Directory.GetFiles(q1.Folder, "*.tmp").Should().BeEmpty();
    }
}
