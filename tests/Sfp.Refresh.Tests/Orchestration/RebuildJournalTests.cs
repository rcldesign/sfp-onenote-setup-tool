using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-3: a sidecar rebuild keeps the journal a run left when it wrote Updates and could not record them (its
/// final save failed), so the next objective change is written instead of every legend being blocked as edited by the
/// user. The rebuild carries the journaled texts for the slots it re-finds, and a page it cannot read keeps its record.
/// </summary>
public class RebuildJournalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Updates_a_run_wrote_and_could_not_record_are_settled_even_when_the_next_run_rebuilds(bool rebuild)
    {
        RunHarness h = new();
        FlakySidecarStore store = WriteThenFailFinalSave(h);
        if (rebuild)
        {
            // The user deletes a Notes page: its recorded ID no longer resolves, so the next run rebuilds the sidecar.
            h.App.MoveToRecycleBin(NotesPageId(h));
        }

        RefreshResult settled = h.Run();

        settled.Success.Should().BeTrue(settled.Error);
        settled.Summary!.SidecarRebuilt.Should().Be(rebuild);
        settled.Summary.Changes.Should().BeEmpty("every legend already holds the text the failed run wrote");
        SidecarPage monday = h.Saved().Pages[RunHarness.MonJan4.Value];
        monday.AnchorTexts.Should().Contain("objectiveLegend", "O1 Ship the API", "the journaled text is recorded as the tool's");
        monday.PendingTexts.Should().BeNull("the page was read, so its journal is settled");
        store.Failed.Should().Be(3);
        h.RetypeSetup("Ship the API", "Ship the docs");

        RefreshResult after = h.Run();

        after.Success.Should().BeTrue(after.Error);
        after.Summary!.Changes.Should().HaveCount(107).And.OnlyContain(
            c => !c.Blocked && c.Kind == ChangeKind.Update,
            "the outlines hold the text the failed run journaled, so they are not taken for the user's edits");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the docs").Should().ContainSingle();
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the API").Should().BeEmpty();
    }

    [Fact]
    public void A_page_the_rebuild_cannot_read_keeps_its_record_and_journal_until_it_can_be_read()
    {
        RunHarness h = new();
        WriteThenFailFinalSave(h);
        string monday = h.Notebook.PageIds[RunHarness.MonJan4];
        Sidecar journaled = h.Saved();
        h.App.MoveToRecycleBin(NotesPageId(h));
        bool unreadable = true;
        h.App.GetPageContentFailure = id => unreadable && id == monday
            ? new OneNoteException("GetPageContent", OneNoteHResults.NotYetSynchronized, "The page has not been synchronized yet.", null)
            : null;

        RefreshResult rebuilt = h.Run();

        rebuilt.Success.Should().BeTrue(rebuilt.Error);
        rebuilt.Summary!.SidecarRebuilt.Should().BeTrue();
        Sidecar saved = h.Saved();
        saved.UnreadPages.Should().NotContain(monday, "the stale record still names the page");
        SidecarPage kept = saved.Pages[RunHarness.MonJan4.Value];
        kept.PageId.Should().Be(monday);
        kept.Anchors.Should().BeEquivalentTo(journaled.Pages[RunHarness.MonJan4.Value].Anchors);
        kept.PendingTexts.Should().Contain("objectiveLegend", "O1 Ship the API", "the page could not be read, so its journal is kept");
        unreadable = false;

        RefreshResult read = h.Run();

        read.Success.Should().BeTrue(read.Error);
        read.Summary!.SidecarRebuilt.Should().BeFalse();
        read.Summary.Changes.Should().BeEmpty();
        SidecarPage settled = h.Saved().Pages[RunHarness.MonJan4.Value];
        settled.AnchorTexts.Should().Contain("objectiveLegend", "O1 Ship the API", "the journal is settled once the page can be read");
        settled.PendingTexts.Should().BeNull();
        h.RetypeSetup("Ship the API", "Ship the docs");

        RefreshResult after = h.Run();

        after.Summary!.Changes.Should().HaveCount(107).And.OnlyContain(c => !c.Blocked && c.Kind == ChangeKind.Update);
    }

    /// <summary>
    /// Writes "Ship the connector" in every legend, then changes it to "Ship the API" in a run whose final save fails on
    /// every attempt, so the sidecar on disk holds the journal of the Updates and the older recorded texts.
    /// </summary>
    private static FlakySidecarStore WriteThenFailFinalSave(RunHarness h)
    {
        FlakySidecarStore store = new(h.Store);
        h.Services = h.Services with { Sidecars = store };
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        h.Run().Success.Should().BeTrue();
        h.RetypeSetup("Ship the connector", "Ship the API");
        store.FailNext(3, FlakySidecarStore.Final);

        RefreshResult failed = h.Run();

        failed.ExitCode.Should().Be(5, "the Updates are written, then every attempt at the final save fails");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the API").Should().ContainSingle();
        SidecarPage left = h.Saved().Pages[RunHarness.MonJan4.Value];
        left.AnchorTexts.Should().Contain("objectiveLegend", "O1 Ship the connector");
        left.PendingTexts.Should().Contain("objectiveLegend", "O1 Ship the API");
        return store;
    }

    private static string NotesPageId(RunHarness h) =>
        h.Notebook.PageIds.First(p => p.Key.Value.StartsWith("notes:", StringComparison.Ordinal)).Value;
}
