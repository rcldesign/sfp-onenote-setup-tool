using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>M4.3: opening a notebook by name through the sidecar, the open notebooks, or OpenHierarchy on its folder path.</summary>
public class NotebookOpenerTests
{
    private static NotebookOpener Opener(FakeOneNoteApp app, SidecarStore store, CapturingLog? log = null) =>
        new(app, store, (log ?? new CapturingLog()).Logger, TimeSpan.FromSeconds(4), _ => { });

    [Fact]
    public void An_open_notebook_is_found_by_name_and_its_sidecar_read_from_the_folder()
    {
        string folder = TestPaths.NewTempDir("opener");
        FakeNotebook nb = FakeNotebook.Generate(folder: folder);
        SidecarStore store = new();
        store.Save(store.PathFor(nb.NotebookPath), nb.Sidecar);

        OpenedNotebook? opened = Opener(nb.App, store).Open("SFP Q1 2027", folder, Array.Empty<string>());

        opened.Should().NotBeNull();
        opened!.Notebook.Should().Be(nb.Handle);
        opened.Sidecar.Should().BeEquivalentTo(nb.Sidecar);
        opened.SidecarPath.Should().Be(store.PathFor(nb.NotebookPath));
        nb.App.Calls.Should().NotContain(c => c.Call == "OpenHierarchy" && c.Detail.EndsWith(":None", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sidecar_beside_the_notebook_is_loaded_when_the_search_folders_do_not_hold_it()
    {
        string generatedInto = TestPaths.NewTempDir("opener-a");
        string searched = TestPaths.NewTempDir("opener-b");
        FakeNotebook nb = FakeNotebook.Generate(folder: generatedInto);
        SidecarStore store = new();
        store.Save(store.PathFor(nb.NotebookPath), nb.Sidecar);
        CapturingLog log = new();

        OpenedNotebook? opened = Opener(nb.App, store, log).Open("SFP Q1 2027", null, new[] { searched });

        opened.Should().NotBeNull();
        opened!.Sidecar.Should().BeEquivalentTo(nb.Sidecar, "the notebook's own folder is where its sidecar lives");
        opened.SidecarPath.Should().Be(store.PathFor(nb.NotebookPath));
        log.Messages(Serilog.Events.LogEventLevel.Information).Should().Contain(m => m.Contains("found beside the notebook", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_sidecar_the_open_notebook_still_resolves_and_the_sidecar_path_is_beside_its_folder()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        SidecarStore store = new();

        OpenedNotebook? opened = Opener(nb.App, store).Open("sfp q1 2027", null, new[] { TestPaths.NewTempDir("none") });

        opened.Should().NotBeNull();
        opened!.Sidecar.Should().BeNull();
        opened.Notebook.Id.Should().Be(nb.NotebookId);
        opened.SidecarPath.Should().Be(store.PathFor(nb.NotebookPath));
    }

    [Fact]
    public void A_closed_notebook_is_opened_from_the_sidecar_path_and_the_run_waits_for_it()
    {
        string folder = TestPaths.NewTempDir("closed");
        FakeNotebook nb = FakeNotebook.Generate(folder: folder);
        SidecarStore store = new();
        store.Save(store.PathFor(nb.NotebookPath), nb.Sidecar);
        nb.App.Close(nb.NotebookId);

        OpenedNotebook? opened = Opener(nb.App, store).Open("SFP Q1 2027", null, new[] { folder });

        opened.Should().NotBeNull();
        opened!.Notebook.Id.Should().Be(nb.NotebookId);
        nb.App.Calls.Should().Contain(("OpenHierarchy", nb.NotebookPath + ":None"));
    }

    [Fact]
    public void A_closed_notebook_is_opened_from_the_given_folder_when_there_is_no_sidecar()
    {
        string folder = TestPaths.NewTempDir("closed2");
        FakeNotebook nb = FakeNotebook.Generate(folder: folder);
        nb.App.Close(nb.NotebookId);

        OpenedNotebook? opened = Opener(nb.App, new SidecarStore()).Open("SFP Q1 2027", folder, Array.Empty<string>());

        opened.Should().NotBeNull();
        opened!.Sidecar.Should().BeNull();
        opened.Notebook.Path.Should().Be(nb.NotebookPath);
    }

    [Fact]
    public void A_notebook_that_does_not_open_within_60_s_is_reported_with_the_prd_message()
    {
        string folder = TestPaths.NewTempDir("slow");
        FakeNotebook nb = FakeNotebook.Generate(folder: folder);
        SidecarStore store = new();
        store.Save(store.PathFor(nb.NotebookPath), nb.Sidecar);
        nb.App.Close(nb.NotebookId);
        nb.App.GetHierarchyFailure = (id, scope) => id == nb.NotebookId && scope == HierarchyScope.Self
            ? new OneNoteException("GetHierarchy", OneNoteHResults.NotebookDoesNotExist, "The notebook is still opening.", null)
            : null;
        List<TimeSpan> waits = new();
        CapturingLog log = new();
        NotebookOpener opener = new(nb.App, store, log.Logger, openTimeout: null, sleep: waits.Add);

        Action act = () => opener.Open("SFP Q1 2027", null, new[] { folder });

        act.Should().Throw<OneNoteException>().WithMessage("Notebook 'SFP Q1 2027' did not open within 60 s.");
        NotebookOpener.DefaultOpenTimeout.Should().Be(TimeSpan.FromSeconds(60));
        waits.Should().HaveCount(30, "polled every 2 s up to the 60 s the PRD allows").And.AllSatisfy(w => w.Should().Be(TimeSpan.FromSeconds(2)));
        log.Messages(Serilog.Events.LogEventLevel.Information).Count(m => m.Contains("Waiting for notebook", StringComparison.Ordinal)).Should().Be(30);
        nb.App.Calls.Should().Contain(("OpenHierarchy", nb.NotebookPath + ":None"), "the closed notebook was opened from its sidecar path first");
    }

    [Fact]
    public void Nothing_is_returned_when_the_notebook_is_nowhere_and_unavailability_propagates()
    {
        FakeOneNoteApp app = new();
        CapturingLog log = new();

        Opener(app, new SidecarStore(), log).Open("SFP Q9 2099", null, new[] { TestPaths.NewTempDir("nowhere") }).Should().BeNull();
        Opener(app, new SidecarStore(), log).Open("SFP Q9 2099", TestPaths.NewTempDir("nowhere2"), Array.Empty<string>()).Should().BeNull("OpenHierarchy fails on a folder that holds no notebook");
        log.Messages(Serilog.Events.LogEventLevel.Warning).Should().HaveCount(2);

        app.Unavailable = new OneNoteUnavailableException();
        Action act = () => Opener(app, new SidecarStore()).Open("SFP Q1 2027", null, Array.Empty<string>());
        act.Should().Throw<OneNoteUnavailableException>();
    }
}
