using System.Globalization;
using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Refresh.Calendar;
using Sfp.Render;

namespace Sfp.Refresh.Propagation;

/// <summary>Where a tool-owned text object sits on a page (sheet points, like <see cref="AnchorPlacement"/>) and how it is styled.</summary>
/// <param name="Rect">Rectangle in sheet points.</param>
/// <param name="Align">Paragraph alignment.</param>
/// <param name="SizePt">Font size in points.</param>
/// <param name="Opacity">Ink opacity (1.0 names and legends, 0.6 pre-fills, 0.7 events).</param>
public sealed record SlotGeometry(RectPt Rect, TextAlign Align, double SizePt, double Opacity)
{
    /// <summary>The placement the page XML builder takes for this slot with the given text.</summary>
    public AnchorPlacement ToPlacement(string slotId, string text) =>
        new(slotId, (float)Rect.X, (float)Rect.Y, (float)Rect.W, (float)Rect.H, text, Align, (float)SizePt)
        {
            Kind = AnchorKind.Propagated,
            Opacity = (float)Opacity,
        };
}

/// <summary>Slot ids of the Setup-derived propagation targets (the PRD Setup table), as recorded in the sidecar's anchor map.</summary>
public static class PropagationSlots
{
    /// <summary>The objective legend band on every Weekly Overview and daily page.</summary>
    public const string ObjectiveLegend = "objectiveLegend";

    /// <summary>The habit legend under each monthly calendar's tracker strips.</summary>
    public const string HabitLegend = "habitLegend";

    /// <summary>The objective title beside the Objective Details heading.</summary>
    public const string ObjectiveHeading = "objectiveHeading";

    public const string StandingGoalPrefix = "standingGoal";
    public const string MetricNamePrefix = "metricName";
    public const string AnnualRowPrefix = "annualRow";
    public const string ScorecardRowPrefix = "scorecardRow";

    /// <summary>"standingGoal:1" to "standingGoal:3" on every daily page.</summary>
    public static string StandingGoal(int i) => Id(StandingGoalPrefix, i);

    /// <summary>"metricName:1" to "metricName:3" on every daily page.</summary>
    public static string MetricName(int i) => Id(MetricNamePrefix, i);

    /// <summary>"annualRow:1" to "annualRow:8" on the annual pages (block n).</summary>
    public static string AnnualRow(int n) => Id(AnnualRowPrefix, n);

    /// <summary>"scorecardRow:1" to "scorecardRow:8" on the scorecard.</summary>
    public static string ScorecardRow(int n) => Id(ScorecardRowPrefix, n);

    /// <summary>Splits "prefix:instance" into its parts; the instance is null when there is no colon.</summary>
    public static (string Prefix, string? Instance) Split(string slotId)
    {
        ArgumentNullException.ThrowIfNull(slotId);
        int i = slotId.IndexOf(':', StringComparison.Ordinal);
        return i < 0 ? (slotId, null) : (slotId[..i], slotId[(i + 1)..]);
    }

    /// <summary>The integer instance of "prefix:n", or null.</summary>
    public static int? Index(string slotId)
    {
        (_, string? instance) = Split(slotId);
        return int.TryParse(instance, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    private static string Id(string prefix, int n) => prefix + ":" + n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The one place that knows where every tool-owned object sits on every page type: the Setup-derived propagation slots,
/// the calendar slots and the link anchors, all resolved from the manifest for a page of the plan. The propagator asks
/// it for geometry and blocking rectangles, the anchor writer for Create placements, and the sidecar recovery for the
/// rectangles it matches outlines against. Pure: the quarter, the date engine and the time-block window are given.
/// </summary>
public sealed class SlotCatalog
{
    private readonly IDateEngine dates;
    private readonly Dictionary<(int Year, int Month), MonthGrid> grids = new();
    private IReadOnlyList<Week>? weeks;

    /// <param name="manifest">Slot and link geometry.</param>
    /// <param name="dates">Month grids and weeks for the calendar pages.</param>
    /// <param name="quarter">The notebook's quarter.</param>
    /// <param name="window">The time-block window the daily pages were generated with (the Setup value, else the default).</param>
    public SlotCatalog(Manifest manifest, IDateEngine dates, Quarter quarter, TimeBlockWindow window)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(quarter);
        ArgumentNullException.ThrowIfNull(window);
        if (!window.IsValid)
        {
            throw new ArgumentException($"The time-block window {window.StartHour} to {window.EndHour} is not valid.", nameof(window));
        }

        Manifest = manifest;
        this.dates = dates;
        Quarter = quarter;
        Window = window;
    }

    public Manifest Manifest { get; }

    public Quarter Quarter { get; }

    public TimeBlockWindow Window { get; }

    /// <summary>Geometry of a propagation or calendar slot on a page, or null when the slot does not apply to that page.</summary>
    public SlotGeometry? Slot(PageSpec page, string slotId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(slotId);
        SlotsSpec slots = Manifest.Slots;
        ColorSpec colors = Manifest.Colors;
        (string prefix, string? instance) = PropagationSlots.Split(slotId);
        int? n = PropagationSlots.Index(slotId);
        switch (prefix)
        {
            case PropagationSlots.ObjectiveLegend:
                return Applies(slots.ObjectiveLegend.AppliesTo, page) ? Band(slots.ObjectiveLegend, 1.0) : null;
            case PropagationSlots.StandingGoalPrefix:
                return Applies(slots.StandingGoals.AppliesTo, page) && InRange(n, slots.StandingGoals.Rows.Count)
                    ? new SlotGeometry(slots.StandingGoals.Rows[n!.Value - 1], TextAlign.Left, slots.StandingGoals.SizePt, colors.PrefillInkOpacity)
                    : null;
            case PropagationSlots.MetricNamePrefix:
                return Applies(slots.MetricNames.AppliesTo, page) && InRange(n, slots.MetricNames.Cells.Count)
                    ? new SlotGeometry(slots.MetricNames.Cells[n!.Value - 1], TextAlign.Left, slots.MetricNames.SizePt, 1.0)
                    : null;
            case PropagationSlots.HabitLegend:
                return Applies(slots.HabitLegend.AppliesTo, page) ? new SlotGeometry(slots.HabitLegend.Rect, TextAlign.Left, slots.HabitLegend.SizePt, 1.0) : null;
            case PropagationSlots.AnnualRowPrefix:
                return AnnualRow(page, n);
            case PropagationSlots.ObjectiveHeading:
                return Applies(slots.ObjectiveHeading.AppliesTo, page) ? Band(slots.ObjectiveHeading, 1.0) : null;
            case PropagationSlots.ScorecardRowPrefix:
                return page.Type == PageType.Scorecard && InRange(n, Manifest.Generated.Scorecard.RowCount)
                    ? new SlotGeometry(Manifest.Generated.Scorecard.RowLabel(n!.Value - 1), TextAlign.Left, Manifest.Generated.Scorecard.LabelSizePt, 1.0)
                    : null;
            case CalendarSlots.AllDayBand:
                return Applies(slots.AllDayBand.AppliesTo, page) ? Band(slots.AllDayBand, colors.EventInkOpacity) : null;
            case "timeBlockEvent":
                return TimeBlockRow(page, n);
            case "overviewDay":
                return Applies(slots.OverviewDayColumns.AppliesTo, page) && n is >= 0 and <= 6
                    ? new SlotGeometry(slots.OverviewDayColumns.Column(n.Value), TextAlign.Left, slots.OverviewDayColumns.SizePt, colors.EventInkOpacity)
                    : null;
            case "calendarEvents":
                return CalendarCell(page, instance);
            default:
                return null;
        }
    }

    /// <summary>Geometry of a link anchor (a <see cref="LinkSpec.AnchorId"/>) on a page, or null when it is not one the page can carry.</summary>
    public SlotGeometry? Link(PageSpec page, string anchorId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(anchorId);
        LinksSpec links = Manifest.Links;
        (string type, string? instance) = PropagationSlots.Split(anchorId);
        int? n = PropagationSlots.Index(anchorId);
        switch (type)
        {
            case "annualObjective":
            {
                int perPage = Math.Max(1, links.AnnualObjective.BlockTops.Count);
                return n is int k && k >= 1 && page.Key == PageKey.Annual(((k - 1) / perPage) + 1)
                    ? new SlotGeometry(links.AnnualObjective.Rect((k - 1) % perPage), links.AnnualObjective.Align, links.AnnualObjective.SizePt, 1.0)
                    : null;
            }

            case "objectiveBack":
                return Fixed(links.ObjectiveBack);
            case "calendarDay":
            {
                (MonthGrid grid, DayCell cell, int row, int column)? found = Cell(page, instance);
                return found is null ? null : new SlotGeometry(links.CalendarDay.Rect(Manifest.Slots.DayGrid, found.Value.row, found.Value.column), links.CalendarDay.Align, links.CalendarDay.SizePt, 1.0);
            }

            case "calendarWeek":
            {
                int? row = WeekRow(page, n);
                return row is null ? null : new SlotGeometry(links.CalendarWeek.Rect(Manifest.Slots.DayGrid, row.Value), links.CalendarWeek.Align, links.CalendarWeek.SizePt, 1.0);
            }

            case "dailyDate":
                return new SlotGeometry(links.DailyDate.Rect, links.DailyDate.Align, links.DailyDate.SizePt, 1.0);
            case "overviewDayHeader":
                return n is >= 0 and <= 6 ? new SlotGeometry(links.OverviewDayHeader.Rect(n.Value), links.OverviewDayHeader.Align, links.OverviewDayHeader.SizePt, 1.0) : null;
            case "prev":
                return Fixed(links.Prev);
            case "next":
                return Fixed(links.Next);
            case "prevCalendar":
                return Fixed(links.PrevCalendar);
            case "nextCalendar":
                return Fixed(links.NextCalendar);
            case "reflectionNext":
                return Fixed(links.ReflectionNext);
            case "progressScorecard":
                return Fixed(links.ProgressScorecard);
            case "reflectionQuarterReview":
                return Fixed(links.ReflectionQuarterReview);
            case "calendarReview":
                return Fixed(links.CalendarReview);
            case "quarterReviewScorecard":
                return Fixed(links.QuarterReviewScorecard);
            case "nextQuarter":
                return Fixed(links.NextQuarter);
            case "notesContents":
                return Fixed(links.NotesContents);
            case "scorecardWeek":
                return n is int w && w >= 1 && page.Type == PageType.Scorecard
                    ? new SlotGeometry(Manifest.Generated.Scorecard.WeekHeader(w - 1), links.ScorecardWeek.Align, links.ScorecardWeek.SizePt, 1.0)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Geometry of any tool-owned object id on a page (slot or link anchor), or null.</summary>
    public SlotGeometry? Any(PageSpec page, string id) => Slot(page, id) ?? Link(page, id);

    /// <summary>Every Setup-derived slot id that applies to the page, in a stable order.</summary>
    public IReadOnlyList<string> PropagatedSlotsOn(PageSpec page)
    {
        ArgumentNullException.ThrowIfNull(page);
        List<string> ids = new();
        AddIf(ids, page, PropagationSlots.ObjectiveLegend);
        for (int i = 1; i <= Manifest.Slots.StandingGoals.Rows.Count; i++)
        {
            AddIf(ids, page, PropagationSlots.StandingGoal(i));
        }

        for (int i = 1; i <= Manifest.Slots.MetricNames.Cells.Count; i++)
        {
            AddIf(ids, page, PropagationSlots.MetricName(i));
        }

        AddIf(ids, page, PropagationSlots.HabitLegend);
        int objectives = Manifest.PageTypes.Objective.PdfPages.Count;
        for (int n = 1; n <= objectives; n++)
        {
            AddIf(ids, page, PropagationSlots.AnnualRow(n));
        }

        AddIf(ids, page, PropagationSlots.ObjectiveHeading);
        for (int n = 1; n <= Manifest.Generated.Scorecard.RowCount; n++)
        {
            AddIf(ids, page, PropagationSlots.ScorecardRow(n));
        }

        return ids;
    }

    /// <summary>Every calendar slot id that applies to the page: hour rows and the all-day band, overview columns, or the in-month calendar cells.</summary>
    public IReadOnlyList<string> CalendarSlotsOn(PageSpec page)
    {
        ArgumentNullException.ThrowIfNull(page);
        List<string> ids = new();
        AddIf(ids, page, CalendarSlots.AllDayBand);
        for (int h = Window.StartHour; h < Window.EndHour; h++)
        {
            AddIf(ids, page, CalendarSlots.TimeBlockEvent(h));
        }

        for (int i = 0; i < 7; i++)
        {
            AddIf(ids, page, CalendarSlots.OverviewDay(i));
        }

        if (page.Type == PageType.Calendar && page.Date is DateOnly first && Applies(Manifest.Slots.CalendarEventLines.AppliesTo, page))
        {
            foreach (DayCell cell in Grid(first.Year, first.Month).Cells.Where(c => c.InMonth))
            {
                ids.Add(CalendarSlots.CalendarEvents(cell.Date));
            }
        }

        return ids;
    }

    /// <summary>The manifest page name of a page ("mon", "overview", "calendar" ...) from its tokens, else from its type.</summary>
    public static string PageName(PageSpec page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Tokens.TryGetValue(Tokens.Extra.PdfPageName, out string? name) && !string.IsNullOrEmpty(name))
        {
            return name;
        }

        return page.Type switch
        {
            PageType.Day => PageNames.Days[PlanBuilder.WeekdayIndex(page.Date ?? Quarter_Start(page))],
            PageType.WeeklyOverview => PageNames.Overview,
            PageType.WeeklyReflection => PageNames.Reflection,
            PageType.Calendar => PageNames.Calendar,
            PageType.Annual => PageNames.Annual,
            PageType.Objective => PageNames.Objective,
            PageType.Scorecard => PageNames.Scorecard,
            PageType.MonthlyReview => PageNames.MonthlyReview,
            PageType.QuarterlyReview => PageNames.QuarterlyReview,
            PageType.Notes => PageNames.Notes,
            PageType.Cover => PageNames.Cover,
            PageType.Contents => PageNames.Contents,
            PageType.Setup => PageNames.Setup,
            _ => PageNames.Blank,
        };
    }

    private static DateOnly Quarter_Start(PageSpec page) =>
        page.Date ?? throw new InvalidOperationException($"Day page {page.Key} has no date.");

    private static bool Applies(IReadOnlyList<string> appliesTo, PageSpec page) => appliesTo.Contains(PageName(page), StringComparer.Ordinal);

    private static bool InRange(int? n, int count) => n is int i && i >= 1 && i <= count;

    private static SlotGeometry Band(BandSlot band, double opacity) => new(band.Rect, TextAlign.Left, band.SizePt, opacity);

    private static SlotGeometry Fixed(FixedAnchor anchor) => new(anchor.Rect, anchor.Align, anchor.SizePt, 1.0);

    private void AddIf(List<string> ids, PageSpec page, string slotId)
    {
        if (Slot(page, slotId) is not null)
        {
            ids.Add(slotId);
        }
    }

    private SlotGeometry? AnnualRow(PageSpec page, int? n)
    {
        AnnualTitleSlot slot = Manifest.Slots.AnnualTitle;
        if (page.Type != PageType.Annual || !Applies(slot.AppliesTo, page) || n is not int k || k < 1)
        {
            return null;
        }

        int perPage = Math.Max(1, slot.BlockTops.Count);
        return page.Key == PageKey.Annual(((k - 1) / perPage) + 1)
            ? new SlotGeometry(slot.Rect((k - 1) % perPage), slot.Align, slot.SizePt, 1.0)
            : null;
    }

    private SlotGeometry? TimeBlockRow(PageSpec page, int? hour)
    {
        TimeBlocksSlot column = Manifest.Slots.TimeBlocks;
        if (!Applies(column.AppliesTo, page) || hour is not int h || h < Window.StartHour || h >= Window.EndHour)
        {
            return null;
        }

        double pitch = column.PitchFor(Window.RowCount);
        RectPt rect = new(column.EventTextX, column.Top + ((h - Window.StartHour) * pitch), column.ColumnRight - column.EventTextX, pitch);
        return new SlotGeometry(rect, TextAlign.Left, column.EventSizePt, Manifest.Colors.EventInkOpacity);
    }

    private SlotGeometry? CalendarCell(PageSpec page, string? instance)
    {
        CalendarEventLinesSlot lines = Manifest.Slots.CalendarEventLines;
        if (!Applies(lines.AppliesTo, page))
        {
            return null;
        }

        (MonthGrid grid, DayCell cell, int row, int column)? found = Cell(page, instance);
        if (found is null)
        {
            return null;
        }

        DayGridSlot dayGrid = Manifest.Slots.DayGrid;
        RectPt box = dayGrid.Box(found.Value.row, found.Value.column);
        RectPt rect = new(box.X + lines.XOffset, box.Bottom + lines.OffsetBelowBox, lines.W, lines.LineHeight * Math.Max(1, lines.MaxLines));
        return new SlotGeometry(rect, TextAlign.Left, lines.SizePt, Manifest.Colors.EventInkOpacity);
    }

    /// <summary>The in-month grid cell of a date on a calendar page, with its row and column, or null.</summary>
    private (MonthGrid Grid, DayCell Cell, int Row, int Column)? Cell(PageSpec page, string? isoDate)
    {
        if (page.Type != PageType.Calendar || page.Date is not DateOnly first
            || !DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return null;
        }

        MonthGrid grid = Grid(first.Year, first.Month);
        for (int r = 0; r < grid.Rows.Count; r++)
        {
            for (int c = 0; c < grid.Rows[r].Count; c++)
            {
                DayCell cell = grid.Rows[r][c];
                if (cell.InMonth && cell.Date == date)
                {
                    return (grid, cell, r, c);
                }
            }
        }

        return null;
    }

    /// <summary>The grid row whose Monday starts week NN on a calendar page, or null.</summary>
    private int? WeekRow(PageSpec page, int? week)
    {
        if (page.Type != PageType.Calendar || page.Date is not DateOnly first || week is not int w)
        {
            return null;
        }

        weeks ??= dates.Weeks(Quarter);
        if (w < 1 || w > weeks.Count)
        {
            return null;
        }

        DateOnly monday = weeks[w - 1].MondayStart;
        MonthGrid grid = Grid(first.Year, first.Month);
        for (int r = 0; r < grid.Rows.Count; r++)
        {
            if (grid.Rows[r][0].Date == monday)
            {
                return r;
            }
        }

        return null;
    }

    private MonthGrid Grid(int year, int month)
    {
        if (!grids.TryGetValue((year, month), out MonthGrid? grid))
        {
            grid = dates.Grid(year, month, Quarter);
            grids[(year, month)] = grid;
        }

        return grid;
    }
}
