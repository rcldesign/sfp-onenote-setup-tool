using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.6 and the pass-through pages: the cover label lines, the Annual Objectives pages (label cells erased when the
/// manifest says so, "annualObjective:{n}" links and "annualRow:{n}" title cells), the Objective Details pages
/// ("objectiveBack" link, "objectiveHeading" cell), the Weekly Overview (date baked, day-name headers erased and
/// placed as links, event columns), the daily page (date, strip, counter, time blocks, propagated bands, rows and
/// cells) and the Weekly Reflection (date baked, navigation and scorecard links). The fixed-position link anchors are
/// placed by <see cref="AnchorLayout"/>. With no link for them (links off), the block labels and the detail page's
/// "Objective n" are baked in their anchor rectangles instead, so the pages printed from the same art still differ.
/// </summary>
internal static class TemplatePages
{
    public const string AnnualObjectiveAnchor = "annualObjective";
    public const string ObjectiveBackAnchor = "objectiveBack";
    public const string AnnualRowSlot = "annualRow";
    public const string ObjectiveHeadingSlot = "objectiveHeading";
    public const string OverviewDayHeaderAnchor = "overviewDayHeader";
    public const string OverviewDaySlot = "overviewDay";
    public const string ObjectiveLegendSlot = "objectiveLegend";
    public const string StandingGoalSlot = "standingGoal";
    public const string MetricNameSlot = "metricName";
    public const string AllDayBandSlot = "allDayBand";

    public static void Cover(RenderContext c)
    {
        CoverLabelSlot slot = c.Manifest.Slots.CoverLabel;
        if (!c.Applies(slot.AppliesTo))
        {
            return;
        }

        (string label, string span) = PlannerText.CoverLines(c.Plan.Quarter);
        string[] lines = [label, span];
        for (int i = 0; i < Math.Min(lines.Length, slot.Baselines.Count); i++)
        {
            c.Text(lines[i], slot.X, slot.Baselines[i], slot.Align, slot.SizePt);
        }
    }

    public static void Annual(RenderContext c)
    {
        Manifest m = c.Manifest;
        AnnualObjectiveAnchor anchor = m.Links.AnnualObjective;
        AnnualTitleSlot title = m.Slots.AnnualTitle;
        int blocks = anchor.BlockTops.Count;
        int pageIndex = AnnualPageIndex(c);
        for (int block = 0; block < blocks; block++)
        {
            int n = (pageIndex * blocks) + block + 1;
            string number = n.ToString(CultureInfo.InvariantCulture);
            LinkSpec? link = c.Link(AnnualObjectiveAnchor + ":" + number);
            RectPt rect = anchor.Rect(block);
            if (anchor.Erase)
            {
                c.Erase(rect);
            }

            if (link is not null)
            {
                c.AddLink(link.AnchorId, rect, link.Text, anchor.Align, anchor.SizePt);
            }
            else
            {
                // No link for this block (links off): the label is baked so each block still names its objective.
                c.TextIn(rect, LinkGraphBuilder.ObjectiveLabel(n), anchor.Align, anchor.SizePt);
            }

            if (c.Applies(title.AppliesTo))
            {
                c.AddPropagated(AnnualRowSlot + ":" + number, title.Rect(anchor.BlockTops[block]), title.SizePt);
            }
        }
    }

    public static void Objective(RenderContext c)
    {
        BandSlot heading = c.Manifest.Slots.ObjectiveHeading;
        if (c.Applies(heading.AppliesTo))
        {
            c.AddPropagated(ObjectiveHeadingSlot, heading.Rect, heading.SizePt);
        }

        if (c.Link(ObjectiveBackAnchor) is null)
        {
            // No back link (links off): "Objective n" is baked where the link would sit, beside the printed heading,
            // so the eight detail pages printed from the same art still say which objective they hold.
            FixedAnchor back = c.Manifest.Links.ObjectiveBack;
            if (back.Erase)
            {
                c.Erase(back.Rect);
            }

            c.TextIn(back.Rect, LinkGraphBuilder.ObjectiveLabel(c.Page.IntToken(Tokens.ObjectiveNumber)), back.Align, back.SizePt);
        }
    }

    public static void Overview(RenderContext c)
    {
        DateSlots.DrawDate(c);
        OverviewDayHeaderAnchor header = c.Manifest.Links.OverviewDayHeader;
        foreach (LinkSpec link in c.LinksOfType(OverviewDayHeaderAnchor))
        {
            int i = int.Parse(link.AnchorInstance ?? "", CultureInfo.InvariantCulture);
            if (header.EraseCell(i) is RectPt erase)
            {
                c.Erase(erase);
            }

            c.AddLink(link.AnchorId, header.Rect(i), link.Text, header.Align, header.SizePt);
        }

        ObjectiveLegend(c);
        OverviewDayColumnsSlot columns = c.Manifest.Slots.OverviewDayColumns;
        if (c.Applies(columns.AppliesTo))
        {
            for (int i = 0; i < MonthGrid.ColumnCount; i++)
            {
                c.AddPropagated(OverviewDaySlot + ":" + i.ToString(CultureInfo.InvariantCulture), columns.Column(i), columns.SizePt, c.Manifest.Colors.EventInkOpacity);
            }
        }
    }

    public static void Day(RenderContext c)
    {
        Manifest m = c.Manifest;
        DateSlots.DrawDate(c);
        DateSlots.DrawWeeksRemaining(c);
        DateSlots.DrawDayCounter(c);
        TimeBlockColumn.Draw(c);
        ObjectiveLegend(c);

        RowsSlot goals = m.Slots.StandingGoals;
        if (c.Applies(goals.AppliesTo))
        {
            for (int i = 0; i < goals.Rows.Count; i++)
            {
                c.AddPropagated(StandingGoalSlot + ":" + (i + 1).ToString(CultureInfo.InvariantCulture), goals.Rows[i], goals.SizePt, m.Colors.PrefillInkOpacity);
            }
        }

        CellsSlot metrics = m.Slots.MetricNames;
        if (c.Applies(metrics.AppliesTo))
        {
            for (int i = 0; i < metrics.Cells.Count; i++)
            {
                c.AddPropagated(MetricNameSlot + ":" + (i + 1).ToString(CultureInfo.InvariantCulture), metrics.Cells[i], metrics.SizePt);
            }
        }

        BandSlot allDay = m.Slots.AllDayBand;
        if (c.Applies(allDay.AppliesTo))
        {
            c.AddPropagated(AllDayBandSlot, allDay.Rect, allDay.SizePt, m.Colors.EventInkOpacity);
        }
    }

    public static void Reflection(RenderContext c) => DateSlots.DrawDate(c);

    private static void ObjectiveLegend(RenderContext c)
    {
        BandSlot legend = c.Manifest.Slots.ObjectiveLegend;
        if (c.Applies(legend.AppliesTo))
        {
            c.AddPropagated(ObjectiveLegendSlot, legend.Rect, legend.SizePt);
        }
    }

    /// <summary>0 for the first Annual Objectives page, 1 for the continuation, from the page's PDF page in pageTypes.annual.</summary>
    private static int AnnualPageIndex(RenderContext c)
    {
        IReadOnlyList<int> pages = c.Manifest.PageTypes.Annual.PdfPages;
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] == c.Page.PdfPage)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Annual page {c.Page.Key} uses PDF page {c.Page.PdfPage}, which pageTypes.annual.pdfPages does not list.");
    }
}
