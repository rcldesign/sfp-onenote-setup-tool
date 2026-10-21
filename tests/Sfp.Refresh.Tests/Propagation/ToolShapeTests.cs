using System.Xml.Linq;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>Round 2 refresh-3: a tool outline holding anything but text counts as edited and is never deleted.</summary>
public class ToolShapeTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    /// <summary>A pasted picture, as the content of a paragraph (one:OE).</summary>
    public static object Picture(XNamespace one) =>
        new XElement(one + "Image", new XAttribute("format", "png"), OneNoteXml.Size(one, 40, 20, true), new XElement(one + "Data", Convert.ToBase64String(FakeNotebook.Png)));

    [Theory]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>", true)]
    [InlineData("<one:OE><one:T><![CDATA[Walk]]></one:T></one:OE><one:OE><one:T><![CDATA[and run]]></one:T></one:OE>", true)]
    [InlineData("<one:OE><one:T><![CDATA[Walk]]></one:T></one:OE><one:OE><one:Image format=\"png\"><one:Size width=\"40\" height=\"20\"/></one:Image></one:OE>", false)]
    [InlineData("<one:OE><one:T><![CDATA[Walk]]></one:T><one:InkWord recognizedText=\"x\"/></one:OE>", false)]
    [InlineData("<one:OE><one:InkDrawing/><one:T><![CDATA[Walk]]></one:T></one:OE>", false)]
    [InlineData("<one:OE><one:T><![CDATA[Walk]]></one:T></one:OE><one:OE><one:InsertedFile pathCache=\"C:\\x.pdf\"/></one:OE>", false)]
    [InlineData("<one:OE><one:Tag index=\"0\"/><one:T><![CDATA[Walk]]></one:T></one:OE>", false)]
    [InlineData("<one:OE><one:Table/></one:OE>", false)]
    public void Only_an_outline_of_text_has_the_tools_shape(string paragraphs, bool expected)
    {
        SlotInspector.HasToolShape(Outline(paragraphs)).Should().Be(expected);
    }

    [Fact]
    public void Clearing_or_changing_a_slot_whose_outline_holds_a_picture_is_blocked_as_edited()
    {
        SlotState withPicture = new(Outline("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE><one:OE><one:Image format=\"png\"/></one:OE>"), false, Array.Empty<Intruder>());
        SlotState textOnly = new(Outline("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>"), false, Array.Empty<Intruder>());

        Propagator.Decide(MonJan4, "standingGoal:1", "", withPicture, "Walk 30 min")
            .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Remove && c.Blocked && c.Reason == Propagator.EditedReason);
        Propagator.Decide(MonJan4, "standingGoal:1", "Run 5k", withPicture, null)
            .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Update && c.Blocked && c.Reason == Propagator.EditedReason, "a missing record of the tool's text does not excuse a picture inside");
        Propagator.Decide(MonJan4, "standingGoal:1", "", textOnly, "Walk 30 min")!.Blocked.Should().BeFalse("an outline of the tool's text alone is removed");
    }

    [Fact]
    public void The_writer_refuses_to_delete_an_outline_holding_a_picture()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        SlotGeometry goal = nb.Catalog.Slot(nb.Plan.FindPage(MonJan4)!, "standingGoal:1")!;
        AnchorWriter writer = new(nb.App, nb.Options, new CapturingLog().Logger);
        string id = writer.Write(nb.PageIds[MonJan4], nb.Read(MonJan4), new[] { new AnchorWrite(new PlannedChange(MonJan4, "standingGoal:1", null, "Walk 30 min", ChangeKind.Create, false, null), goal, null) })
            .Created["standingGoal:1"];
        nb.AddInsideOutline(MonJan4, id, Picture);
        int deletes = nb.App.CallCount("DeletePageContent");

        Action remove = () => writer.Write(nb.PageIds[MonJan4], nb.Read(MonJan4), new[] { new AnchorWrite(new PlannedChange(MonJan4, "standingGoal:1", "Walk 30 min", "", ChangeKind.Remove, false, null), goal, id) });

        remove.Should().Throw<InvalidOperationException>().WithMessage($"The outline {id} of standingGoal:1 on page * holds an element outside the tool's shape (text, lists, indents, meta) that the user put inside it, so it is never deleted.");
        nb.App.CallCount("DeletePageContent").Should().Be(deletes);
        nb.Read(MonJan4).Outlines.Should().Contain(o => o.ObjectId == id);
    }

    private static OutlineInfo Outline(string paragraphs) => PageXmlParser.Parse(
        "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\"{p}\"><one:Outline objectID=\"{o}\">"
        + "<one:Position x=\"10\" y=\"20\"/><one:Size width=\"100\" height=\"13\" isSetByUser=\"true\"/><one:OEChildren>"
        + paragraphs + "</one:OEChildren></one:Outline></one:Page>").Outlines.Single();
}
