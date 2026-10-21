using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 4 refresh-3: an open notebook in the folder beside the sidecar file is that sidecar's own notebook whatever
/// local folder the sidecar records, so a local notebook moved to another folder with its sidecar (a new PC, a
/// reorganized folder) is rebuilt over its own sidecar, keeping the Time Blocks window, the plan options and the
/// confirmed inked values, as in round 2. A notebook of that name anywhere else is still stopped, and a sidecar that
/// records a URL (it stays in the folder its notebook left for OneDrive) never takes a notebook open beside it.
/// </summary>
public class MovedLocalNotebookTests
{
    private const string Url = "https://d.docs.live.net/0123abcd/Documents/SFP Q1 2027";

    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(RefreshHarness.Jan4);

    [Fact]
    public void A_notebook_moved_to_another_folder_with_its_sidecar_is_rebuilt_over_it_and_keeps_its_state()
    {
        (RefreshHarness h, Sidecar before) = Confirmed();
        string newFolder = TestPaths.NewTempDir("moved-local-new");
        string newPath = Path.Combine(newFolder, h.Name);
        Directory.CreateDirectory(newPath);
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, newPath);
        string newSidecar = h.Store.PathFor(newPath);
        File.Move(h.SidecarPath, newSidecar);

        RefreshResult result = h.Run(folder: newFolder);

        result.ExitCode.Should().Be(0, result.Error);
        result.Summary!.SidecarRebuilt.Should().BeTrue("the notebook got new IDs when it moved");
        Sidecar saved = h.Store.Load(newSidecar)!;
        saved.NotebookId.Should().Be(movedId);
        saved.NotebookPath.Should().Be(newPath);
        saved.TimeBlocks.Should().Be(new TimeBlockWindow(7, 19));
        saved.PlanOptions.Should().BeEquivalentTo(before.PlanOptions);
        saved.ConfirmedInked.Should().Equal(before.ConfirmedInked);
        PageXmlParser.Parse(h.App.GetPageContent(saved.Pages[MonJan4.Value].PageId, PageInfo.Basic)).Outlines
            .Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v2", "the confirmed inked objective stays in the legend");
        FailureCounter.LoadBeside(newSidecar).ConsecutiveTotalFailures.Should().Be(0);
        h.Notifier.Notifications.Should().BeEmpty();

        RefreshResult next = h.Run(folder: newFolder);

        next.ExitCode.Should().Be(0, next.Error);
        next.Summary!.SidecarRebuilt.Should().BeFalse("the next run finds the moved notebook by its ID");
        next.Summary.Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_notebook_of_that_name_that_is_not_beside_the_sidecar_is_still_stopped()
    {
        (RefreshHarness h, _) = Confirmed();
        string newFolder = TestPaths.NewTempDir("moved-local-sidecar");
        string elsewhere = Path.Combine(TestPaths.NewTempDir("moved-local-elsewhere"), h.Name);
        h.App.MoveNotebook(h.Notebook.NotebookId, elsewhere);
        string newSidecar = h.Store.PathFor(Path.Combine(newFolder, h.Name));
        File.Move(h.SidecarPath, newSidecar);
        string before = File.ReadAllText(newSidecar);

        RefreshResult result = h.Run(folder: newFolder);

        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain(elsewhere).And.Contain(h.Notebook.NotebookPath).And.Contain("no longer there");
        File.ReadAllText(newSidecar).Should().Be(before);
    }

    [Fact]
    public void A_sidecar_that_records_a_url_has_its_url_opened_rather_than_take_a_notebook_open_beside_it()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, Url);
        h.Store.Save(h.SidecarPath, h.Notebook.Sidecar with { NotebookPath = Url });
        h.App.Close(movedId);
        FakeNotebook copy = FakeNotebook.Generate(folder: h.Folder, app: h.App);
        HashSet<string> copyPages = copy.PageIds.Values.ToHashSet(StringComparer.Ordinal);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(folder: h.Folder);

        result.Success.Should().BeTrue(result.Error);
        h.App.Calls.Skip(calls).Should().Contain(("OpenHierarchy", Url + ":None"), "a copy left in the old folder beside the sidecar is not the notebook on OneDrive");
        h.SavedSidecar().NotebookId.Should().Be(movedId);
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" && copyPages.Contains(c.Detail), "the copy is not written");
    }

    /// <summary>
    /// A notebook generated with the out-of-quarter days and a 7 to 19 window, objective 1 typed and objective 3 inked
    /// "Launch v2" and confirmed by a run that writes inked values; returns the sidecar that run saved.
    /// </summary>
    private static (RefreshHarness H, Sidecar Before) Confirmed()
    {
        RefreshHarness h = new(shape: o => o with { IncludeOutOfQuarterDays = true }, window: new TimeBlockWindow(7, 19));
        h.Type(S.Objectives[0], "Ship the connector");
        h.Notebook.InkUserText(PageKey.Setup, S.Objectives[2], "Launch", "v2");
        RefreshResult confirmed = h.Run(writeInked: true);
        confirmed.Success.Should().BeTrue(confirmed.Error);
        Sidecar before = h.SavedSidecar();
        before.ConfirmedInked.Should().Equal(new Dictionary<string, string> { ["objective:3"] = "Launch v2" });
        before.PlanOptions!.IncludeOutOfQuarterDays.Should().BeTrue();
        before.TimeBlocks.Should().Be(new TimeBlockWindow(7, 19));
        return (h, before);
    }
}
