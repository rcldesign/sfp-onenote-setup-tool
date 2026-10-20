using System.Globalization;
using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.8: the pages the tool draws itself in the template style from manifest.generated: Setup (labels, numbered ruled
/// slots, the time-block window boxes with hint digits, hint lines), the Quarter scorecard (objective rows, week
/// columns whose headers are link placements, or baked week numbers when links are off, a final Quarter column, row
/// label cells as propagated placements), the
/// monthly and quarterly reviews (the week-13 reflection art with its heading erased and the review title drawn), and
/// the ruled Notes pages. Headings use generated.heading, the same spot as the printed page titles.
/// </summary>
internal static class GeneratedPages
{
    public const string ScorecardRowSlot = "scorecardRow";
    public const string ScorecardWeekAnchor = "scorecardWeek";
    public const string QuarterColumnTitle = "Quarter";
    public const string WindowSeparator = "to";

    /// <summary>The page title drawn where the printed pages carry theirs.</summary>
    public static void Heading(RenderContext c, string text)
    {
        HeadingSpec h = c.Manifest.Generated.Heading;
        c.Text(text, h.X, h.BaselineY, TextAlign.Left, h.SizePt);
    }

    public static void Setup(RenderContext c)
    {
        SetupPageSpec s = c.Manifest.Generated.Setup;
        Heading(c, c.Page.Title);
        foreach (LabelSpec label in s.Labels)
        {
            c.Text(label.Text, label.X, label.BaselineY, TextAlign.Left, s.LabelSizePt);
        }

        NumberedSlots(c, s, s.Objectives);
        NumberedSlots(c, s, s.StandingGoals);
        NumberedSlots(c, s, s.MetricNames);
        NumberedSlots(c, s, s.Habits);
        NumberedSlots(c, s, s.CalendarSources);

        TimeBlockWindow window = c.Generate.TimeBlocks;
        WindowBox(c, s, s.TimeBlockStart, window.StartHour);
        WindowBox(c, s, s.TimeBlockEnd, window.EndHour);
        double gapCenter = (s.TimeBlockStart.Right + s.TimeBlockEnd.X) / 2;
        c.Text(WindowSeparator, gapCenter, SlotBaseline(c, s.TimeBlockStart, s.SlotSizePt), TextAlign.Center, s.SlotSizePt);

        foreach (LabelSpec hint in s.Hints)
        {
            c.Text(hint.Text, hint.X, hint.BaselineY, TextAlign.Left, s.HintSizePt, c.Manifest.Colors.PrefillInkOpacity);
        }
    }

    public static void Scorecard(RenderContext c)
    {
        ScorecardPageSpec s = c.Manifest.Generated.Scorecard;
        StyleOnlyAnchor header = c.Manifest.Links.ScorecardWeek;
        Heading(c, c.Page.Title);

        int weeks = c.Plan.Quarter.WeekCount;
        double right = s.FinalColumnX + s.FinalColumnW;
        double top = s.FirstRowTop;
        double bottom = s.FirstRowTop + (s.RowPitch * s.RowCount);

        for (int week = 1; week <= weeks; week++)
        {
            string nn = PlannerText.WeekNumber(week);
            RectPt cell = s.WeekHeader(week - 1);
            LinkSpec? link = c.Link(ScorecardWeekAnchor + ":" + nn);
            if (link is not null)
            {
                c.AddLink(link.AnchorId, cell, link.Text, header.Align, header.SizePt);
            }
            else
            {
                // No link to the week's reflection (links off): the column header is baked so the grid still reads.
                c.TextIn(cell, nn, header.Align, header.SizePt, s.HeaderBaselineY);
            }
        }

        c.Text(QuarterColumnTitle, s.FinalColumnX + (s.FinalColumnW / 2), s.HeaderBaselineY, TextAlign.Center, s.LabelSizePt);

        for (int row = 0; row <= s.RowCount; row++)
        {
            double y = s.FirstRowTop + (s.RowPitch * row);
            c.Rule(s.RowLabelX, y, right, y, s.RuleWidthPt);
        }

        c.Rule(s.RowLabelX, top, s.RowLabelX, bottom, s.RuleWidthPt);
        for (int column = 0; column <= weeks; column++)
        {
            double x = s.ColumnX0 + (s.ColumnPitch * column);
            c.Rule(x, top, x, bottom, s.RuleWidthPt);
        }

        c.Rule(s.FinalColumnX, top, s.FinalColumnX, bottom, s.RuleWidthPt);
        c.Rule(right, top, right, bottom, s.RuleWidthPt);

        for (int row = 0; row < s.RowCount; row++)
        {
            c.AddPropagated(ScorecardRowSlot + ":" + (row + 1).ToString(CultureInfo.InvariantCulture), s.RowLabel(row), s.LabelSizePt);
        }
    }

    /// <summary>A monthly or quarterly review: the reflection art with the printed heading and the empty date slot erased, retitled.</summary>
    public static void Review(RenderContext c)
    {
        c.Erase(c.Manifest.Generated.Reviews.HeadingErase);
        c.Erase(c.Manifest.Slots.Date.SlashErase);
        Heading(c, c.Page.Title);
    }

    public static void Notes(RenderContext c)
    {
        NotesPageSpec n = c.Manifest.Generated.Notes;
        Heading(c, c.Page.Title);
        if (n.RulePitch <= 0)
        {
            throw new InvalidOperationException("generated.notes.rulePitch must be positive.");
        }

        for (double y = n.RulesTop; y <= n.RulesBottom + 0.001; y += n.RulePitch)
        {
            c.Rule(n.RuleX, y, n.RuleRight, y, n.RuleWidthPt);
        }
    }

    /// <summary>Number of rules a Notes page carries, for tests and the reviewer notes.</summary>
    public static int NotesRuleCount(NotesPageSpec n)
    {
        ArgumentNullException.ThrowIfNull(n);
        return (int)Math.Floor(((n.RulesBottom - n.RulesTop) / n.RulePitch) + 0.001) + 1;
    }

    private static void NumberedSlots(RenderContext c, SetupPageSpec s, IReadOnlyList<RectPt> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            RectPt slot = slots[i];
            double baseline = SlotBaseline(c, slot, s.SlotSizePt);
            c.Text((i + 1).ToString(CultureInfo.InvariantCulture), slot.X - 6, baseline, TextAlign.Right, s.SlotSizePt);
            c.Rule(slot.X, slot.Bottom, slot.Right, slot.Bottom, s.RuleWidthPt);
        }
    }

    private static void WindowBox(RenderContext c, SetupPageSpec s, RectPt box, int hour)
    {
        c.RectOutline(box, s.RuleWidthPt);
        string digits = hour.ToString("00", CultureInfo.InvariantCulture);
        c.Text(digits, box.CenterX, SlotBaseline(c, box, s.SlotSizePt), TextAlign.Center, s.SlotSizePt, c.Manifest.Colors.GreyedInkOpacity);
    }

    /// <summary>A baseline that centers capitals vertically in a slot rectangle.</summary>
    private static double SlotBaseline(RenderContext c, RectPt slot, double sizePt) => slot.CenterY + (c.CapHeight(sizePt) / 2);
}
