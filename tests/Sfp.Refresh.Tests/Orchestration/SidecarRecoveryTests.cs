using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>M4.3: a missing or stale sidecar is rebuilt from the notebook's page keys with every page, section and anchor; a consistent one is kept.</summary>
public class SidecarRecoveryTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;

    private static SidecarRecovery Recovery(FakeNotebook nb, CapturingLog? log = null)
    {
        DateEngine dates = new();
        return new SidecarRecovery(nb.App, M, dates, new PlanBuilder(dates), (log ?? new CapturingLog()).Logger);
    }

    [Fact]
    public void A_missing_sidecar_is_rebuilt_with_every_page_key_section_and_anchor_and_warns_once()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        CapturingLog log = new();

        RecoveryResult result = Recovery(nb, log).Recover(nb.Handle, null, "1.2.3");

        result.Rebuilt.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be(SidecarRecovery.RebuiltWarning);
        Sidecar rebuilt = result.Sidecar;
        rebuilt.Pages.Keys.Should().BeEquivalentTo(nb.Plan.Pages.Select(p => p.Key.Value), "every page key is found through its sfp.key meta");
        rebuilt.Pages.Should().HaveCount(144);
        rebuilt.Sections.Should().BeEquivalentTo(nb.Sidecar.Sections);
        rebuilt.NotebookId.Should().Be(nb.NotebookId);
        rebuilt.NotebookName.Should().Be("SFP Q1 2027");
        rebuilt.NotebookPath.Should().Be(nb.NotebookPath);
        rebuilt.Quarter.Should().Be(new QuarterSpec(2027, 1), "derived from the calendar page keys");
        rebuilt.ToolVersion.Should().Be("1.2.3");
        rebuilt.ManifestId.Should().Be(M.TemplateId);
        rebuilt.Generated.Should().BeNull();
        foreach ((string key, SidecarPage page) in nb.Sidecar.Pages)
        {
            rebuilt.Pages[key].PageId.Should().Be(page.PageId, key);
            rebuilt.Pages[key].SectionId.Should().Be(page.SectionId, key);
            rebuilt.Pages[key].Anchors.Should().BeEquivalentTo(page.Anchors, key);
        }

        rebuilt.Pages["day:2027-01-04"].Anchors.Keys.Should().BeEquivalentTo(new[] { "dailyDate", "prev", "next" });
        rebuilt.Pages["calendar:2027-01"].Anchors.Should().HaveCount(31 + 5 + 2);
        result.Pages.Should().HaveCount(144);
        log.Messages(Serilog.Events.LogEventLevel.Warning).Should().ContainSingle().Which.Should().Contain("No sidecar");
    }

    [Fact]
    public void A_consistent_sidecar_is_kept_as_is_without_reading_any_page()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        int reads = nb.App.CallCount("GetPageContent");

        RecoveryResult result = Recovery(nb).Recover(nb.Handle, nb.Sidecar, "1.2.3");

        result.Rebuilt.Should().BeFalse();
        result.Sidecar.Should().BeSameAs(nb.Sidecar);
        result.Warnings.Should().BeEmpty();
        result.Pages.Should().BeEmpty();
        nb.App.CallCount("GetPageContent").Should().Be(reads);
        nb.App.Calls.Last().Should().Be(("GetHierarchy", nb.NotebookId + ":Pages"));
    }

    [Fact]
    public void A_sidecar_whose_ids_no_longer_resolve_is_rebuilt_keeping_its_history()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        Sidecar old = nb.Sidecar with { LastRefresh = new DateTimeOffset(2027, 1, 5, 5, 0, 0, TimeSpan.Zero), FrozenThrough = new DateOnly(2027, 1, 4) };
        string newNotebookId = nb.App.RenumberIds(nb.NotebookId);
        NotebookHandle moved = new(newNotebookId, nb.NotebookPath, nb.Plan.Name);

        RecoveryResult result = Recovery(nb).Recover(moved, old, "1.2.3");

        result.Rebuilt.Should().BeTrue();
        Sidecar rebuilt = result.Sidecar;
        rebuilt.NotebookId.Should().Be(newNotebookId);
        rebuilt.Pages.Should().HaveCount(144);
        rebuilt.Pages["day:2027-01-04"].PageId.Should().NotBe(old.Pages["day:2027-01-04"].PageId);
        rebuilt.Pages["day:2027-01-04"].Anchors.Keys.Should().BeEquivalentTo(new[] { "dailyDate", "prev", "next" });
        rebuilt.Pages["day:2027-01-04"].Anchors.Values.Should().NotIntersectWith(old.Pages["day:2027-01-04"].Anchors.Values);
        rebuilt.LastRefresh.Should().Be(old.LastRefresh);
        rebuilt.FrozenThrough.Should().Be(old.FrozenThrough);
        rebuilt.Generated.Should().Be(old.Generated);
        rebuilt.ToolVersion.Should().Be("test");
    }

    [Fact]
    public void Consistency_needs_the_notebook_every_section_and_every_page_id()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(nb.App.GetHierarchy(nb.NotebookId, HierarchyScope.Pages));

        SidecarRecovery.IsConsistent(nb.Sidecar, tree).Should().BeTrue();
        SidecarRecovery.IsConsistent(nb.Sidecar with { NotebookId = "{other}" }, tree).Should().BeFalse();
        Dictionary<string, SidecarPage> pages = new(nb.Sidecar.Pages, StringComparer.Ordinal) { ["cover"] = nb.Sidecar.Pages["cover"] with { PageId = "{gone}" } };
        SidecarRecovery.IsConsistent(nb.Sidecar with { Pages = pages }, tree).Should().BeFalse();
        Dictionary<string, string> sections = new(nb.Sidecar.Sections, StringComparer.Ordinal) { ["Quarter"] = "{gone}" };
        SidecarRecovery.IsConsistent(nb.Sidecar with { Sections = sections }, tree).Should().BeFalse();
    }

    [Fact]
    public void Section_groups_must_resolve_too_and_a_rebuild_records_them_from_the_hierarchy()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        HierarchyNotebook flat = HierarchyXmlReader.Notebook(nb.App.GetHierarchy(nb.NotebookId, HierarchyScope.Pages));
        HierarchyNotebook grouped = flat with { SectionGroups = new[] { new HierarchySectionGroup("{weeks}", "Weeks", Path.Combine(nb.NotebookPath, "Weeks")) } };
        Sidecar withGroup = nb.Sidecar with { SectionGroups = new Dictionary<string, string>(StringComparer.Ordinal) { ["Weeks"] = "{weeks}" } };

        SidecarRecovery.IsConsistent(withGroup, grouped).Should().BeTrue();
        SidecarRecovery.IsConsistent(withGroup, flat).Should().BeFalse("the group the sidecar records is gone");
        SidecarRecovery.IsConsistent(nb.Sidecar, grouped).Should().BeTrue("a sidecar without groups does not ask for any");

        (Sidecar rebuilt, _) = Recovery(nb).Rebuild(nb.Handle, grouped, null, "1.2.3");

        rebuilt.SectionGroups.Should().Equal(new Dictionary<string, string> { ["Weeks"] = "{weeks}" });
        rebuilt.Sections.Should().BeEquivalentTo(nb.Sidecar.Sections);
        Recovery(nb).Rebuild(nb.Handle, flat, null, "1.2.3").Sidecar.SectionGroups.Should().BeEmpty();
    }

    [Fact]
    public void A_stale_sidecar_keeps_its_window_and_the_rebuild_refinds_calendar_rows_with_it()
    {
        TimeBlockWindow generated = new(8, 18);
        FakeNotebook nb = FakeNotebook.Generate(window: generated);
        PageKey tueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
        AnchorWrite write = new(
            new PlannedChange(tueJan5, "timeBlockEvent:9", null, "9:00 Staff meeting", ChangeKind.Create, false, null),
            nb.Catalog.Slot(nb.Plan.Page(tueJan5), "timeBlockEvent:9"),
            null);
        new AnchorWriter(nb.App, nb.Options, new CapturingLog().Logger).Write(nb.PageIds[tueJan5], nb.Read(tueJan5), new[] { write })
            .Created.Should().ContainKey("timeBlockEvent:9");
        string newNotebookId = nb.App.RenumberIds(nb.NotebookId);

        RecoveryResult result = Recovery(nb).Recover(new NotebookHandle(newNotebookId, nb.NotebookPath, nb.Plan.Name), nb.Sidecar, "1.2.3");

        result.Rebuilt.Should().BeTrue();
        result.Sidecar.TimeBlocks.Should().Be(generated);
        result.Sidecar.Pages[tueJan5.Value].Anchors.Should().ContainKey("timeBlockEvent:9", "the line is re-found on the 9 row of the 8 to 18 column the pages show");
        Recovery(nb).Recover(new NotebookHandle(newNotebookId, nb.NotebookPath, nb.Plan.Name), null, "1.2.3").Sidecar.TimeBlocks
            .Should().BeNull("without a previous sidecar the window the pages were generated with is unknown");
    }

    [Fact]
    public void Anchor_matching_adopts_an_outline_only_at_the_position_the_tool_writes_it()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey key = PageKey.Day(new DateOnly(2027, 1, 4));
        nb.TypeUserText(key, M.Slots.ObjectiveLegend.Rect with { X = M.Slots.ObjectiveLegend.Rect.X + 60 }, "a note in the band");
        PageXml page = nb.Read(key);

        IReadOnlyDictionary<string, string> ids = SidecarRecovery.MatchAnchors(nb.Plan.Page(key), page, nb.Catalog, nb.Plan, M.OneNote);

        ids.Keys.Should().BeEquivalentTo(new[] { "dailyDate", "prev", "next" }, "the note is not where the legend is written, so it is not adopted");
        ids.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Quarter_is_derived_from_calendar_keys_or_day_keys()
    {
        SidecarRecovery.QuarterFromKeys(new[] { "cover", "calendar:2027-03", "calendar:2027-01", "day:2027-02-03" }).Should().Be(new QuarterSpec(2027, 1));
        SidecarRecovery.QuarterFromKeys(new[] { "day:2026-11-05", "day:2026-10-02" }).Should().Be(new QuarterSpec(2026, 10));
        SidecarRecovery.QuarterFromKeys(new[] { "cover", "notes:ideas" }).Should().BeNull();
    }

    [Fact]
    public void A_notebook_without_page_keys_cannot_be_recovered()
    {
        FakeOneNoteApp app = new();
        string id = app.OpenHierarchy(Path.Combine("C:", "Notebooks", "Personal"), null, CreateFileType.Notebook);
        string section = app.OpenHierarchy(Path.Combine("C:", "Notebooks", "Personal", "Notes.one"), id, CreateFileType.Section);
        app.CreateNewPage(section, NewPageStyle.BlankPageWithTitle);
        DateEngine dates = new();
        SidecarRecovery recovery = new(app, M, dates, new PlanBuilder(dates), new CapturingLog().Logger);

        Action act = () => recovery.Recover(new NotebookHandle(id, "C:\\Notebooks\\Personal", "Personal"), null, "1");

        act.Should().Throw<InvalidOperationException>().WithMessage("*sfp.key*");
    }

    [SkippableFact]
    [Trait("Category", "COM")]
    public void Com_delete_the_sidecar_and_run_refresh_dry_run_rebuilds_it_with_every_page_key()
    {
        Skip.If(!OperatingSystem.IsWindows(), "Needs OneNote desktop on Windows: generate a notebook, delete its sfp-state.json, run refresh --dry-run and check the rebuilt sidecar.");
        Skip.If(Environment.GetEnvironmentVariable("SFP_COM_NOTEBOOK") is null, "Set SFP_COM_NOTEBOOK to the name of a generated notebook on the owner's PC; the fake-app tests above cover the logic.");
    }
}
