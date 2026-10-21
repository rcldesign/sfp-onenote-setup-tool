using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-9): the pages a sidecar rebuild cannot read (OneNote still downloading a moved notebook) are listed in
/// the sidecar, and the next run reads them, and any page the sidecar does not record, before planning, so they are not
/// dropped for good; the sidecar stays consistent and is completed, not rebuilt.
/// </summary>
public class UnreadPagesTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly string[] Unsynced = { "day:2027-01-04", "day:2027-01-05", "day:2027-01-06" };

    private static string SchemaPath => Path.Combine(TestPaths.RepoRoot, "tests", "Sfp.OneNote.Tests", "schema", "sfp-state.schema.json");

    [Fact]
    public void Pages_a_rebuild_could_not_read_are_listed_and_the_next_run_reads_and_completes_them()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Run().Success.Should().BeTrue();
        h.App.RenumberIds(h.Notebook.NotebookId);
        Dictionary<string, string> idsByKey = PageIdsByKey(h);
        FailingReads app = new(h.App) { Failing = Unsynced.Select(k => idsByKey[k]).ToHashSet(StringComparer.Ordinal) };
        h.Services = h.Services with { App = app, Writer = new AnchorWriter(app, h.Notebook.Options, h.Log.Logger) };

        RefreshResult moved = h.Run(today: RefreshHarness.Jan4.AddDays(1));

        moved.Success.Should().BeTrue(moved.Error);
        moved.Summary!.SidecarRebuilt.Should().BeTrue();
        moved.Summary.Warnings.Should().Contain(SidecarRecovery.UnreadWarning(3));
        Sidecar rebuilt = h.SavedSidecar();
        rebuilt.Pages.Should().HaveCount(141).And.NotContainKeys(Unsynced);
        rebuilt.UnreadPages.Should().BeEquivalentTo(app.Failing, "the rebuild lists the pages it could not read");
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), File.ReadAllText(h.SidecarPath));

        app.Failing.Clear();
        RefreshResult next = h.Run(today: RefreshHarness.Jan4.AddDays(2));

        next.Success.Should().BeTrue(next.Error);
        next.Summary!.SidecarRebuilt.Should().BeFalse("the sidecar is consistent; it is completed, not rebuilt");
        next.Summary.Warnings.Should().NotContain(w => w.Contains("could not be read", StringComparison.Ordinal));
        Sidecar completed = h.SavedSidecar();
        completed.Pages.Should().HaveCount(144);
        completed.UnreadPages.Should().BeEmpty();
        completed.Pages["day:2027-01-04"].PageId.Should().Be(idsByKey["day:2027-01-04"]);
        completed.Pages["day:2027-01-04"].Anchors.Keys.Should().Contain(new[] { "dailyDate", "prev", "next", "objectiveLegend" }, "its link anchors and the legend written before the move are re-found");
        next.Summary.Changes.Should().NotContain(c => Unsynced.Contains(c.Page.Value) && c.Blocked);
        h.Run(today: RefreshHarness.Jan4.AddDays(3)).Summary!.Changes.Should().BeEmpty("every page is current and recorded");
    }

    [Fact]
    public void A_page_that_still_cannot_be_read_stays_listed_and_a_consistent_sidecar_needs_no_rebuild_for_it()
    {
        RefreshHarness h = new();
        string monday = h.Notebook.PageIds[PageKey.Day(new DateOnly(2027, 1, 4))];
        Dictionary<string, SidecarPage> pages = new(h.Notebook.Sidecar.Pages, StringComparer.Ordinal);
        pages.Remove("day:2027-01-04").Should().BeTrue();
        h.Store.Save(h.SidecarPath, h.Notebook.Sidecar with { Pages = pages, UnreadPages = new[] { monday, "{gone}" } });
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(h.App.GetHierarchy(h.Notebook.NotebookId, HierarchyScope.Pages));
        SidecarRecovery.IsConsistent(h.SavedSidecar(), tree).Should().BeTrue("unread page IDs need not resolve");
        FailingReads app = new(h.App) { Failing = new HashSet<string>(StringComparer.Ordinal) { monday } };
        h.Services = h.Services with { App = app, Writer = new AnchorWriter(app, h.Notebook.Options, h.Log.Logger) };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.Summary!.SidecarRebuilt.Should().BeFalse();
        result.Summary.Warnings.Should().Contain(SidecarRecovery.UnreadWarning(1));
        h.SavedSidecar().UnreadPages.Should().Equal(new[] { monday }, "a page still unreadable stays listed; one gone from the notebook is dropped");
        result.Summary.Changes.Single(c => c.Page.Value == "day:2027-01-04").Reason.Should().Be(Sfp.Refresh.Propagation.Propagator.UnreadablePageReason);
    }

    private static Dictionary<string, string> PageIdsByKey(RefreshHarness h)
    {
        string notebookId = HierarchyXmlReader.Notebooks(h.App.GetHierarchy(null, HierarchyScope.Notebooks)).Single(n => n.Name == h.Name).Id;
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(h.App.GetHierarchy(notebookId, HierarchyScope.Pages));
        return tree.Pages
            .Select(p => (Key: Sfp.OneNote.Xml.PageXmlParser.Parse(h.App.GetPageContent(p.Page.Id, PageInfo.Basic)).SfpKey, p.Page.Id))
            .Where(p => p.Key is not null)
            .ToDictionary(p => p.Key!, p => p.Id, StringComparer.Ordinal);
    }

    /// <summary>The fake OneNote, where GetPageContent answers hrNotYetSynchronized for the pages listed.</summary>
    private sealed class FailingReads : IOneNoteApp
    {
        private readonly FakeOneNoteApp inner;

        public FailingReads(FakeOneNoteApp inner)
        {
            this.inner = inner;
        }

        public HashSet<string> Failing { get; init; } = new(StringComparer.Ordinal);

        public string Version => inner.Version;

        public string GetHierarchy(string? startId, HierarchyScope scope) => inner.GetHierarchy(startId, scope);

        public string OpenHierarchy(string path, string? relativeToId, CreateFileType createFileType) => inner.OpenHierarchy(path, relativeToId, createFileType);

        public string CreateNewPage(string sectionId, NewPageStyle pageStyle) => inner.CreateNewPage(sectionId, pageStyle);

        public void UpdateHierarchy(string xml) => inner.UpdateHierarchy(xml);

        public void UpdatePageContent(string xml, DateTime dateExpectedLastModified) => inner.UpdatePageContent(xml, dateExpectedLastModified);

        public string GetPageContent(string pageId, PageInfo pageInfo) => Failing.Contains(pageId)
            ? throw new OneNoteException("GetPageContent", OneNoteHResults.NotYetSynchronized, "The page has not been synchronized yet.", null)
            : inner.GetPageContent(pageId, pageInfo);

        public string GetHyperlinkToObject(string id, string? pageObjectId) => inner.GetHyperlinkToObject(id, pageObjectId);

        public void SyncHierarchy(string id) => inner.SyncHierarchy(id);

        public void NavigateTo(string id) => inner.NavigateTo(id);

        public void DeleteHierarchy(string id, bool deletePermanently) => inner.DeleteHierarchy(id, deletePermanently);

        public void DeletePageContent(string pageId, string objectId, DateTime dateExpectedLastModified) => inner.DeletePageContent(pageId, objectId, dateExpectedLastModified);

        public void CloseNotebook(string id) => inner.CloseNotebook(id);

        public void Dispose() => inner.Dispose();
    }
}
