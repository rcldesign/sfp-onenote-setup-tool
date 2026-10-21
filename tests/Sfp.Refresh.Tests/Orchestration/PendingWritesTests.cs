using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 refresh-4 (a) and (b): what a run wrote and could not record (a failed re-read after the write, a failed
/// sidecar save) is taken back by the next run from the journal in the sidecar, by position, signature and text, instead
/// of blocking the tool's own slots as user objects.
/// </summary>
public class PendingWritesTests
{
    [Fact]
    public void Outlines_whose_ids_were_not_learned_because_the_re_read_failed_are_taken_back_next_run()
    {
        RunHarness h = new();
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        string monId = h.Notebook.PageIds[RunHarness.MonJan4];
        int updates = h.App.UpdateCount(monId);
        bool failed = false;
        h.App.GetPageContentFailure = id =>
        {
            if (failed || id != monId || h.App.UpdateCount(id) == updates)
            {
                return null;
            }

            failed = true;
            return new OneNoteException("GetPageContent", OneNoteHResults.RpcServerCallRetryLater, "OneNote is busy.", null);
        };

        RefreshResult first = h.Run();

        first.ExitCode.Should().Be(5, first.Error);
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().ContainSingle("OneNote applied the write before the re-read failed");
        SidecarPage record = h.Saved().Pages[RunHarness.MonJan4.Value];
        record.Anchors.Should().NotContainKey("objectiveLegend");
        record.PendingTexts.Should().Contain("objectiveLegend", "O1 Ship the connector");

        RefreshResult second = h.Run();

        second.Success.Should().BeTrue(second.Error);
        second.Summary!.Changes.Should().NotContain(c => c.Blocked);
        second.Summary.Changes.Should().NotContain(c => c.Page == RunHarness.MonJan4, "the legend is in place and was taken back");
        string legend = h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().ContainSingle().Which.ObjectId;
        SidecarPage settled = h.Saved().Pages[RunHarness.MonJan4.Value];
        settled.Anchors["objectiveLegend"].Should().Be(legend);
        settled.AnchorTexts["objectiveLegend"].Should().Be("O1 Ship the connector");
        settled.PendingTexts.Should().BeNull();
    }

    [Fact]
    public void A_run_whose_final_save_failed_leaves_its_creates_and_updates_to_the_next_run()
    {
        RunHarness h = new();
        FlakyStore store = new(h.Store) { FailSave = 2 };
        h.Services = h.Services with { Sidecars = store };
        h.Type(h.Setup.Objectives[0], "Ship the connector");

        h.Run().ExitCode.Should().Be(5, "the journal is saved, every page is written, then the final save fails");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().ContainSingle();
        h.Saved().Pages[RunHarness.MonJan4.Value].Anchors.Should().NotContainKey("objectiveLegend");

        RefreshResult next = h.Run();

        next.Success.Should().BeTrue(next.Error);
        next.Summary!.Changes.Should().BeEmpty("every outline the failed run wrote is taken back and already holds its text");
        h.Saved().Pages[RunHarness.MonJan4.Value].Anchors.Should().ContainKey("objectiveLegend");

        h.RetypeSetup("Ship the connector", "Ship the API");
        store.FailSave = store.Saves + 2;
        h.Run().ExitCode.Should().Be(5, "the updates are written, then the final save fails again");
        h.RetypeSetup("Ship the API", "Ship the docs");

        RefreshResult after = h.Run();

        after.Success.Should().BeTrue(after.Error);
        after.Summary!.Changes.Should().NotBeEmpty().And.OnlyContain(
            c => !c.Blocked && c.Kind == ChangeKind.Update,
            "the outlines hold the text the failed run journaled, so they are not taken for the user's edits");
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the docs").Should().ContainSingle();
    }

    [Fact]
    public void A_note_holding_the_journaled_text_without_the_tools_signature_is_not_taken_back()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string note = nb.AddOutlineAt(RunHarness.MonJan4, nb.Manifest.Slots.ObjectiveLegend.Rect, "O1 Ship the connector", fixedWidth: false);
        Sidecar journaled = nb.Sidecar with
        {
            Pages = nb.Sidecar.Pages.ToDictionary(
                p => p.Key,
                p => p.Key == RunHarness.MonJan4.Value ? p.Value with { PendingTexts = new Dictionary<string, string> { ["objectiveLegend"] = "O1 Ship the connector" } } : p.Value),
        };

        Sidecar settled = PendingWrites.Resolve(journaled, nb.Plan, nb.Catalog, nb.Read, new CapturingLog().Logger);

        settled.Pages[RunHarness.MonJan4.Value].Anchors.Values.Should().NotContain(note);
        settled.Pages[RunHarness.MonJan4.Value].PendingTexts.Should().BeNull("the page was read, so its journal is settled");
    }

    /// <summary>A sidecar store whose n-th save (counted from 1) fails the way a file held by a sync client or a scanner does.</summary>
    private sealed class FlakyStore : ISidecarStore
    {
        private readonly ISidecarStore inner;

        public FlakyStore(ISidecarStore inner)
        {
            this.inner = inner;
        }

        public int Saves { get; private set; }

        public int FailSave { get; set; }

        public string PathFor(string notebookFolder) => inner.PathFor(notebookFolder);

        public Sidecar? Load(string path) => inner.Load(path);

        public void Save(string path, Sidecar sidecar)
        {
            Saves++;
            if (Saves == FailSave)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            inner.Save(path, sidecar);
        }
    }
}
