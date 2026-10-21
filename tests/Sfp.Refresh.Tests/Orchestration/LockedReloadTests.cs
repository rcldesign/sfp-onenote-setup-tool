using System.Globalization;
using Sfp.OneNote;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-4 (orchestrator half): a run reads its sidecar again once it holds the lock beside it, so a run that
/// finished while this one was still opening the notebook is not undone by this run's save of the copy it read first.
/// </summary>
public class LockedReloadTests
{
    [Fact]
    public void A_run_that_finished_while_this_one_was_opening_the_notebook_is_seen_and_kept()
    {
        RunHarness h = new();
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        RefreshResult? a = null;
        bool fired = false;
        h.App.GetHierarchyFailure = (startId, _) =>
        {
            // Run B has read the sidecar and is in its open step (its first GetHierarchy): run A starts and ends meanwhile,
            // then the user adds a standing goal, which only B can write.
            if (!fired && startId is null)
            {
                fired = true;
                a = h.Run();
                h.Type(h.Setup.StandingGoals[0], "Walk 30 min");
            }

            return null;
        };

        RefreshResult b = h.Run();

        a.Should().NotBeNull();
        a!.Success.Should().BeTrue(a.Error);
        a.Summary!.ValuesWritten.Should().Be(107);
        b.Success.Should().BeTrue(b.Error);
        b.Summary!.Changes.Should().NotContain(c => c.Blocked, "B reads the sidecar again under the lock, so A's outlines are the tool's");
        b.Summary.Changes.Should().HaveCount(90).And.OnlyContain(c => c.SlotId == "standingGoal:1" && c.Kind == ChangeKind.Create);
        SidecarPage monday = h.Saved().Pages[RunHarness.MonJan4.Value];
        monday.Anchors.Should().ContainKeys(new[] { "objectiveLegend", "standingGoal:1" }, "the sidecar B saves holds A's records and its own");
        monday.AnchorTexts.Should().Contain("objectiveLegend", "O1 Ship the connector");
        monday.AnchorTexts.Should().Contain("standingGoal:1", "Walk 30 min");
        h.Run().Summary!.Changes.Should().BeEmpty("the next run finds every slot current and owned");
    }

    [Fact]
    public void A_sidecar_that_names_another_notebook_when_read_again_stops_the_run_and_is_left_as_it_is()
    {
        RunHarness h = new();
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        Sidecar other = h.Notebook.Sidecar with { NotebookId = "{other}{1}{B0}", NotebookPath = Path.Combine(h.Folder, "elsewhere", h.Notebook.Plan.Name) };
        bool fired = false;
        h.App.GetHierarchyFailure = (startId, _) =>
        {
            // While this run is in its open step, another run rebuilds the sidecar from another notebook of the same name.
            if (!fired && startId is null)
            {
                fired = true;
                h.Store.Save(h.SidecarPath, other);
            }

            return null;
        };
        int updates = h.App.CallCount("UpdatePageContent");

        RefreshResult result = h.Run();

        result.ExitCode.Should().Be(RefreshOrchestrator.NotFoundExitCode);
        result.Error.Should().Be(string.Format(CultureInfo.InvariantCulture, RefreshOrchestrator.SidecarChangedFormat, h.SidecarPath, other.NotebookPath, h.Notebook.Plan.Name, h.Notebook.NotebookPath));
        h.App.CallCount("UpdatePageContent").Should().Be(updates, "nothing is written");
        Sidecar left = h.Saved();
        left.NotebookId.Should().Be(other.NotebookId, "the sidecar is left as the other run saved it");
        left.NotebookPath.Should().Be(other.NotebookPath);
        left.LastRefresh.Should().Be(other.LastRefresh);
    }

    [Fact]
    public void A_sidecar_that_cannot_be_read_again_leaves_the_first_read_in_use()
    {
        RunHarness h = new();
        SecondLoadFails store = new(h.Store);
        h.Services = h.Services with { Sidecars = store };
        h.Type(h.Setup.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        store.Loads.Should().Be(2, "the first read, then the read under the lock");
        result.Summary!.SidecarRebuilt.Should().BeFalse("the first read is used, not a missing sidecar");
        result.Summary.ValuesWritten.Should().Be(107);
        h.Log.Events.Should().Contain(e => e.MessageTemplate.Text.StartsWith("Sidecar {Path} could not be read again", StringComparison.Ordinal));
    }

    /// <summary>The real store, whose second <see cref="Load"/> fails the way a file held by a sync client does.</summary>
    private sealed class SecondLoadFails : ISidecarStore
    {
        private readonly ISidecarStore inner;

        public SecondLoadFails(ISidecarStore inner)
        {
            this.inner = inner;
        }

        public int Loads { get; private set; }

        public string PathFor(string notebookFolder) => inner.PathFor(notebookFolder);

        public Sidecar? Load(string path) => ++Loads == 2 ? throw new IOException(FlakySidecarStore.Message) : inner.Load(path);

        public void Save(string path, Sidecar sidecar) => inner.Save(path, sidecar);
    }
}
