using Sfp.Core;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Writer;
using Sfp.OneNote.Tests.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>M3.6: link placements joined to the plan's links and the gathered hyperlinks; propagated slots left out.</summary>
public class AnchorResolverTests
{
    private static readonly NotebookPlan Plan = TestPlans.Small();

    private static IReadOnlyDictionary<PageKey, string> Hrefs() =>
        Plan.Pages.ToDictionary(p => p.Key, p => "onenote:#" + p.Key.Value + "&section-id={s}&page-id={" + p.Key.Value + "}&end");

    private static RenderedPage Art(PageKey key, params AnchorPlacement[] placements) => new(key, MonJan4.Png, 1, 1, placements);

    private static AnchorPlacement Placement(string id, string text, AnchorKind kind = AnchorKind.Link) =>
        new(id, 10, 20, 100, 14, text, TextAlign.Left, 10) { Kind = kind };

    [Fact]
    public void Link_placements_get_the_target_page_href_and_propagated_slots_are_not_written()
    {
        PageSpec monday = Plan.Page(TestPlans.Monday);
        RenderedPage art = Art(monday.Key, Placement("dailyDate", "01 / 04 / 27"), Placement("objectiveLegend", "", AnchorKind.Propagated), Placement("next", "Tue Jan 5 ›"));
        List<string> problems = new();

        IReadOnlyList<ResolvedAnchor> anchors = AnchorResolver.Resolve(monday, art, AnchorResolver.LinksByPage(Plan)[monday.Key], Hrefs(), linksEnabled: true, problems);

        anchors.Select(a => a.Placement.AnchorId).Should().Equal("dailyDate", "next");
        anchors[0].Href.Should().Be(Hrefs()[TestPlans.Overview]);
        anchors[1].Href.Should().Be(Hrefs()[TestPlans.Tuesday]);
        anchors.Should().OnlyContain(a => a.ToolOwned);
        problems.Should().BeEmpty();
    }

    [Fact]
    public void A_placement_without_a_link_spec_or_without_a_target_href_is_left_out_with_a_note()
    {
        PageSpec monday = Plan.Page(TestPlans.Monday);
        RenderedPage art = Art(monday.Key, Placement("dailyDate", "01 / 04 / 27"), Placement("calendarWeek:1", "Week 02"));
        Dictionary<PageKey, string> hrefs = new(Hrefs());
        hrefs.Remove(TestPlans.Overview);
        List<string> problems = new();

        IReadOnlyList<ResolvedAnchor> anchors = AnchorResolver.Resolve(monday, art, AnchorResolver.LinksByPage(Plan)[monday.Key], hrefs, true, problems);

        anchors.Should().BeEmpty();
        problems.Should().HaveCount(2);
        problems[0].Should().Contain("dailyDate").And.Contain("no hyperlink");
        problems[1].Should().Contain("calendarWeek:1").And.Contain("no link");
    }

    [Fact]
    public void Links_switched_off_write_nothing()
    {
        PageSpec monday = Plan.Page(TestPlans.Monday);
        RenderedPage art = Art(monday.Key, Placement("dailyDate", "01 / 04 / 27"));

        AnchorResolver.Resolve(monday, art, AnchorResolver.LinksByPage(Plan)[monday.Key], Hrefs(), linksEnabled: false).Should().BeEmpty();
    }

    [Fact]
    public void Links_by_page_groups_the_plan_links_by_their_source_page_and_anchor_id()
    {
        IReadOnlyDictionary<PageKey, IReadOnlyDictionary<string, LinkSpec>> byPage = AnchorResolver.LinksByPage(Plan);

        byPage.Keys.Should().BeEquivalentTo(new[] { TestPlans.Monday, TestPlans.Tuesday, TestPlans.Overview, TestPlans.Reflection });
        byPage[TestPlans.Overview].Keys.Should().BeEquivalentTo("overviewDayHeader:0", "overviewDayHeader:1");
        byPage[TestPlans.Overview]["overviewDayHeader:1"].To.Should().Be(TestPlans.Tuesday);
        byPage.Should().NotContainKey(PageKey.Cover);
        AnchorResolver.None.Should().BeEmpty();
    }
}
