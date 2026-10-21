using Sfp.Core.Options;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 3 refresh-1: when the real notebook is closed and its sidecar records a URL or a local folder that is gone, a
/// local notebook of the same name that happens to be open is never taken by name, so the real sidecar is never rebuilt
/// from it. A notebook that moved to OneDrive (its old folder gone, its own path a URL) still rebuilds over its own sidecar.
/// </summary>
public class MovedSameNameNotebookTests
{
    private const string Url = "https://d.docs.live.net/0123abcd/Documents/SFP Q1 2027";

    [Fact]
    public void A_sidecar_that_records_a_url_has_its_url_opened_and_never_takes_the_open_local_notebook_of_that_name()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("moved-url-trial"));
        RunHarness h = new(app: trial.App, window: new TimeBlockWindow(7, 19));
        h.Store.Save(h.SidecarPath, h.Notebook.Sidecar with { NotebookPath = Url });
        h.App.Close(h.Notebook.NotebookId);
        string before = File.ReadAllText(h.SidecarPath);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain(Url).And.Contain(trial.NotebookPath);
        File.ReadAllText(h.SidecarPath).Should().Be(before, "the sidecar of the notebook on OneDrive is not rebuilt from the local trial");
        h.App.Calls.Skip(calls).Should().Contain(("OpenHierarchy", Url + ":None"), "OneNote is asked to open the URL the sidecar records");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "GetPageContent", "the trial is neither read nor written");
    }

    [Fact]
    public void A_sidecar_whose_local_folder_is_gone_never_takes_the_open_local_notebook_of_that_name()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("moved-gone-trial"));
        RunHarness h = new(app: trial.App, window: new TimeBlockWindow(7, 19));
        Directory.Exists(h.Notebook.NotebookPath).Should().BeFalse("the fake keeps no notebook folder on disk, so the recorded folder is gone");
        h.App.Close(h.Notebook.NotebookId);
        string before = File.ReadAllText(h.SidecarPath);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain(h.Notebook.NotebookPath).And.Contain(trial.NotebookPath).And.Contain("no longer there").And.Contain(h.SidecarPath);
        File.ReadAllText(h.SidecarPath).Should().Be(before, "the sidecar is not rebuilt from the local trial");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "GetPageContent", "the trial is neither read nor written");
    }

    [Fact]
    public void A_notebook_moved_to_onedrive_whose_old_folder_is_gone_is_taken_by_name_and_rebuilt_over_its_own_sidecar()
    {
        RunHarness h = new(window: new TimeBlockWindow(7, 19));
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, Url);
        Directory.Exists(h.Notebook.NotebookPath).Should().BeFalse("the sidecar still records the old local folder, which is gone");

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeTrue();
        Sidecar saved = h.Saved();
        saved.NotebookId.Should().Be(movedId);
        saved.NotebookPath.Should().Be(Url);
        saved.TimeBlocks.Should().Be(new TimeBlockWindow(7, 19), "the rebuild keeps what the notebook's own sidecar recorded");
        LegendOnMonJan4(h, saved).Should().BeTrue();
    }

    [Fact]
    public void A_sidecar_that_records_a_url_opens_that_notebook_while_a_local_one_of_the_same_name_is_open()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("moved-open-trial"));
        RunHarness h = new(app: trial.App);
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, Url);
        h.Store.Save(h.SidecarPath, h.Notebook.Sidecar with { NotebookPath = Url });
        h.App.Close(movedId);
        HashSet<string> trialPages = trial.PageIds.Values.ToHashSet(StringComparer.Ordinal);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        h.App.Calls.Skip(calls).Should().Contain(("OpenHierarchy", Url + ":None"));
        result.Summary!.SidecarRebuilt.Should().BeTrue("the notebook got new IDs when it moved");
        Sidecar saved = h.Saved();
        saved.NotebookId.Should().Be(movedId, "the notebook opened from the URL its sidecar records is that sidecar's notebook");
        LegendOnMonJan4(h, saved).Should().BeTrue();
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" && trialPages.Contains(c.Detail), "the trial is not written");
    }

    private static bool LegendOnMonJan4(RunHarness h, Sidecar saved) =>
        PageXmlParser.Parse(h.App.GetPageContent(saved.Pages[RunHarness.MonJan4.Value].PageId, PageInfo.Basic)).Outlines
            .Any(o => o.PlainText == "O1 Ship the connector");
}
