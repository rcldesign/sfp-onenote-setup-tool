using System.Xml.Linq;
using Sfp.Core.Manifests;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// Joining anchors to the outlines OneNote reports: by the sfp.anchor marker first, and for outlines without one (a
/// notebook generated before the marker) by position within a tolerance, by text as the fallback.
/// </summary>
public class AnchorIdMatcherTests
{
    private static readonly Manifest M = TestManifest.Load();
    private static readonly XNamespace One = OneNoteXml.Namespace2013;

    /// <summary>
    /// The Mon Jan 4 page as OneNote would report it: objectIDs on every outline, the sheet image as a callback. Unmarked,
    /// the anchor outlines carry no sfp.anchor meta, as in a notebook generated before the marker existed.
    /// </summary>
    private static PageXml ReadBack(IReadOnlyList<ResolvedAnchor> anchors, Func<XElement, int, XElement>? mutate = null, bool marked = true)
    {
        XElement root = XElement.Parse(PageXmlBuilder.Build(MonJan4.PageId, MonJan4.Page(), MonJan4.Art(anchors), anchors, MonJan4.Options(M)));
        int n = 0;
        foreach (XElement outline in root.Elements(One + "Outline").ToList())
        {
            n++;
            outline.SetAttributeValue("objectID", "{o" + n + "}");
            if (!marked)
            {
                outline.Elements(One + "Meta").Remove();
            }

            mutate?.Invoke(outline, n);
        }

        return PageXmlParser.Parse(root.ToString());
    }

    [Fact]
    public void Every_anchor_outline_carries_its_marker_and_the_sheet_carries_none()
    {
        PageXml page = ReadBack(MonJan4.Anchors(M));

        page.Outlines.Select(AnchorIdMatcher.MarkedAnchorId).Should().Equal(new string?[] { null, "dailyDate", "prev", "next", "objectiveLegend", "standingGoal:1" });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_anchor_is_matched_to_the_outline_at_its_position(bool marked)
    {
        IReadOnlyList<ResolvedAnchor> anchors = MonJan4.Anchors(M);

        AnchorMatchResult result = AnchorIdMatcher.Match(ReadBack(anchors, marked: marked), anchors, MonJan4.Options(M));

        result.Unmatched.Should().BeEmpty();
        result.ObjectIds.Should().HaveCount(5);
        result.ObjectIds["dailyDate"].Should().Be("{o2}", "outline 1 is the sheet");
        result.ObjectIds["prev"].Should().Be("{o3}");
        result.ObjectIds["next"].Should().Be("{o4}");
        result.ObjectIds["objectiveLegend"].Should().Be("{o5}");
        result.ObjectIds["standingGoal:1"].Should().Be("{o6}");
    }

    [Fact]
    public void The_marker_wins_over_a_closer_outline_with_the_same_text()
    {
        IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) };
        XElement root = XElement.Parse(PageXmlBuilder.Build(MonJan4.PageId, MonJan4.Page(), MonJan4.Art(anchors), anchors, MonJan4.Options(M)));
        List<XElement> outlines = root.Elements(One + "Outline").ToList();
        outlines[0].SetAttributeValue("objectID", "{sheet}");
        XElement marked = outlines[1];
        XElement copy = new(marked);
        copy.Elements(One + "Meta").Remove();
        copy.SetAttributeValue("objectID", "{copy}");
        root.Add(copy);
        marked.SetAttributeValue("objectID", "{marked}");
        XElement position = marked.Element(One + "Position")!;
        position.SetAttributeValue("y", OneNoteXml.Pt(double.Parse(position.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture) + 40));

        AnchorMatchResult result = AnchorIdMatcher.Match(PageXmlParser.Parse(root.ToString()), anchors, MonJan4.Options(M));

        result.ObjectIds["dailyDate"].Should().Be("{marked}", "the marker names the anchor wherever the outline sits; an unmarked outline with the same text right where the anchor was written is not it");
        result.Unmatched.Should().BeEmpty();
    }

    [Fact]
    public void An_outline_shifted_within_the_tolerance_still_matches_and_one_shifted_further_matches_by_text()
    {
        IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M), MonJan4.NextAnchor(M) };
        PageXml page = ReadBack(
            anchors,
            (outline, n) =>
            {
                XElement position = outline.Element(One + "Position")!;
                double y = double.Parse(position.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                position.SetAttributeValue("y", OneNoteXml.Pt(y + (n == 2 ? 2.5 : 40)));
                return outline;
            },
            marked: false);

        AnchorMatchResult result = AnchorIdMatcher.Match(page, anchors, MonJan4.Options(M));

        result.Unmatched.Should().BeEmpty();
        result.ObjectIds["dailyDate"].Should().Be("{o2}", "2.5 pt is within the tolerance");
        result.ObjectIds["next"].Should().Be("{o3}", "40 pt away, but it is the only outline reading 'Tue Jan 5'");
    }

    [Fact]
    public void Two_outlines_within_tolerance_are_told_apart_by_their_text()
    {
        ResolvedAnchor a = MonJan4.PrevAnchor(M);
        ResolvedAnchor b = a with { Placement = a.Placement with { AnchorId = "prev2", X = a.Placement.X + 1, Text = "‹ Sat Jan 2" } };
        IReadOnlyList<ResolvedAnchor> anchors = new[] { a, b };

        AnchorMatchResult result = AnchorIdMatcher.Match(ReadBack(anchors, marked: false), anchors, MonJan4.Options(M));

        result.ObjectIds["prev"].Should().Be("{o2}");
        result.ObjectIds["prev2"].Should().Be("{o3}");
    }

    [Fact]
    public void An_anchor_with_no_outline_nearby_and_no_text_match_is_reported_unmatched()
    {
        IReadOnlyList<ResolvedAnchor> written = new[] { MonJan4.DateAnchor(M) };
        IReadOnlyList<ResolvedAnchor> asked = new[] { MonJan4.DateAnchor(M), MonJan4.NextAnchor(M) };

        AnchorMatchResult result = AnchorIdMatcher.Match(ReadBack(written), asked, MonJan4.Options(M));

        result.ObjectIds.Keys.Should().Equal("dailyDate");
        result.Unmatched.Should().Equal("next");
    }

    [Fact]
    public void The_sheet_outline_and_outlines_without_object_ids_are_never_candidates()
    {
        IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.LegendAnchor(M) with { Placement = MonJan4.LegendAnchor(M).Placement with { X = 0, Y = 0, Text = "" } } };
        PageXml page = ReadBack(anchors, (outline, n) =>
        {
            if (n == 2)
            {
                outline.SetAttributeValue("objectID", null);
            }

            return outline;
        });

        AnchorMatchResult result = AnchorIdMatcher.Match(page, anchors, MonJan4.Options(M));

        result.ObjectIds.Should().BeEmpty("the only outline at (0, 36) with an id is the locked sheet");
        result.Unmatched.Should().Equal("objectiveLegend");
    }
}
