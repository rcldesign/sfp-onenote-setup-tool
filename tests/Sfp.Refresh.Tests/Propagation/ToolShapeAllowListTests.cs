using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Xml;
using Sfp.Refresh.Propagation;

namespace Sfp.Refresh.Tests.Propagation;

/// <summary>
/// Round 3 refresh-2: the tool shape is an allow-list taken from the official 2013 schema's Outline and OE content
/// models, so content the old deny-list missed (one:FutureObject, an Outlook task, a linked note, a media time stamp) or
/// that no list names marks a tool outline edited, while the formatting a text outline carries (lists, indents, meta,
/// indented paragraphs of text) does not.
/// </summary>
public class ToolShapeAllowListTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    [Theory]
    [InlineData("<one:OE><one:FutureObject objectID=\"{f}\"><one:Position x=\"10\" y=\"40\"/><one:Size width=\"40\" height=\"20\"/></one:FutureObject></one:OE>")]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE><one:OE><one:FutureObject objectID=\"{f}\"/></one:OE>")]
    [InlineData("<one:OE><one:OutlookTask startDate=\"2027-01-04T09:00:00.000Z\" dueDate=\"2027-01-04T17:00:00.000Z\" guidTask=\"{6E1B7B57-2C3A-4E0B-9F3D-1A2B3C4D5E6F}\" completed=\"false\"/><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>")]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T><one:LinkedNote state=\"1\"><one:LinkedNoteUri>https://example.com/plan.docx</one:LinkedNoteUri><one:LinkedNoteShortName>plan.docx</one:LinkedNoteShortName><one:LinkedNoteFriendlyName>Plan</one:LinkedNoteFriendlyName></one:LinkedNote></one:OE>")]
    [InlineData("<one:OE><one:MediaIndex timeIndex=\"12\"><one:MediaReference mediaID=\"{11111111-2222-3333-4444-555555555555}\"/></one:MediaIndex><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>")]
    [InlineData("<one:OE><one:InkParagraph><one:InkWord recognizedText=\"x\"/></one:InkParagraph></one:OE>")]
    [InlineData("<one:ChildOELayout spaceBefore=\"2\"/><one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>")]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE><one:OE><one:Sticker objectID=\"{s}\"/></one:OE>")]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T><x:Note xmlns:x=\"urn:example:notes\"/></one:OE>")]
    public void Anything_but_text_and_its_formatting_inside_a_tool_outline_marks_it_edited(string paragraphs)
    {
        SlotInspector.HasToolShape(Outline(paragraphs)).Should().BeFalse();
    }

    [Theory]
    [InlineData("<one:OE><one:List><one:Bullet bullet=\"2\" fontSize=\"11.0\"/></one:List><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>", "")]
    [InlineData("<one:OE><one:List><one:Number numberSequence=\"0\" numberFormat=\"##.\" fontSize=\"11.0\" text=\"1.\"/></one:List><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>", "")]
    [InlineData("<one:OE><one:Meta name=\"note\" content=\"x\"/><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>", "")]
    [InlineData("<one:OE><one:T><![CDATA[Walk]]></one:T><one:OEChildren><one:OE><one:T><![CDATA[and run]]></one:T></one:OE></one:OEChildren></one:OE>", "")]
    [InlineData("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE>", "<one:Meta name=\"sfp.anchor\" content=\"standingGoal:1\"/><one:Indents><one:Indent level=\"0\" indent=\"0.0\"/></one:Indents>")]
    public void Text_with_lists_indents_and_meta_keeps_the_tools_shape(string paragraphs, string afterSize)
    {
        SlotInspector.HasToolShape(Outline(paragraphs, afterSize)).Should().BeTrue();
    }

    [Fact]
    public void Clearing_or_changing_a_slot_whose_outline_holds_a_future_object_is_blocked_as_edited()
    {
        SlotState withFuture = new(Outline("<one:OE><one:T><![CDATA[Walk 30 min]]></one:T></one:OE><one:OE><one:FutureObject objectID=\"{f}\"/></one:OE>"), false, Array.Empty<Intruder>());

        Propagator.Decide(MonJan4, "standingGoal:1", "", withFuture, "Walk 30 min")
            .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Remove && c.Blocked && c.Reason == Propagator.EditedReason);
        Propagator.Decide(MonJan4, "standingGoal:1", "Run 5k", withFuture, "Walk 30 min")
            .Should().Match<PlannedChange>(c => c.Kind == ChangeKind.Update && c.Blocked && c.Reason == Propagator.EditedReason);
    }

    private static OutlineInfo Outline(string paragraphs, string afterSize = "") => PageXmlParser.Parse(
        "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\"{p}\"><one:Outline objectID=\"{o}\">"
        + "<one:Position x=\"10\" y=\"20\"/><one:Size width=\"100\" height=\"13\" isSetByUser=\"true\"/>" + afterSize + "<one:OEChildren>"
        + paragraphs + "</one:OEChildren></one:Outline></one:Page>").Outlines.Single();
}
