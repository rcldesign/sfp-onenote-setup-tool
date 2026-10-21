using System.Text.Json.Serialization;

namespace Sfp.Core.Manifests;

/// <summary>
/// The template description, mirroring templates/sfp-quarterly.json one to one. Everything measured on the PDF lives
/// here, never in code. Loaded and merged by the manifest loader (M1.2); validated against the PDF by <see cref="IManifestValidator"/>.
/// Coordinates are PDF points with a top-left origin.
/// </summary>
public sealed record Manifest
{
    public required string TemplateId { get; init; }

    /// <summary>[width, height] in points.</summary>
    public required IReadOnlyList<double> PageSizePt { get; init; }

    public required int PageCount { get; init; }

    public required FontSpec Font { get; init; }

    public required ColorSpec Colors { get; init; }

    public required PageTypesSpec PageTypes { get; init; }

    public required SlotsSpec Slots { get; init; }

    public required LinksSpec Links { get; init; }

    public required GeneratedSpec Generated { get; init; }

    /// <summary>The JSON key is "onenote" (one word, as the PRD writes it), not the camelCase "oneNote".</summary>
    [JsonPropertyName("onenote")]
    public required OneNoteSpec OneNote { get; init; }

    public required NotebookSpec Notebook { get; init; }

    [JsonIgnore]
    public double PageWidthPt => PageSizePt[0];

    [JsonIgnore]
    public double PageHeightPt => PageSizePt[1];
}

/// <summary>A rectangle in PDF points, top-left origin.</summary>
public readonly record struct RectPt(double X, double Y, double W, double H)
{
    [JsonIgnore]
    public double Right => X + W;

    [JsonIgnore]
    public double Bottom => Y + H;

    [JsonIgnore]
    public double CenterX => X + (W / 2);

    [JsonIgnore]
    public double CenterY => Y + (H / 2);

    /// <summary>True when the point lies inside the rectangle (edges inclusive).</summary>
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>True when the two rectangles overlap by more than a hairline.</summary>
    public bool Overlaps(RectPt other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>A line segment in PDF points.</summary>
public sealed record LinePt(double X1, double Y1, double X2, double Y2);

public sealed record FontSpec
{
    public required string Family { get; init; }

    public required string Fallback { get; init; }

    /// <summary>Relative paths (from the templates folder) of the fallback face files by style: regular, bold, italic, boldItalic.</summary>
    public required IReadOnlyDictionary<string, string> FallbackFiles { get; init; }
}

public sealed record ColorSpec
{
    public required string Background { get; init; }

    public required string Ink { get; init; }

    public required string DayBox { get; init; }

    /// <summary>Three section tints cycling by month.</summary>
    public required IReadOnlyList<string> SectionTints { get; init; }

    /// <summary>Ink opacity of out-of-quarter and out-of-month dates (the PRD's 40%).</summary>
    public required double GreyedInkOpacity { get; init; }

    /// <summary>Ink opacity of propagated pre-fills such as standing goals (the PRD's 60%).</summary>
    public required double PrefillInkOpacity { get; init; }

    /// <summary>Ink opacity of calendar event lines (the PRD's 70%).</summary>
    public required double EventInkOpacity { get; init; }

    /// <summary>Ink opacity of the day counter (the PRD's 60%).</summary>
    public required double CounterInkOpacity { get; init; }
}

public sealed record PageTypesSpec
{
    public required FixedPagesSpec Cover { get; init; }

    public required FixedPagesSpec Contents { get; init; }

    public required FixedPagesSpec Annual { get; init; }

    public required FixedPagesSpec Objective { get; init; }

    public required FixedPagesSpec Calendar { get; init; }

    public required WeeklyPagesSpec Weekly { get; init; }
}

/// <summary>A page type that maps to fixed PDF pages.</summary>
public sealed record FixedPagesSpec
{
    /// <summary>1-based PDF page numbers.</summary>
    public required IReadOnlyList<int> PdfPages { get; init; }

    /// <summary>Text that must appear on the first of these pages.</summary>
    public required string Probe { get; init; }
}

/// <summary>The 13 nine-page week runs.</summary>
public sealed record WeeklyPagesSpec
{
    public required int FirstPdfPage { get; init; }

    public required int Stride { get; init; }

    public required int Count { get; init; }

    /// <summary>Page names within a run, in PDF order: overview, mon ... sun, reflection.</summary>
    public required IReadOnlyList<string> Pages { get; init; }

    /// <summary>Probe text per page name.</summary>
    public required IReadOnlyDictionary<string, string> Probes { get; init; }

    /// <summary>1-based PDF page for a page name in week run <paramref name="run1"/> (1-based, clamped to <see cref="Count"/>).</summary>
    public int PdfPageFor(int run1, string pageName)
    {
        int run = Math.Clamp(run1, 1, Count);
        int offset = ((List<string>)Pages.ToList()).IndexOf(pageName);
        if (offset < 0)
        {
            throw new ArgumentException($"Unknown weekly page name '{pageName}'.", nameof(pageName));
        }

        return FirstPdfPage + (Stride * (run - 1)) + offset;
    }
}

/// <summary>All baked and propagated slots.</summary>
public sealed record SlotsSpec
{
    public required DateSlot Date { get; init; }

    public required WeeksRemainingSlot WeeksRemaining { get; init; }

    public required MonthNameSlot MonthName { get; init; }

    public required DayGridSlot DayGrid { get; init; }

    public required CoverLabelSlot CoverLabel { get; init; }

    public required DayCounterSlot DayCounter { get; init; }

    public required BandSlot ObjectiveLegend { get; init; }

    public required RowsSlot StandingGoals { get; init; }

    public required CellsSlot MetricNames { get; init; }

    public required HabitLegendSlot HabitLegend { get; init; }

    public required TimeBlocksSlot TimeBlocks { get; init; }

    public required BandSlot AllDayBand { get; init; }

    public required OverviewDayColumnsSlot OverviewDayColumns { get; init; }

    public required CalendarEventLinesSlot CalendarEventLines { get; init; }

    /// <summary>The objective title cell in the header band of each Annual Objectives block (propagated "annualRow:{n}").</summary>
    public required AnnualTitleSlot AnnualTitle { get; init; }

    /// <summary>The objective title beside the heading of each Objective Details page (propagated "objectiveHeading").</summary>
    public required BandSlot ObjectiveHeading { get; init; }

    /// <summary>Every slot's manifest name with the page names it applies to, in manifest order.</summary>
    public IReadOnlyList<(string Slot, IReadOnlyList<string> AppliesTo)> AppliesToBySlot() => new[]
    {
        ("date", Date.AppliesTo),
        ("weeksRemaining", WeeksRemaining.AppliesTo),
        ("monthName", MonthName.AppliesTo),
        ("dayGrid", DayGrid.AppliesTo),
        ("coverLabel", CoverLabel.AppliesTo),
        ("dayCounter", DayCounter.AppliesTo),
        ("objectiveLegend", ObjectiveLegend.AppliesTo),
        ("standingGoals", StandingGoals.AppliesTo),
        ("metricNames", MetricNames.AppliesTo),
        ("habitLegend", HabitLegend.AppliesTo),
        ("timeBlocks", TimeBlocks.AppliesTo),
        ("allDayBand", AllDayBand.AppliesTo),
        ("overviewDayColumns", OverviewDayColumns.AppliesTo),
        ("calendarEventLines", CalendarEventLines.AppliesTo),
        ("annualTitle", AnnualTitle.AppliesTo),
        ("objectiveHeading", ObjectiveHeading.AppliesTo),
    };
}

/// <summary>
/// Annual Objectives title cells: one per block, x fixed, y = block top + offset (the "annualRow:{n}" slots Refresh fills).
/// The block tops repeat links.annualObjective.blockTops so a consumer can address a block by index.
/// </summary>
public sealed record AnnualTitleSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double X { get; init; }

    /// <summary>Tops of the four objective blocks on an annual page, the same values as links.annualObjective.blockTops.</summary>
    public required IReadOnlyList<double> BlockTops { get; init; }

    public required double YOffset { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>The cell rectangle for a block whose top is <paramref name="blockTop"/>.</summary>
    public RectPt Rect(double blockTop) => new(X, blockTop + YOffset, W, H);

    /// <summary>Rectangle for block index 0 to 3 on a page.</summary>
    public RectPt Rect(int blockIndex) => Rect(BlockTops[blockIndex]);
}

public sealed record DateField(string Name, TextAlign Align, double X);

public sealed record DateSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    /// <summary>Page names where the date is a link anchor (slashes erased) instead of baked text.</summary>
    public IReadOnlyList<string> LinkOn { get; init; } = Array.Empty<string>();

    public required double SizePt { get; init; }

    public required double BaselineY { get; init; }

    public required IReadOnlyList<DateField> Fields { get; init; }

    public IReadOnlyList<LinePt> Slashes { get; init; } = Array.Empty<LinePt>();

    public required RectPt SlashErase { get; init; }

    /// <summary>Ink bounds of the printed slashes, measured on the PDF; <see cref="SlashErase"/> must contain them.</summary>
    public required RectPt SlashPrinted { get; init; }
}

public sealed record Span(double From, double To);

public sealed record WeeksRemainingSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required RectPt Erase { get; init; }

    /// <summary>Ink bounds of the printed numbers and disc over every week run, measured on the PDF; <see cref="Erase"/> must contain them.</summary>
    public required RectPt Printed { get; init; }

    public required double SizePt { get; init; }

    public required double BaselineY { get; init; }

    /// <summary>Printed left edges of the 13 numbers.</summary>
    public required IReadOnlyList<double> NumberX { get; init; }

    public required Span NumberSpan { get; init; }

    public required double CircleDiameterPt { get; init; }

    public required double CircleCenterY { get; init; }

    public RectPt LabelRect { get; init; }
}

public sealed record MonthNameSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double X { get; init; }

    public required double BaselineY { get; init; }

    public required double SizePt { get; init; }

    public MonthRule? Rule { get; init; }
}

public sealed record MonthRule(double X1, double X2, double Y);

public sealed record DayGridSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double SizePt { get; init; }

    public required IReadOnlyList<double> ColumnX { get; init; }

    public required IReadOnlyList<double> RowY { get; init; }

    public required double BoxPt { get; init; }

    /// <summary>
    /// Air kept between a stacked sixth-row string ("23 / 30") and each side of its day box: the string is set smaller
    /// when it is wider than <see cref="BoxPt"/> less this on both sides.
    /// </summary>
    public required double StackedInsetPt { get; init; }

    /// <summary>Width of one grid cell (the distance between column origins).</summary>
    public required double ColumnPitch { get; init; }

    /// <summary>Height of one grid cell (the distance between row origins).</summary>
    public required double RowPitch { get; init; }

    /// <summary>Baseline of the printed Monday to Sunday headers above the grid.</summary>
    public required double DayHeaderY { get; init; }

    public TrackerStripSpec? TrackerStrip { get; init; }

    public NotesColumnSpec? NotesColumn { get; init; }

    /// <summary>The day-number box of a cell as a rectangle.</summary>
    public RectPt Box(int row, int column) => new(ColumnX[column], RowY[row], BoxPt, BoxPt);

    /// <summary>The whole cell as a rectangle.</summary>
    public RectPt Cell(int row, int column) => new(ColumnX[column], RowY[row], ColumnPitch, RowPitch);
}

public sealed record TrackerStripSpec(double BoxPt, int Count, double HeightFromCellBottom);

public sealed record NotesColumnSpec(double X, double Right, int Lines);

public sealed record CoverLabelSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double X { get; init; }

    public required IReadOnlyList<double> Baselines { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Center;
}

public sealed record DayCounterSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double RightX { get; init; }

    public required double BaselineY { get; init; }

    public required double SizePt { get; init; }

    /// <summary>Format with {dayOfQuarter}, {quarterLength}, {week}, {weekCount}.</summary>
    public string Format { get; init; } = "Day {dayOfQuarter} of {quarterLength} · Week {week} of {weekCount}";
}

/// <summary>A single-line text band that Refresh fills (legend, all-day events).</summary>
public sealed record BandSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required RectPt Rect { get; init; }

    public required double SizePt { get; init; }

    public double? MaxWidthPt { get; init; }

    public string Separator { get; init; } = "  ";
}

/// <summary>A list of row rectangles that Refresh fills one value per row (standing goals).</summary>
public sealed record RowsSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double SizePt { get; init; }

    public required IReadOnlyList<RectPt> Rows { get; init; }
}

/// <summary>A list of cell rectangles that Refresh fills one value per cell (metric names).</summary>
public sealed record CellsSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double SizePt { get; init; }

    public required IReadOnlyList<RectPt> Cells { get; init; }
}

public sealed record HabitLegendSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required RectPt Rect { get; init; }

    public required double SizePt { get; init; }

    /// <summary>Habit entries per legend line.</summary>
    public required int Columns { get; init; }

    /// <summary>Distance between the legend's lines.</summary>
    public required double LineHeight { get; init; }
}

public sealed record TimeBlocksSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required RectPt Erase { get; init; }

    /// <summary>Ink bounds of the printed hour and half-hour rules and hour labels, measured on the PDF; <see cref="Erase"/> must contain them.</summary>
    public required RectPt Printed { get; init; }

    public required double ColumnX { get; init; }

    public required double ColumnRight { get; init; }

    public required double Top { get; init; }

    public required double Bottom { get; init; }

    public required double LabelRightX { get; init; }

    public required double LabelSizePt { get; init; }

    /// <summary>Size of the hour labels the PDF prints in the column (7 pt).</summary>
    public required double PrintedLabelSizePt { get; init; }

    /// <summary>Number of hour rules the PDF prints in the column (25: 12, 1 ... 12, 1 ... 12).</summary>
    public required int PrintedRows { get; init; }

    /// <summary>Distance between the printed hour rules.</summary>
    public required double PrintedPitch { get; init; }

    public required double RuleWidthPt { get; init; }

    public required double EventTextX { get; init; }

    public required double EventSizePt { get; init; }

    /// <summary>Height of one calendar event line in a Time Blocks row.</summary>
    public required double EventLineHeight { get; init; }

    /// <summary>Row pitch for a window of <paramref name="rows"/> hourly rows.</summary>
    public double PitchFor(int rows) => (Bottom - Top) / rows;
}

public sealed record OverviewDayColumnsSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double X0 { get; init; }

    public required double Pitch { get; init; }

    public required double ColumnW { get; init; }

    public required double Top { get; init; }

    public required double Bottom { get; init; }

    public required double SizePt { get; init; }

    public required double LineHeight { get; init; }

    public required int MaxLines { get; init; }

    /// <summary>Column rectangle for weekday index 0 (Monday) to 6 (Sunday).</summary>
    public RectPt Column(int weekdayIndex) => new(X0 + (Pitch * weekdayIndex), Top, ColumnW, Bottom - Top);
}

public sealed record CalendarEventLinesSlot
{
    public required IReadOnlyList<string> AppliesTo { get; init; }

    public required double OffsetBelowBox { get; init; }

    public required double XOffset { get; init; }

    public required double W { get; init; }

    public required double SizePt { get; init; }

    public required double LineHeight { get; init; }

    public required int MaxLines { get; init; }
}

/// <summary>Geometry of every link anchor type. Each anchor's AnchorId prefix is its property name in camelCase.</summary>
public sealed record LinksSpec
{
    public required AnnualObjectiveAnchor AnnualObjective { get; init; }

    public required FixedAnchor ObjectiveBack { get; init; }

    public required CalendarDayAnchor CalendarDay { get; init; }

    public required CalendarWeekAnchor CalendarWeek { get; init; }

    public required RightAlignedAnchor DailyDate { get; init; }

    public required OverviewDayHeaderAnchor OverviewDayHeader { get; init; }

    public required FixedAnchor Prev { get; init; }

    public required FixedAnchor Next { get; init; }

    public required FixedAnchor PrevCalendar { get; init; }

    public required FixedAnchor NextCalendar { get; init; }

    public required FixedAnchor ReflectionNext { get; init; }

    public required FixedAnchor ProgressScorecard { get; init; }

    public required StyleOnlyAnchor ScorecardWeek { get; init; }

    public required FixedAnchor CalendarReview { get; init; }

    public required FixedAnchor ReflectionQuarterReview { get; init; }

    public required FixedAnchor QuarterReviewScorecard { get; init; }

    public required FixedAnchor NextQuarter { get; init; }

    public required FixedAnchor NotesContents { get; init; }
}

/// <summary>An anchor at a fixed position on its page.</summary>
public sealed record FixedAnchor
{
    public required double X { get; init; }

    public required double Y { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>Erase the printed art under the anchor before placing it (true when the PDF prints the same text there).</summary>
    public bool Erase { get; init; }

    /// <summary>Optional text pattern with {title} for navigation anchors.</summary>
    public string? TextFormat { get; init; }

    [JsonIgnore]
    public RectPt Rect => new(X, Y, W, H);
}

/// <summary>An anchor whose right edge is fixed (the daily page date).</summary>
public sealed record RightAlignedAnchor
{
    public required double RightX { get; init; }

    public required double Y { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Right;

    [JsonIgnore]
    public RectPt Rect => new(RightX - W, Y, W, H);
}

/// <summary>Annual Objectives block labels: x fixed, y = block top + offset.</summary>
public sealed record AnnualObjectiveAnchor
{
    public required double X { get; init; }

    public required IReadOnlyList<double> BlockTops { get; init; }

    public required double YOffset { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;

    public bool Erase { get; init; } = true;

    /// <summary>Rectangle for block index 0 to 3 on a page.</summary>
    public RectPt Rect(int blockIndex) => new(X, BlockTops[blockIndex] + YOffset, W, H);
}

/// <summary>Calendar day numbers: x = column origin, y = row origin + offset.</summary>
public sealed record CalendarDayAnchor
{
    public required double YOffset { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Center;

    public RectPt Rect(DayGridSlot grid, int row, int column) => new(grid.ColumnX[column], grid.RowY[row] + YOffset, W, H);
}

/// <summary>Calendar "Week NN" labels in the Monday cell, right of the day box.</summary>
public sealed record CalendarWeekAnchor
{
    public required double XOffset { get; init; }

    public required double YOffset { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;

    public RectPt Rect(DayGridSlot grid, int row) => new(grid.ColumnX[0] + XOffset, grid.RowY[row] + YOffset, W, H);
}

/// <summary>Weekly Overview day-name headers: x = x0 + pitch * i.</summary>
public sealed record OverviewDayHeaderAnchor
{
    public required double X0 { get; init; }

    public required double Pitch { get; init; }

    public required double Y { get; init; }

    public required double W { get; init; }

    public required double H { get; init; }

    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Center;

    /// <summary>Band erased before the names are redrawn as links.</summary>
    public RectPt? Erase { get; init; }

    /// <summary>When set, the band is erased one cell at a time, inset this much from each column boundary so the printed column rules survive.</summary>
    public double EraseInsetX { get; init; }

    public RectPt Rect(int weekdayIndex) => new(X0 + (Pitch * weekdayIndex), Y, W, H);

    /// <summary>The erase rectangle for one header cell (the band clipped to the column, inset by <see cref="EraseInsetX"/>).</summary>
    public RectPt? EraseCell(int weekdayIndex)
    {
        if (Erase is not RectPt band)
        {
            return null;
        }

        double left = X0 + (Pitch * weekdayIndex) + EraseInsetX;
        return new RectPt(left, band.Y, Pitch - (2 * EraseInsetX), band.H);
    }
}

/// <summary>An anchor whose position comes from a generated page's own geometry (scorecard column headers).</summary>
public sealed record StyleOnlyAnchor
{
    public required double SizePt { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Center;
}

/// <summary>Geometry of the pages the tool draws itself in the template style.</summary>
public sealed record GeneratedSpec
{
    public required HeadingSpec Heading { get; init; }

    public required NotesPageSpec Notes { get; init; }

    public required ReviewsPageSpec Reviews { get; init; }

    public required ScorecardPageSpec Scorecard { get; init; }

    public required SetupPageSpec Setup { get; init; }
}

public sealed record HeadingSpec(double X, double BaselineY, double SizePt);

public sealed record NotesPageSpec
{
    public required double RulesTop { get; init; }

    public required double RulesBottom { get; init; }

    public required double RulePitch { get; init; }

    public required double RuleX { get; init; }

    public required double RuleRight { get; init; }

    public required double RuleWidthPt { get; init; }
}

public sealed record ReviewsPageSpec
{
    /// <summary>Rectangle over the printed "Weekly Reflection" heading, erased before the review title is drawn.</summary>
    public required RectPt HeadingErase { get; init; }
}

public sealed record ScorecardPageSpec
{
    public required double LabelSizePt { get; init; }

    public required double RowLabelX { get; init; }

    public required double RowLabelW { get; init; }

    public required double HeaderBaselineY { get; init; }

    public required double FirstRowTop { get; init; }

    public required double RowPitch { get; init; }

    public required int RowCount { get; init; }

    public required double ColumnX0 { get; init; }

    public required double ColumnPitch { get; init; }

    public required double FinalColumnX { get; init; }

    public required double FinalColumnW { get; init; }

    public required double RuleWidthPt { get; init; }

    /// <summary>Header cell for week column index 0-based.</summary>
    public RectPt WeekHeader(int weekIndex) => new(ColumnX0 + (ColumnPitch * weekIndex), HeaderBaselineY - 12, ColumnPitch, 14);

    /// <summary>Objective title cell for row index 0-based.</summary>
    public RectPt RowLabel(int rowIndex) => new(RowLabelX, FirstRowTop + (RowPitch * rowIndex) + 4, RowLabelW, 14);
}

public sealed record LabelSpec(string Text, double X, double BaselineY);

public sealed record SetupPageSpec
{
    public required double LabelSizePt { get; init; }

    public required double SlotSizePt { get; init; }

    public required double HintSizePt { get; init; }

    public required double RuleWidthPt { get; init; }

    public required IReadOnlyList<LabelSpec> Labels { get; init; }

    public required IReadOnlyList<RectPt> Objectives { get; init; }

    public required IReadOnlyList<RectPt> StandingGoals { get; init; }

    public required IReadOnlyList<RectPt> MetricNames { get; init; }

    public required IReadOnlyList<RectPt> Habits { get; init; }

    public required IReadOnlyList<RectPt> CalendarSources { get; init; }

    public required RectPt TimeBlockStart { get; init; }

    public required RectPt TimeBlockEnd { get; init; }

    public IReadOnlyList<LabelSpec> Hints { get; init; } = Array.Empty<LabelSpec>();
}

public sealed record PointPt(double X, double Y);

/// <summary>OneNote writer settings measured in the M0 spike.</summary>
public sealed record OneNoteSpec
{
    /// <summary>Height of the title area; the sheet outline sits at this y on a titled page.</summary>
    public required double TitleOffsetPt { get; init; }

    public bool HideTitle { get; init; }

    /// <summary>PageSettings color; "automatic" if OneNote rejects a hex value.</summary>
    public required string PageColor { get; init; }

    /// <summary>Offset added to every anchor outline position to compensate for OneNote's internal padding.</summary>
    public required PointPt AnchorPaddingPt { get; init; }

    public bool AnchorUnderline { get; init; }

    /// <summary>Hour (local) used for one:Page dateTime on the planner date.</summary>
    public required int PageDateTimeHour { get; init; }

    public required PointPt ContentsOutline { get; init; }

    public string SchemaNamespace { get; init; } = "http://schemas.microsoft.com/office/onenote/2013/onenote";
}

/// <summary>The notebook plan template: sections and page patterns with tokens.</summary>
public sealed record NotebookSpec
{
    public required string NamePattern { get; init; }

    public required IReadOnlyList<SectionTemplate> Sections { get; init; }
}

public sealed record SectionTemplate
{
    /// <summary>Name pattern; tokens allowed when <see cref="EachWeek"/> is set.</summary>
    public required string Name { get; init; }

    /// <summary>Repeat this section once per week of the quarter.</summary>
    public bool EachWeek { get; init; }

    /// <summary>Color the repeated sections by month using the manifest section tints.</summary>
    public bool ColorByMonth { get; init; }

    public required IReadOnlyList<PageTemplate> Pages { get; init; }
}

public sealed record PageTemplate
{
    /// <summary>Manifest page type name: cover, contents, setup, annual, blank, objective, calendar, overview, day, reflection, scorecard, monthlyReview, quarterlyReview, notes.</summary>
    public required string Type { get; init; }

    /// <summary>Title pattern using the PRD token set.</summary>
    public string? Title { get; init; }

    public required int Level { get; init; }

    /// <summary>
    /// Which instance a template that does not repeat stands for, 0-based (default 0): for annual (and the other fixed
    /// types) which of the type's PDF pages; for objective which objective (0 is Objective 1); for calendar and
    /// monthlyReview which quarter month; for notes which configured Notes page name.
    /// </summary>
    public int? Index { get; init; }

    /// <summary>Repeat per instance: objectives 1 to 8, or days of the week.</summary>
    public bool Each { get; init; }

    /// <summary>Repeat once per month of the quarter.</summary>
    public bool EachMonth { get; init; }

    /// <summary>Repeat once per configured Notes page name.</summary>
    public bool EachNote { get; init; }
}
