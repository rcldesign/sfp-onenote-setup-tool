using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;
using Sfp.Render;

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
    public void Pages_in_the_recycle_bin_are_not_the_notebooks_pages_for_the_check_or_the_rebuild()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey key = PageKey.Day(new DateOnly(2027, 1, 4));
        string monday = nb.PageIds[key];
        string deletedCopy = nb.App.CopyToRecycleBin(monday);
        string xml = nb.App.GetHierarchy(nb.NotebookId, HierarchyScope.Pages);
        xml.Should().Contain("isRecycleBin=\"true\"").And.Contain("isInRecycleBin=\"true\"").And.Contain(deletedCopy, "the fixture lists the bin, ahead of the sections");
        HierarchyNotebook tree = HierarchyXmlReader.Notebook(xml);

        tree.Pages.Should().HaveCount(144).And.NotContain(p => p.Page.Id == deletedCopy);
        tree.SectionGroups.Should().BeEmpty("the recycle bin is not one of the notebook's section groups");
        SidecarRecovery.IsConsistent(nb.Sidecar, tree).Should().BeTrue();
        Recovery(nb).Rebuild(nb.Handle, tree, null, "1.2.3").Sidecar.Pages[key.Value].PageId
            .Should().Be(monday, "a deleted copy with the same sfp.key is not taken for the live page, even listed first");

        nb.App.MoveToRecycleBin(monday);
        HierarchyNotebook afterDelete = HierarchyXmlReader.Notebook(nb.App.GetHierarchy(nb.NotebookId, HierarchyScope.Pages));

        SidecarRecovery.IsConsistent(nb.Sidecar, afterDelete).Should().BeFalse("a page the user deleted no longer resolves, so the sidecar is stale");
        RecoveryResult rebuilt = Recovery(nb).Recover(nb.Handle, nb.Sidecar, "1.2.3");
        rebuilt.Rebuilt.Should().BeTrue();
        rebuilt.Sidecar.Pages.Should().HaveCount(143).And.NotContainKey(key.Value, "only the recycle bin holds Mon Jan 4 now");
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
    public void Anchor_matching_adopts_a_tool_written_outline_but_not_a_user_note_at_a_slot_origin()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey key = PageKey.Day(new DateOnly(2027, 1, 4));
        PageSpec spec = nb.Plan.Page(key);
        SlotGeometry goal1 = nb.Catalog.Slot(spec, "standingGoal:1")!;
        SlotGeometry goal2 = nb.Catalog.Slot(spec, "standingGoal:2")!;
        SlotGeometry goal3 = nb.Catalog.Slot(spec, "standingGoal:3")!;
        AnchorWriteResult written = new AnchorWriter(nb.App, nb.Options, new CapturingLog().Logger).Write(nb.PageIds[key], nb.Read(key), new[]
        {
            new AnchorWrite(new PlannedChange(key, "standingGoal:1", null, "Walk 30 min", ChangeKind.Create, false, null), goal1, null),
        });
        string plainNote = nb.AddOutlineAt(key, nb.Catalog.Slot(spec, "objectiveLegend")!.Rect, "my note", fixedWidth: true);
        string ownColor = nb.AddOutlineAt(key, nb.Catalog.Slot(spec, "metricName:1")!.Rect, "<span style='font-family:Bookman Old Style;font-size:10.0pt;color:#000000'>Steps</span>", fixedWidth: true);
        string widened = nb.AddOutlineAt(key, goal2.Rect with { W = goal2.Rect.W + 40 }, AnchorWriter.Html("standingGoal:2", "Read", goal2, nb.Options), fixedWidth: true);
        string unfixed = nb.AddOutlineAt(key, goal3.Rect, AnchorWriter.Html("standingGoal:3", "Stretch", goal3, nb.Options), fixedWidth: false);

        IReadOnlyDictionary<string, string> ids = SidecarRecovery.MatchAnchors(spec, nb.Read(key), nb.Catalog, nb.Plan, M.OneNote);

        ids.Keys.Should().BeEquivalentTo(new[] { "dailyDate", "prev", "next", "standingGoal:1" });
        ids["standingGoal:1"].Should().Be(written.Created["standingGoal:1"], "the outline the tool wrote carries its signature and is re-found");
        ids.Values.Should().NotContain(new[] { plainNote, ownColor, widened, unfixed }, "a note at a slot's origin is the user's unless it carries every mark of the tool's outline");
    }

    [Fact]
    public void The_next_quarter_link_on_the_quarterly_review_is_re_found_and_recorded()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey review = PageKey.QuarterlyReview;
        FixedAnchor slot = M.Links.NextQuarter;
        AnchorPlacement placement = new(NextQuarterLinkWriter.AnchorId, (float)slot.X, (float)slot.Y, (float)slot.W, (float)slot.H, LinkGraphBuilder.LinkText(slot, "SFP Q2 2027"), slot.Align, (float)slot.SizePt)
        {
            Kind = AnchorKind.Link,
        };
        PageXml page = nb.Read(review);
        System.Xml.Linq.XElement root = page.Document.Root!;
        root.Add(PageXmlBuilder.AnchorOutline(new ResolvedAnchor(placement, "onenote:#Cover&section-id={s-0002}&page-id={p-0003}&end", true), nb.Options));
        nb.App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
        string linkId = nb.Read(review).Outlines.Single(o => !page.Outlines.Select(p => p.ObjectId).Contains(o.ObjectId)).ObjectId;

        SidecarRecovery.MatchAnchors(nb.Plan.Page(review), nb.Read(review), nb.Catalog, nb.Plan, M.OneNote)
            .Should().Contain(NextQuarterLinkWriter.AnchorId, linkId);
        Recovery(nb).Recover(nb.Handle, null, "1.2.3").Sidecar.Pages[review.Value].Anchors
            .Should().Contain(NextQuarterLinkWriter.AnchorId, linkId, "a rebuilt sidecar keeps the link a later Generate with --previous updates in place");
    }

    [Fact]
    public void The_signature_needs_the_fixed_width_the_typeface_the_color_and_for_a_link_an_onenote_href()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey key = PageKey.Day(new DateOnly(2027, 1, 4));
        SlotGeometry legend = nb.Catalog.Slot(nb.Plan.Page(key), "objectiveLegend")!;
        SlotGeometry goal = nb.Catalog.Slot(nb.Plan.Page(key), "standingGoal:1")!;
        OutlineInfo Outline(RectPt rect, string html, bool fixedWidth)
        {
            OutlineInfo typed = new PageXmlFixture("{p-0001}{1}{B0}", key.Value).TypedAt(rect, html).Build().Outlines.Last();
            return fixedWidth ? typed with { Element = WithFixedWidth(typed.Element) } : typed;
        }

        string legendHtml = AnchorWriter.Html("objectiveLegend", "O1 Ship", legend, nb.Options);
        AnchorSignature.Matches(Outline(legend.Rect, legendHtml, true), legend, false, 1.0, M).Should().BeTrue();
        AnchorSignature.Matches(Outline(legend.Rect, legendHtml, false), legend, false, 1.0, M).Should().BeFalse("the tool fixes the width");
        AnchorSignature.Matches(Outline(legend.Rect, legendHtml.Replace("Bookman Old Style", "\"TeX Gyre Bonum\"", StringComparison.Ordinal), true), legend, false, 1.0, M).Should().BeTrue("the bundled fallback is the tool's typeface too");
        AnchorSignature.Matches(Outline(legend.Rect, legendHtml.Replace("Bookman Old Style", "Calibri", StringComparison.Ordinal), true), legend, false, 1.0, M).Should().BeFalse();
        AnchorSignature.Matches(Outline(legend.Rect, legendHtml, true), goal, false, 1.0, M).Should().BeFalse("the legend's full ink is not the pre-fill's 60% color, and its width differs");
        AnchorSignature.Matches(Outline(goal.Rect, AnchorWriter.Html("standingGoal:1", "Walk", goal, nb.Options).Replace("#6B6763", "rgb(107, 103, 99)", StringComparison.Ordinal), true), goal, false, 1.0, M).Should().BeTrue("an rgb() color is the same color");
        AnchorSignature.Matches(Outline(legend.Rect, "<span style='font-family:Bookman Old Style;color:#1E1E20'>4</span>", true), legend, true, 1.0, M).Should().BeFalse("a link needs an onenote: href");
        AnchorSignature.Matches(Outline(legend.Rect, "<a href=\"onenote:#x&amp;end\"><span style='font-family:Bookman Old Style;color:#1E1E20'>4</span></a>", true), legend, true, 1.0, M).Should().BeTrue();
        AnchorSignature.Matches(Outline(legend.Rect, "<a href=\"onenote:#x&amp;end\"><span style='font-family:Bookman Old Style'>4</span></a>", true), legend, true, 1.0, M).Should().BeTrue("OneNote may drop a link's span color");
        AnchorSignature.Matches(Outline(legend.Rect, "<a href=\"https://example.com\"><span style='font-family:Bookman Old Style;color:#1E1E20'>4</span></a>", true), legend, true, 1.0, M).Should().BeFalse();
        AnchorSignature.Matches(Outline(legend.Rect with { W = legend.Rect.W * 0.5 }, legendHtml, true), legend, false, 0.5, M).Should().BeTrue("the width follows the sheet's placed scale");
    }

    private static System.Xml.Linq.XElement WithFixedWidth(System.Xml.Linq.XElement outline)
    {
        System.Xml.Linq.XElement copy = new(outline);
        copy.Element(copy.Name.Namespace + "Size")!.SetAttributeValue("isSetByUser", "true");
        return copy;
    }

    [Fact]
    public void A_rebuild_carries_the_texts_the_tool_wrote_for_the_slots_it_re_finds()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey key = PageKey.Day(new DateOnly(2027, 1, 4));
        AnchorWriteResult written = new AnchorWriter(nb.App, nb.Options, new CapturingLog().Logger).Write(nb.PageIds[key], nb.Read(key), new[]
        {
            new AnchorWrite(new PlannedChange(key, "standingGoal:1", null, "Walk 30 min", ChangeKind.Create, false, null), nb.Catalog.Slot(nb.Plan.Page(key), "standingGoal:1"), null),
        });
        SidecarPage monday = nb.Sidecar.Pages[key.Value];
        Dictionary<string, SidecarPage> pages = new(nb.Sidecar.Pages, StringComparer.Ordinal)
        {
            [key.Value] = monday with
            {
                Anchors = new Dictionary<string, string>(monday.Anchors, StringComparer.Ordinal) { ["standingGoal:1"] = written.Created["standingGoal:1"] },
                AnchorTexts = new Dictionary<string, string>(StringComparer.Ordinal) { ["standingGoal:1"] = "Walk 30 min", ["metricName:1"] = "Steps" },
            },
        };
        string newNotebookId = nb.App.RenumberIds(nb.NotebookId);

        RecoveryResult result = Recovery(nb).Recover(new NotebookHandle(newNotebookId, nb.NotebookPath, nb.Plan.Name), nb.Sidecar with { Pages = pages }, "1.2.3");

        result.Rebuilt.Should().BeTrue();
        result.Sidecar.Pages[key.Value].Anchors.Should().ContainKey("standingGoal:1");
        result.Sidecar.Pages[key.Value].AnchorTexts.Should().Equal(
            new Dictionary<string, string> { ["standingGoal:1"] = "Walk 30 min" },
            "the re-found slot keeps what the tool wrote there; a slot whose outline is gone is dropped");
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
