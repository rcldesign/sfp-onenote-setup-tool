using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-7): a run whose every page write OneNote refuses as a conflict (hrLastModifiedDateDidNotMatch, the
/// risk that it reads the passed stamp differently) fails with exit 5, names the count and the stamp, and counts toward
/// the failure streak and its toast; a run in which only some pages conflict still succeeds.
/// </summary>
public class ConflictRunTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;

    private static OneNoteException Refused() =>
        new("UpdatePageContent", OneNoteHResults.LastModifiedDateDidNotMatch, "The page was modified since it was read.", null);

    [Fact]
    public void A_run_whose_every_page_write_is_refused_as_a_conflict_fails_and_builds_the_streak()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.App.UpdatePageContentFailure = _ => Refused();

        for (int day = 1; day <= 3; day++)
        {
            RefreshResult result = h.Run(today: RefreshHarness.Jan4.AddDays(day - 1));

            result.Success.Should().BeFalse();
            result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
            result.Error.Should().Be(string.Format(System.Globalization.CultureInfo.InvariantCulture, RefreshOrchestrator.AllWritesConflictedFormat, 107))
                .And.Contain("dateExpectedLastModified");
            result.Summary!.Changes.Should().HaveCount(107).And.AllSatisfy(c => c.Reason.Should().Be(RefreshOrchestrator.ConflictReason));
            result.Summary.ValuesWritten.Should().Be(0);
            FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(day, "a run that wrote nothing is not a success");
            h.Notifier.Notifications.Should().HaveCount(day < 3 ? 0 : 1, "the toast comes with the third such run");
        }

        h.Notifier.Notifications.Single().Title.Should().Be(RefreshOrchestrator.RefreshFailureToastTitle);
        h.Notifier.Notifications.Single().Message.Should().Contain("refused every page write");
        h.App.CallCount("SyncHierarchy").Should().Be(0);
    }

    [Fact]
    public void A_run_in_which_only_some_page_writes_conflict_still_succeeds()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string monday = h.Notebook.PageIds[PageKey.Day(new DateOnly(2027, 1, 4))];
        h.App.UpdatePageContentFailure = id => id == monday ? Refused() : null;

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        result.Summary!.Changes.Where(c => c.Reason == RefreshOrchestrator.ConflictReason).Should().ContainSingle();
        result.Summary.ValuesWritten.Should().Be(106);
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(0);
    }
}
