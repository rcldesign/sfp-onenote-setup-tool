using System.Net;
using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// Generate with --previous: the Next quarter link written onto the previous notebook's quarterly review, through the
/// in-memory OneNote and the previous notebook's state file.
/// </summary>
public class NextQuarterLinkWriterTests
{
    private const string CoverHref = "onenote:#Cover&section-id={next-quarter}{1}{B0}&page-id={next-cover}{1}{B0}&end";
    private const string OtherCoverHref = "onenote:#Cover&section-id={again-quarter}{1}{B0}&page-id={again-cover}{1}{B0}&end";
    private const string LinkText = "Next quarter ›";

    private static readonly Manifest M = Support.TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;
    private static readonly NotebookHandle Next = new("{nb:next}{1}{B0}", "/out/SFP Q1 2027", "SFP Q1 2027");

    private static NextQuarterLinkWriter Writer(FakeOneNoteApp app, CapturingSink? log = null) =>
        new(app, new SidecarStore(), M, MonJan4.Options(M), (log ?? new CapturingSink()).CreateLogger());

    private static PreviousNotebook Previous(FakeOneNoteApp app, PageXmlOptions? options = null, string? movedTo = null) =>
        PreviousNotebook.Create(app, TestPaths.NewTempDir("previous"), M, options, movedTo);

    private static IReadOnlyList<OutlineInfo> LinkOutlines(PageXml page) => page.Outlines.Where(o => o.PlainText == LinkText).ToList();

    private static int OpenCalls(FakeOneNoteApp app) => app.CallsOf("OpenHierarchy").Count(c => c.EndsWith(":None", StringComparison.Ordinal));

    [Fact]
    public void Adds_one_link_outline_with_the_next_quarter_text_and_the_new_cover_href_and_records_its_object_id()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        PageXml before = previous.Read(app);
        int writes = app.CallsOf("UpdatePageContent").Count;
        List<string> sent = new();
        app.UpdatePageContentFailure = xml =>
        {
            sent.Add(xml);
            return null;
        };

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.Added, result.Warning);
        result.Written.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.PreviousNotebook.Should().Be(PreviousNotebook.Name);
        result.Summary.Should().Be("Next quarter link added to the quarterly review of SFP Q4 2026.");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes + 1, "one UpdatePageContent carries the new outline");
        XElement update = XElement.Parse(sent.Should().ContainSingle().Subject);
        update.Attribute("ID")!.Value.Should().Be(previous.PageId);
        update.Elements().Should().ContainSingle("only the link outline is sent; OneNote keeps everything the XML leaves out")
            .Which.Name.Should().Be(One + "Outline");
        OpenCalls(app).Should().Be(0, "an open notebook is not opened again");

        PageXml after = previous.Read(app);
        after.Outlines.Should().HaveCount(before.Outlines.Count + 1, "exactly one outline is added");
        OutlineInfo link = after.Outlines.Single(o => o.ObjectId == result.ObjectId);
        link.PlainText.Should().Be(LinkText);
        link.TextRuns.Should().ContainSingle().Which.Html.Should().Contain("<a href=\"" + WebUtility.HtmlEncode(CoverHref) + "\">");
        FixedAnchor slot = M.Links.NextQuarter;
        link.X.Should().BeApproximately(slot.X + M.OneNote.AnchorPaddingPt.X, 0.01);
        link.Y.Should().BeApproximately(slot.Y + M.OneNote.TitleOffsetPt + M.OneNote.AnchorPaddingPt.Y, 0.01);
        link.Z.Should().Be(1, "above the locked sheet");
        after.SheetOutline!.ObjectId.Should().Be(before.SheetOutline!.ObjectId);
        after.Outlines.Single(o => o.ObjectId == previous.ScorecardObjectId).PlainText.Should().Be("Scorecard ›", "nothing else on the page changes");
        after.SfpKey.Should().Be(PageKey.QuarterlyReview.Value);

        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors.Should().Equal(new Dictionary<string, string>
        {
            ["quarterReviewScorecard"] = previous.ScorecardObjectId,
            [NextQuarterLinkWriter.AnchorId] = result.ObjectId!,
        });
    }

    [Fact]
    public void A_second_run_updates_the_same_outline_instead_of_adding_another_and_an_identical_run_sends_nothing()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);
        NextQuarterLinkResult first = writer.Link(previous.Folder, Next, CoverHref);
        int outlines = previous.Read(app).Outlines.Count;
        NotebookHandle again = Next with { Id = "{nb:again}{1}{B0}", Name = "SFP Q1 2027 again" };

        NextQuarterLinkResult second = writer.Link(previous.Folder, again, OtherCoverHref);

        second.Outcome.Should().Be(NextQuarterLinkOutcome.Updated, second.Warning);
        second.ObjectId.Should().Be(first.ObjectId);
        second.Summary.Should().Be("Next quarter link on the quarterly review of SFP Q4 2026 now points to this notebook.");
        PageXml page = previous.Read(app);
        page.Outlines.Should().HaveCount(outlines, "the link is updated in place, not added a second time");
        OutlineInfo link = LinkOutlines(page).Should().ContainSingle().Subject;
        link.ObjectId.Should().Be(first.ObjectId);
        link.TextRuns.Single().Html.Should().Contain(WebUtility.HtmlEncode(OtherCoverHref)).And.NotContain(WebUtility.HtmlEncode(CoverHref));
        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors[NextQuarterLinkWriter.AnchorId].Should().Be(first.ObjectId);

        int writes = app.CallsOf("UpdatePageContent").Count;
        NextQuarterLinkResult third = writer.Link(previous.Folder, again, OtherCoverHref);

        third.Outcome.Should().Be(NextQuarterLinkOutcome.Unchanged);
        third.Written.Should().BeTrue();
        third.Summary.Should().Contain("already points to this notebook");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes, "a link that already says the same is not sent again");
        LinkOutlines(previous.Read(app)).Should().ContainSingle();
    }

    [Fact]
    public void An_edit_the_user_makes_between_the_read_and_the_write_survives_the_add_and_the_update()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        XElement mine = new(
            One + "Outline",
            OneNoteXml.Position(One, 120, 520, 2),
            OneNoteXml.Size(One, 300, 13, false),
            new XElement(One + "OEChildren", new XElement(One + "OE", new XElement(One + "T", new XCData("Q4 went well")))));
        app.UpdatePageContent(OneNoteXml.Serialize(new XElement(One + "Page", new XAttribute(XNamespace.Xmlns + "one", One.NamespaceName), new XAttribute("ID", previous.PageId), mine)), DateTime.MinValue);
        NextQuarterLinkWriter writer = Writer(app);

        // The user's outline changes after the writer has read the page and before its write arrives (an iPad edit synced in).
        app.BeforeNextPageWrite = _ => EditMine(app, previous, "Q4 went well; hired two PMs and shipped the connector");
        NextQuarterLinkResult added = writer.Link(previous.Folder, Next, CoverHref);

        added.Outcome.Should().Be(NextQuarterLinkOutcome.Added, added.Warning);
        PageXml page = previous.Read(app);
        page.Outlines.Select(o => o.PlainText).Should().Contain("Q4 went well; hired two PMs and shipped the connector").And.NotContain("Q4 went well");
        LinkOutlines(page).Should().ContainSingle();

        app.BeforeNextPageWrite = _ => EditMine(app, previous, "Q4 went well; the connector shipped on time");
        NextQuarterLinkResult updated = writer.Link(previous.Folder, Next with { Id = "{nb:again}{1}{B0}" }, OtherCoverHref);

        updated.Outcome.Should().Be(NextQuarterLinkOutcome.Updated, updated.Warning);
        page = previous.Read(app);
        page.Outlines.Select(o => o.PlainText).Should().Contain("Q4 went well; the connector shipped on time");
        LinkOutlines(page).Should().ContainSingle().Which.TextRuns.Single().Html.Should().Contain(WebUtility.HtmlEncode(OtherCoverHref));
    }

    /// <summary>
    /// onenote-1: a link outline the user typed into (a line above the link, or words after it in the same run) is kept
    /// exactly as the user has it; the next run adds a fresh link outline below it, records that one and says so.
    /// </summary>
    [Theory]
    [InlineData("a line above the link")]
    [InlineData("words after the link")]
    public void A_link_outline_the_user_typed_into_is_kept_and_a_fresh_link_is_added_below_it(string edit)
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);
        NextQuarterLinkResult first = writer.Link(previous.Folder, Next, CoverHref);
        XElement link = previous.Read(app).Outlines.Single(o => o.ObjectId == first.ObjectId).Element;
        if (edit == "a line above the link")
        {
            link.Element(One + "OEChildren")!.AddFirst(new XElement(One + "OE", new XElement(One + "T", new XCData("Read the Q4 retro notes first"))));
        }
        else
        {
            XElement run = link.Descendants(One + "T").Single();
            run.ReplaceNodes(new XCData(run.Value + " after the Q4 retro"));
        }

        app.UpdatePageContent(OneNoteXml.Serialize(new XElement(One + "Page", new XAttribute(XNamespace.Xmlns + "one", One.NamespaceName), new XAttribute("ID", previous.PageId), link)), DateTime.MinValue);
        OutlineInfo edited = previous.Read(app).Outlines.Single(o => o.ObjectId == first.ObjectId);

        NextQuarterLinkResult second = writer.Link(previous.Folder, Next with { Id = "{nb:again}{1}{B0}" }, OtherCoverHref);

        second.Outcome.Should().Be(NextQuarterLinkOutcome.Added, second.Warning);
        second.Warning.Should().Be("The Next quarter link on the quarterly review of SFP Q4 2026 was edited in OneNote, so it was kept as it is and a new link was added below it.");
        second.ObjectId.Should().NotBe(first.ObjectId);
        PageXml page = previous.Read(app);
        OutlineInfo kept = page.Outlines.Single(o => o.ObjectId == first.ObjectId);
        kept.TextRuns.Select(r => r.Html).Should().Equal(edited.TextRuns.Select(r => r.Html), "the user's outline is left exactly as it is");
        kept.PlainText.Should().Contain("Q4 retro").And.Contain(LinkText);
        OutlineInfo fresh = page.Outlines.Single(o => o.ObjectId == second.ObjectId);
        fresh.PlainText.Should().Be(LinkText);
        fresh.TextRuns.Single().Html.Should().Contain(WebUtility.HtmlEncode(OtherCoverHref));
        fresh.X.Should().BeApproximately(kept.X, 0.01);
        fresh.Y.Should().BeApproximately(kept.Y + kept.Height, 0.01, "the fresh link goes right below the kept outline");
        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors[NextQuarterLinkWriter.AnchorId].Should().Be(second.ObjectId, "the fresh link is the one the state file records now");

        NextQuarterLinkResult third = writer.Link(previous.Folder, Next with { Id = "{nb:again}{1}{B0}" }, OtherCoverHref);

        third.Outcome.Should().Be(NextQuarterLinkOutcome.Unchanged, "the fresh link holds only the tool's link and already says the same");
        third.Warning.Should().BeNull();
    }

    [Fact]
    public void An_edited_link_outline_with_the_users_text_below_it_is_kept_and_nothing_is_written()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);
        NextQuarterLinkResult first = writer.Link(previous.Folder, Next, CoverHref);
        OutlineInfo link = previous.Read(app).Outlines.Single(o => o.ObjectId == first.ObjectId);
        XElement edited = new(link.Element);
        edited.Element(One + "OEChildren")!.AddFirst(new XElement(One + "OE", new XElement(One + "T", new XCData("Read the Q4 retro notes first"))));
        XElement below = new(
            One + "Outline",
            OneNoteXml.Position(One, link.X + 10, link.Y + link.Height + 2, 2),
            OneNoteXml.Size(One, 150, 13, false),
            new XElement(One + "OEChildren", new XElement(One + "OE", new XElement(One + "T", new XCData("January: hire two PMs")))));
        app.UpdatePageContent(OneNoteXml.Serialize(new XElement(One + "Page", new XAttribute(XNamespace.Xmlns + "one", One.NamespaceName), new XAttribute("ID", previous.PageId), edited, below)), DateTime.MinValue);
        int writes = app.CallsOf("UpdatePageContent").Count;

        NextQuarterLinkResult second = writer.Link(previous.Folder, Next with { Id = "{nb:again}{1}{B0}" }, OtherCoverHref);

        second.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        second.Warning.Should().Contain("its link outline was edited in OneNote and is kept as it is").And.Contain("text 'January: hire two PMs'");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes, "nothing is sent");
        previous.Read(app).Outlines.Single(o => o.ObjectId == first.ObjectId).PlainText.Should().Be("Read the Q4 retro notes first " + LinkText);
    }

    /// <summary>Replaces the text of the user's "Q4 went well" outline, sending that outline alone as OneNote's own editor does.</summary>
    private static void EditMine(FakeOneNoteApp app, PreviousNotebook previous, string text)
    {
        XElement outline = previous.Read(app).Outlines.Single(o => o.PlainText.StartsWith("Q4 went well", StringComparison.Ordinal)).Element;
        outline.Descendants(One + "T").Single().ReplaceNodes(new XCData(text));
        app.UpdatePageContent(OneNoteXml.Serialize(new XElement(One + "Page", new XAttribute(XNamespace.Xmlns + "one", One.NamespaceName), new XAttribute("ID", previous.PageId), outline)), DateTime.MinValue);
    }

    [Fact]
    public void A_link_an_earlier_run_wrote_but_could_not_record_is_adopted_and_recorded_instead_of_duplicated()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);
        NextQuarterLinkResult first = writer.Link(previous.Folder, Next, CoverHref);
        Sidecar sidecar = previous.Sidecar();
        SidecarPage review = sidecar.Pages[PageKey.QuarterlyReview.Value];
        Dictionary<string, string> unrecorded = new(review.Anchors, StringComparer.Ordinal);
        unrecorded.Remove(NextQuarterLinkWriter.AnchorId);
        new SidecarStore().Save(previous.SidecarPath, sidecar with
        {
            Pages = new Dictionary<string, SidecarPage>(StringComparer.Ordinal) { [PageKey.QuarterlyReview.Value] = review with { Anchors = unrecorded } },
        });
        int outlines = previous.Read(app).Outlines.Count;

        NextQuarterLinkResult second = writer.Link(previous.Folder, Next with { Id = "{nb:again}{1}{B0}" }, OtherCoverHref);

        second.Outcome.Should().Be(NextQuarterLinkOutcome.Updated, second.Warning);
        second.ObjectId.Should().Be(first.ObjectId);
        PageXml page = previous.Read(app);
        page.Outlines.Should().HaveCount(outlines, "the unrecorded link is taken over, not blocked or doubled");
        LinkOutlines(page).Single().TextRuns.Single().Html.Should().Contain(WebUtility.HtmlEncode(OtherCoverHref));
        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors[NextQuarterLinkWriter.AnchorId].Should().Be(first.ObjectId, "the adopted link is recorded");
    }

    [Fact]
    public void A_state_file_that_cannot_be_saved_leaves_the_link_written_with_a_warning()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = new(app, new UnsavableStore(), M, MonJan4.Options(M), new CapturingSink().CreateLogger());

        NextQuarterLinkResult result = writer.Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.Added);
        result.Written.Should().BeTrue();
        result.ObjectId.Should().NotBeNullOrEmpty();
        result.Warning.Should().Contain("is on the quarterly review of SFP Q4 2026 but its state file").And.Contain("the disk is full");
        LinkOutlines(previous.Read(app)).Should().ContainSingle();
    }

    [Fact]
    public void A_previous_notebook_that_is_not_open_is_opened_from_its_folder_once_and_linked()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        app.Close(previous.NotebookId);

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.Added, result.Warning);
        app.CallsOf("OpenHierarchy").Where(c => c.EndsWith(":None", StringComparison.Ordinal)).Should().Equal(previous.Folder + ":None");
        LinkOutlines(previous.Read(app)).Should().ContainSingle().Which.ObjectId.Should().Be(result.ObjectId);
    }

    [Fact]
    public void The_state_file_path_or_a_folder_with_a_trailing_separator_is_accepted_in_place_of_the_folder()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);

        NextQuarterLinkResult bySidecar = writer.Link(previous.SidecarPath, Next, CoverHref);
        NextQuarterLinkResult byFolder = writer.Link(previous.Folder + Path.DirectorySeparatorChar, Next, CoverHref);

        bySidecar.Outcome.Should().Be(NextQuarterLinkOutcome.Added, bySidecar.Warning);
        byFolder.Outcome.Should().Be(NextQuarterLinkOutcome.Unchanged, byFolder.Warning);
        byFolder.ObjectId.Should().Be(bySidecar.ObjectId);
    }

    [Fact]
    public void Page_ids_that_no_longer_resolve_are_tried_once_more_after_opening_the_folder_then_reported()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        Sidecar stale = previous.Sidecar();
        SidecarPage review = stale.Pages[PageKey.QuarterlyReview.Value] with { PageId = "{gone}{1}{B0}" };
        new SidecarStore().Save(previous.SidecarPath, stale with { Pages = new Dictionary<string, SidecarPage>(StringComparer.Ordinal) { [PageKey.QuarterlyReview.Value] = review } });
        int writes = app.CallsOf("UpdatePageContent").Count;

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        result.Written.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.Warning.Should().StartWith("The Next quarter link was not written to the previous notebook " + previous.Folder + ":")
            .And.Contain("could not be found");
        OpenCalls(app).Should().Be(1, "the folder is opened once");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes);
        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors.Should().NotContainKey(NextQuarterLinkWriter.AnchorId);
    }

    [Fact]
    public void A_previous_notebook_that_is_nowhere_is_reported_without_a_onenote_call()
    {
        using FakeOneNoteApp app = new();
        string missing = Path.Combine(TestPaths.NewTempDir("previous"), PreviousNotebook.Name);

        NextQuarterLinkResult result = Writer(app).Link(missing, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        result.PreviousNotebook.Should().Be(missing);
        result.Warning.Should().Be("The Next quarter link was not written to the previous notebook " + missing + ": no notebook folder was found there and no state file at " + missing + ".sfp-state.json.");
        app.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_notebook_moved_to_onedrive_is_opened_from_the_location_its_state_file_records()
    {
        using FakeOneNoteApp app = new();
        const string url = "https://d.docs.live.net/4f2a9c/Documents/SFP Q4 2026";
        PreviousNotebook previous = Previous(app, movedTo: url);
        app.Close(previous.NotebookId);
        Directory.Exists(previous.Folder).Should().BeFalse("the local folder went with the move");

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.Added, result.Warning);
        app.CallsOf("OpenHierarchy").Where(c => c.EndsWith(":None", StringComparison.Ordinal)).Should().Equal(url + ":None");
        LinkOutlines(previous.Read(app)).Should().ContainSingle();
    }

    [Fact]
    public void A_notebook_that_cannot_be_opened_is_reported()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        app.Close(previous.NotebookId);
        string moved = Path.Combine(TestPaths.NewTempDir("moved"), PreviousNotebook.Name);
        new SidecarStore().Save(new SidecarStore().PathFor(moved), previous.Sidecar() with { NotebookPath = moved });

        NextQuarterLinkResult result = Writer(app).Link(moved, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        result.Warning.Should().Contain("could not be opened from " + moved).And.Contain("No notebook at");
        OpenCalls(app).Should().Be(1);
    }

    [Fact]
    public void Something_that_is_not_the_tools_in_the_slot_is_left_alone_and_reported()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        XElement root = new(previous.Read(app).Document.Root!);
        FixedAnchor slot = M.Links.NextQuarter;
        root.Add(new XElement(
            One + "Outline",
            OneNoteXml.Position(One, slot.X + 20, slot.Y + M.OneNote.TitleOffsetPt + 2, 2),
            OneNoteXml.Size(One, 150, 13, false),
            new XElement(One + "OEChildren", new XElement(One + "OE", new XElement(One + "T", new XCData("my plan for January"))))));
        app.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);
        int writes = app.CallsOf("UpdatePageContent").Count;

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        result.Warning.Should().Contain("text 'my plan for January'");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes);
        PageXml page = previous.Read(app);
        page.Outlines.Should().Contain(o => o.PlainText == "my plan for January");
        LinkOutlines(page).Should().BeEmpty();
    }

    [Fact]
    public void The_link_goes_over_the_slot_of_a_sheet_placed_without_a_title_and_at_another_scale()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app, MonJan4.Options(M, MonJan4.Generate() with { HideTitle = true, PageScale = 0.72 }));

        NextQuarterLinkResult result = Writer(app).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.Added, result.Warning);
        OutlineInfo link = previous.Read(app).Outlines.Single(o => o.ObjectId == result.ObjectId);
        FixedAnchor slot = M.Links.NextQuarter;
        link.X.Should().BeApproximately((slot.X * 0.72) + M.OneNote.AnchorPaddingPt.X, 0.01);
        link.Y.Should().BeApproximately((slot.Y * 0.72) + M.OneNote.AnchorPaddingPt.Y, 0.01, "the sheet sits at y 0 when the title is hidden");
        link.Width.Should().BeApproximately(slot.W * 0.72, 0.01);
        link.TextRuns.Single().Html.Should().Contain("font-size:" + OneNoteXml.FontSize(slot.SizePt * 0.72));
    }

    [Fact]
    public void A_page_that_now_holds_another_page_key_or_the_notebook_just_generated_is_left_alone()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        NextQuarterLinkWriter writer = Writer(app);

        NextQuarterLinkResult self = writer.Link(previous.Folder, Next with { Id = previous.NotebookId }, CoverHref);

        self.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        self.Warning.Should().Contain("it is the notebook this run generated");

        PageSpec other = PreviousNotebook.Review(PageKey.Notes("Ideas"));
        app.UpdatePageContent(PageXmlBuilder.Build(previous.PageId, other, new RenderedPage(other.Key, MonJan4.Png, 1, 1, Array.Empty<AnchorPlacement>()), Array.Empty<ResolvedAnchor>(), MonJan4.Options(M)), DateTime.MinValue);
        int writes = app.CallsOf("UpdatePageContent").Count;

        NextQuarterLinkResult moved = writer.Link(previous.Folder, Next, CoverHref);

        moved.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        moved.Warning.Should().Contain("'notes:ideas'");
        app.CallsOf("UpdatePageContent").Should().HaveCount(writes);
    }

    [Fact]
    public void OneNote_refusing_the_write_is_a_warning_and_the_state_file_is_left_as_it_was()
    {
        using FakeOneNoteApp app = new();
        PreviousNotebook previous = Previous(app);
        app.UpdatePageContentFailure = xml => xml.Contains(LinkText, StringComparison.Ordinal)
            ? new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InvalidXml, "OneNote rejected the XML passed to UpdatePageContent", xml, null)
            : null;
        CapturingSink log = new();

        NextQuarterLinkResult result = Writer(app, log).Link(previous.Folder, Next, CoverHref);

        result.Outcome.Should().Be(NextQuarterLinkOutcome.NotWritten);
        result.Warning.Should().Contain("OneNote rejected the XML passed to UpdatePageContent");
        previous.Sidecar().Pages[PageKey.QuarterlyReview.Value].Anchors.Should().NotContainKey(NextQuarterLinkWriter.AnchorId);
        log.Messages.Should().Contain(m => m.Contains("could not be written", StringComparison.Ordinal));
    }

    /// <summary>A state file store whose saves fail the way a full or read-only disk does.</summary>
    private sealed class UnsavableStore : ISidecarStore
    {
        private readonly SidecarStore _inner = new();

        public string PathFor(string notebookFolder) => _inner.PathFor(notebookFolder);

        public Sidecar? Load(string path) => _inner.Load(path);

        public void Save(string path, Sidecar sidecar) => throw new IOException("the disk is full");
    }

    [Fact]
    public void Arguments_are_checked()
    {
        using FakeOneNoteApp app = new();
        NextQuarterLinkWriter writer = Writer(app);

        ((Action)(() => writer.Link(" ", Next, CoverHref))).Should().Throw<ArgumentException>();
        ((Action)(() => writer.Link("/x", Next, ""))).Should().Throw<ArgumentException>();
        NextQuarterLinkWriter.WarningFor("/x/SFP Q4 2026", "it was not there.").Should().Be("The Next quarter link was not written to the previous notebook /x/SFP Q4 2026: it was not there.");
    }
}
