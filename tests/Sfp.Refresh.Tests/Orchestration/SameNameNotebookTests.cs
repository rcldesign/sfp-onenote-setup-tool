using Sfp.OneNote;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 refresh-1: with two notebooks of the same name (a trial generated into folder A, the real one into folder B),
/// Refresh opens the one the sidecar describes and never rebuilds a sidecar from the other one.
/// </summary>
public class SameNameNotebookTests
{
    private const string Name = "SFP Q1 2027";

    private static NotebookOpener Opener(FakeOneNoteApp app) => new(app, new SidecarStore(), new CapturingLog().Logger, TimeSpan.FromSeconds(4), _ => { });

    /// <summary>The trial is generated first, so OneNote lists it first; the sidecars are saved beside each when asked.</summary>
    private static (FakeNotebook Trial, FakeNotebook Real) TwoOfTheSameName(bool saveSidecars = true)
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("same-a"));
        FakeNotebook real = FakeNotebook.Generate(folder: TestPaths.NewTempDir("same-b"), app: trial.App);
        if (saveSidecars)
        {
            SidecarStore store = new();
            store.Save(store.PathFor(trial.NotebookPath), trial.Sidecar);
            store.Save(store.PathFor(real.NotebookPath), real.Sidecar);
        }

        return (trial, real);
    }

    [Fact]
    public void The_notebook_the_sidecar_names_is_opened_although_another_of_the_same_name_is_listed_first()
    {
        (FakeNotebook trial, FakeNotebook real) = TwoOfTheSameName();

        OpenedNotebook? opened = Opener(real.App).Open(Name, real.Folder, Array.Empty<string>());

        opened!.Notebook.Id.Should().Be(real.NotebookId).And.NotBe(trial.NotebookId);
        opened.Sidecar!.NotebookId.Should().Be(real.NotebookId);
        opened.SidecarPath.Should().Be(new SidecarStore().PathFor(real.NotebookPath));
    }

    [Fact]
    public void A_closed_notebook_is_opened_from_its_folder_instead_of_taking_the_open_one_of_the_same_name()
    {
        (_, FakeNotebook real) = TwoOfTheSameName();
        Directory.CreateDirectory(real.NotebookPath);
        real.App.Close(real.NotebookId);

        OpenedNotebook? opened = Opener(real.App).Open(Name, real.Folder, Array.Empty<string>());

        opened!.Notebook.Id.Should().Be(real.NotebookId);
        real.App.Calls.Should().Contain(("OpenHierarchy", real.NotebookPath + ":None"));
    }

    [Fact]
    public void Without_a_sidecar_two_open_notebooks_of_the_same_name_need_the_folder()
    {
        (FakeNotebook trial, FakeNotebook real) = TwoOfTheSameName(saveSidecars: false);
        NotebookOpener opener = Opener(real.App);

        opener.Open(Name, null, new[] { TestPaths.NewTempDir("same-none") }).Should().BeNull();
        opener.Problem.Should().Contain("Several notebooks named 'SFP Q1 2027'").And.Contain(trial.NotebookPath).And.Contain(real.NotebookPath).And.Contain("--folder");

        opener.Open(Name, real.Folder, Array.Empty<string>())!.Notebook.Id.Should().Be(real.NotebookId);
        opener.Problem.Should().BeNull();
    }

    [Fact]
    public void Refresh_with_the_real_notebooks_folder_writes_into_it_and_keeps_its_sidecar()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("same-trial"));
        RunHarness h = new(app: trial.App);
        h.Type(h.Setup.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeFalse();
        h.Saved().NotebookId.Should().Be(h.Notebook.NotebookId);
        h.OutlinesWithText(RunHarness.MonJan4, "O1 Ship the connector").Should().ContainSingle();
        trial.Read(RunHarness.MonJan4).Outlines.Should().NotContain(o => o.PlainText == "O1 Ship the connector");
    }

    [Fact]
    public void Refresh_stops_with_exit_2_and_leaves_the_sidecar_when_its_notebook_cannot_be_opened()
    {
        FakeNotebook trial = FakeNotebook.Generate(folder: TestPaths.NewTempDir("same-trial2"));
        RunHarness h = new(app: trial.App);
        Directory.CreateDirectory(h.Notebook.NotebookPath);
        string before = File.ReadAllText(h.SidecarPath);
        h.App.DeleteHierarchy(h.Notebook.NotebookId, false);
        int calls = h.App.Calls.Count;

        RefreshResult result = h.Run(RunHarness.Request(folder: h.Folder));

        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain(h.Notebook.NotebookPath).And.Contain(trial.NotebookPath);
        File.ReadAllText(h.SidecarPath).Should().Be(before, "the sidecar of the notebook that could not be opened is not rebuilt from the other one");
        h.App.Calls.Skip(calls).Should().NotContain(c => c.Call == "UpdatePageContent" || c.Call == "GetPageContent", "the other notebook is neither read nor written");
    }
}
