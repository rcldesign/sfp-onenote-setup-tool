using Sfp.Core.Options;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 4 refresh-1: a notebook moved to OneDrive whose sidecar still records its old local folder, with that folder
/// still on disk. When the folder holds no notebook file (no .onetoc2 and no .one anywhere inside), the lone open
/// notebook of that name at a URL is the one moved and is rebuilt over its own sidecar, as in round 2. When the folder
/// still holds a notebook, the run stops with exit 2 naming both paths, leaves the sidecar as it is and never opens the
/// stale copy, and the stop counts toward the failure streak.
/// </summary>
public class MovedNotebookOldFolderTests
{
    private const string Url = "https://d.docs.live.net/0123abcd/Documents/SFP Q1 2027";

    [Theory]
    [InlineData("Open Notebook.onetoc2")]
    [InlineData("Quick Notes.one")]
    [InlineData("Projects/Notes.one")]
    public void An_old_folder_that_still_holds_a_notebook_stops_the_run_naming_both_paths_and_never_opens_the_copy(string file)
    {
        RunHarness h = new(window: new TimeBlockWindow(7, 19));
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        string oldPath = h.Notebook.NotebookPath;
        h.App.MoveNotebook(h.Notebook.NotebookId, Url);

        // The copy left behind: closed in OneNote, which could open it from the old folder, and its files on disk.
        FakeNotebook copy = FakeNotebook.Generate(folder: h.Folder, app: h.App, window: new TimeBlockWindow(7, 19));
        h.App.Close(copy.NotebookId);
        string notebookFile = Path.Combine(oldPath, file);
        Directory.CreateDirectory(Path.GetDirectoryName(notebookFile)!);
        File.WriteAllText(notebookFile, "");
        string before = File.ReadAllText(h.SidecarPath);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain(oldPath).And.Contain(Url).And.Contain(h.SidecarPath).And.Contain("still holds notebook files");
        File.ReadAllText(h.SidecarPath).Should().Be(before, "the sidecar is neither rebuilt from the copy nor from the notebook at the URL");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "OpenHierarchy", "the stale copy is not opened");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "GetPageContent", "neither notebook is read or written");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1, "a stop by the opener counts toward the streak");
    }

    [Fact]
    public void An_old_folder_left_empty_takes_the_notebook_at_the_url_and_rebuilds_its_own_sidecar()
    {
        RunHarness h = new(window: new TimeBlockWindow(7, 19));
        h.Type(h.Setup.Objectives[0], "Ship the connector");
        string oldPath = h.Notebook.NotebookPath;
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, Url);
        Directory.CreateDirectory(oldPath);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeTrue("the notebook got new IDs when it moved");
        Sidecar saved = h.Saved();
        saved.NotebookId.Should().Be(movedId);
        saved.NotebookPath.Should().Be(Url);
        saved.TimeBlocks.Should().Be(new TimeBlockWindow(7, 19), "the rebuild keeps what the notebook's own sidecar recorded");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "OpenHierarchy", "the empty old folder is not opened");
        PageXmlParser.Parse(h.App.GetPageContent(saved.Pages[RunHarness.MonJan4.Value].PageId, PageInfo.Basic)).Outlines
            .Should().Contain(o => o.PlainText == "O1 Ship the connector");
        h.Run(RunHarness.Request(folder: h.Folder)).Summary!.SidecarRebuilt.Should().BeFalse("the next run finds the moved notebook by its ID");
    }

    [Fact]
    public void An_old_folder_holding_only_other_files_counts_as_empty()
    {
        RunHarness h = new();
        string oldPath = h.Notebook.NotebookPath;
        string movedId = h.App.MoveNotebook(h.Notebook.NotebookId, Url);
        Directory.CreateDirectory(Path.Combine(oldPath, "attachments"));
        File.WriteAllText(Path.Combine(oldPath, "desktop.ini"), "");
        File.WriteAllText(Path.Combine(oldPath, "attachments", "notes.onepkg"), "");

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        h.Saved().NotebookId.Should().Be(movedId);
    }
}
