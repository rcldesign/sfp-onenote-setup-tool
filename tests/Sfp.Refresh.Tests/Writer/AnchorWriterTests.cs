using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Tests.Writer;

/// <summary>M4.5: writes go through the page's existing XML plus the changed outlines only; the image bytes are never resent.</summary>
public class AnchorWriterTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    private static AnchorWriter Writer(FakeNotebook nb) => new(nb.App, nb.Options, new CapturingLog().Logger);

    private static AnchorWrite Create(FakeNotebook nb, PageKey key, string slotId, string text) =>
        new(new PlannedChange(key, slotId, null, text, ChangeKind.Create, false, null), nb.Catalog.Slot(nb.Plan.Page(key), slotId), null);

    private static string OutlineXml(PageXml page, string objectId) =>
        page.Document.Root!.Elements(page.Document.Root.Name.Namespace + "Outline").Single(o => (string?)o.Attribute("objectID") == objectId).ToString();

    [Fact]
    public void Create_appends_one_outline_resends_no_image_bytes_and_records_the_new_object_id()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        PageXml before = nb.Read(MonJan4);
        string imageBefore = nb.App.ImageData(pageId)!;

        AnchorWriteResult result = Writer(nb).Write(pageId, before, new[] { Create(nb, MonJan4, "standingGoal:1", "Walk 30 min") });

        result.Created.Should().ContainKey("standingGoal:1");
        result.Unresolved.Should().BeEmpty();
        nb.App.LastUpdateXml.Should().NotContain("<one:Data>", "the piBasic XML carries the image as a callback ID").And.Contain("CallbackID");
        nb.App.ImageData(pageId).Should().Be(imageBefore);
        nb.App.UpdateCount(pageId).Should().Be(2, "one write at generate, one now");

        PageXml after = nb.Read(MonJan4);
        after.Outlines.Should().HaveCount(before.Outlines.Count + 1);
        OutlineInfo created = after.Outlines.Single(o => o.ObjectId == result.Created["standingGoal:1"]);
        created.PlainText.Should().Be("Walk 30 min");
        created.X.Should().Be(M.Slots.StandingGoals.Rows[0].X + M.OneNote.AnchorPaddingPt.X);
        created.Y.Should().Be(M.Slots.StandingGoals.Rows[0].Y + M.OneNote.TitleOffsetPt + M.OneNote.AnchorPaddingPt.Y);
        created.TextRuns[0].Html.Should().Contain("font-size:10.0pt").And.Contain("color:#6B6763", "pre-fills are written at 60% ink");
        foreach (OutlineInfo old in before.Outlines)
        {
            OutlineXml(after, old.ObjectId).Should().Be(OutlineXml(before, old.ObjectId), "no other outline changes");
        }
    }

    [Fact]
    public void Update_replaces_only_that_outlines_text_and_the_image_element_is_untouched()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        AnchorWriter writer = Writer(nb);
        AnchorWriteResult created = writer.Write(pageId, nb.Read(MonJan4), new[] { Create(nb, MonJan4, "objectiveLegend", "O1 Ship the connector"), Create(nb, MonJan4, "metricName:2", "Calls") });
        string legendId = created.Created["objectiveLegend"];
        PageXml before = nb.Read(MonJan4);

        AnchorWriteResult result = writer.Write(pageId, before, new[]
        {
            new AnchorWrite(new PlannedChange(MonJan4, "objectiveLegend", "O1 Ship the connector", "O1 Ship the connector  O2 Hire two PMs", ChangeKind.Update, false, null), nb.Catalog.Slot(nb.Plan.Page(MonJan4), "objectiveLegend"), legendId),
        });

        result.Updated.Should().Equal("objectiveLegend");
        result.Created.Should().BeEmpty();
        PageXml after = nb.Read(MonJan4);
        after.Outlines.Should().HaveCount(before.Outlines.Count);
        after.Outlines.Single(o => o.ObjectId == legendId).PlainText.Should().Be("O1 Ship the connector  O2 Hire two PMs");
        after.Images.Single().Should().Be(before.Images.Single());
        OutlineXml(after, before.SheetOutline!.ObjectId).Should().Be(OutlineXml(before, before.SheetOutline.ObjectId));
        foreach (OutlineInfo old in before.Outlines.Where(o => o.ObjectId != legendId))
        {
            OutlineXml(after, old.ObjectId).Should().Be(OutlineXml(before, old.ObjectId), "only the updated outline changed");
        }

        nb.App.LastUpdateXml.Should().NotContain("<one:Data>");
    }

    [Fact]
    public void Remove_deletes_the_outline_and_multi_line_text_round_trips_through_br()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageKey january = PageKey.Calendar(2027, 1);
        string pageId = nb.PageIds[january];
        AnchorWriter writer = Writer(nb);
        AnchorWriteResult created = writer.Write(pageId, nb.Read(january), new[] { Create(nb, january, "habitLegend", "1 Run  2 Read\n9 Swim") });
        string id = created.Created["habitLegend"];
        PageXml withLegend = nb.Read(january);
        withLegend.Outlines.Single(o => o.ObjectId == id).PlainText.Should().Be("1 Run  2 Read\n9 Swim");
        withLegend.Outlines.Single(o => o.ObjectId == id).TextRuns[0].Html.Should().Contain("2 Read<br>9 Swim");

        AnchorWriteResult result = writer.Write(pageId, withLegend, new[]
        {
            new AnchorWrite(new PlannedChange(january, "habitLegend", "1 Run  2 Read\n9 Swim", "", ChangeKind.Remove, false, null), null, id),
        });

        result.Removed.Should().Equal("habitLegend");
        nb.Read(january).Outlines.Should().NotContain(o => o.ObjectId == id);
        nb.Read(january).Outlines.Should().HaveCount(withLegend.Outlines.Count - 1);
    }

    [Fact]
    public void Blocked_writes_and_empty_lists_send_nothing()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        int updates = nb.App.UpdateCount(pageId);
        AnchorWrite blocked = new(new PlannedChange(MonJan4, "standingGoal:1", null, "Walk", ChangeKind.Create, true, "in the way"), nb.Catalog.Slot(nb.Plan.Page(MonJan4), "standingGoal:1"), null);

        Writer(nb).Write(pageId, nb.Read(MonJan4), new[] { blocked }).Count.Should().Be(0);
        Writer(nb).Write(pageId, nb.Read(MonJan4), Array.Empty<AnchorWrite>()).Count.Should().Be(0);

        nb.App.UpdateCount(pageId).Should().Be(updates);
    }

    [Fact]
    public void A_missing_owned_outline_and_a_rejected_write_surface_as_OneNote_exceptions()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        AnchorWriter writer = Writer(nb);

        Action gone = () => writer.Write(pageId, nb.Read(MonJan4), new[]
        {
            new AnchorWrite(new PlannedChange(MonJan4, "standingGoal:1", "x", "y", ChangeKind.Update, false, null), null, "{p-0000}{99}{B0}"),
        });
        gone.Should().Throw<OneNoteException>().WithMessage("*standingGoal:1*");

        nb.App.UpdatePageContentFailure = _ => new OneNoteXmlRejectedException("UpdatePageContent", OneNoteHResults.InvalidXml, "rejected", null, null);
        Action rejected = () => writer.Write(pageId, nb.Read(MonJan4), new[] { Create(nb, MonJan4, "standingGoal:1", "Walk") });
        rejected.Should().Throw<OneNoteXmlRejectedException>();
    }

    [Fact]
    public void Anchors_follow_the_sheet_as_it_sits_on_the_page_hidden_title_and_scaled()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        PageXml page = nb.Read(MonJan4);
        XElement sheet = page.Document.Root!.Elements(page.Document.Root.Name.Namespace + "Outline").First(o => (string?)o.Attribute("objectID") == page.SheetOutline!.ObjectId);
        XNamespace one = page.Document.Root.Name.Namespace;
        sheet.Element(one + "Position")!.ReplaceWith(OneNoteXml.Position(one, 0, 0, 0));
        sheet.Element(one + "Size")!.ReplaceWith(OneNoteXml.Size(one, M.PageWidthPt * 0.5, M.PageHeightPt * 0.5, true));
        nb.App.UpdatePageContent(OneNoteXml.Serialize(page.Document.Root), DateTime.MinValue);
        PageXml scaled = nb.Read(MonJan4);

        AnchorWriteResult result = Writer(nb).Write(pageId, scaled, new[] { Create(nb, MonJan4, "metricName:1", "Steps") });

        RectPt cell = M.Slots.MetricNames.Cells[0];
        OutlineInfo created = nb.Read(MonJan4).Outlines.Single(o => o.ObjectId == result.Created["metricName:1"]);
        created.X.Should().BeApproximately(cell.X * 0.5, 1e-6);
        created.Y.Should().BeApproximately(cell.Y * 0.5, 1e-6, "no title offset when the sheet sits at y 0");
        created.Width.Should().BeApproximately(cell.W * 0.5, 1e-6);
        created.TextRuns[0].Html.Should().Contain("font-size:5.0pt", "the font scales with the sheet");
    }

    [Fact]
    public void Html_blends_opacity_and_escapes_text()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        SlotGeometry goal = nb.Catalog.Slot(nb.Plan.Page(MonJan4), "standingGoal:1")!;

        string html = AnchorWriter.Html("standingGoal:1", "Walk & talk\nmore", goal, nb.Options);

        html.Should().Be("<span style='font-family:Bookman Old Style;font-size:10.0pt;color:#6B6763'>Walk &amp; talk<br>more</span>");
        AnchorWriter.Html("x", "a", null, nb.Options).Should().Contain("color:#1E1E20");
    }
}
