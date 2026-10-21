using System.Globalization;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Refresh.Calendar;

/// <summary>Width in points of <paramref name="text"/> drawn at <paramref name="sizePt"/>. The renderer's font service provides the real one; tests pass a monospace estimate.</summary>
public delegate double TextWidthMeasure(string text, double sizePt);

/// <summary>Text conventions of the placement engine.</summary>
public sealed record PlacementOptions
{
    /// <summary>Weekly Overview columns list the latest-starting event first (PRD: "newest at top"). All-day lines always come first.</summary>
    public bool OverviewNewestFirst { get; init; } = true;

    /// <summary>Appended to a title cut to fit its column.</summary>
    public string Ellipsis { get; init; } = "…";

    /// <summary>Prefix of the line written at each later hour an event still covers.</summary>
    public string ContinuationPrefix { get; init; } = "... ";

    /// <summary>Format of the overflow line when a column or cell holds more events than its line cap; {0} is the hidden count.</summary>
    public string MoreFormat { get; init; } = "+{0} more";

    public static PlacementOptions Default { get; } = new();
}

/// <summary>
/// Pure mapping from events to slot texts per the PRD placement table. For every day of the window it emits every
/// calendar slot of that day's pages, including the empty ones, so the diff (M5.3) can see which slots are in scope:
/// daily "timeBlockEvent:{hour}" rows for each hour of the time-block window plus "allDayBand", the week's
/// "overviewDay:{weekdayIndex}" column and the month's "calendarEvents:{yyyy-MM-dd}" cell lines.
/// Widths and line caps come from the manifest (a Time Blocks row holds the lines its pitch fits); nothing measured lives here.
/// </summary>
public sealed class PlacementEngine
{
    private const string LineSeparator = "\n";

    private readonly Manifest manifest;
    private readonly TimeBlockWindow window;
    private readonly TextWidthMeasure measure;
    private readonly PlacementOptions options;

    /// <param name="manifest">Slot geometry (time-block column, all-day band, overview columns, calendar cell lines).</param>
    /// <param name="window">The notebook's time-block window from <see cref="GenerateOptions.TimeBlocks"/>.</param>
    /// <param name="measure">Text width in points at a size in points.</param>
    /// <param name="options">Text conventions; default when null.</param>
    public PlacementEngine(Manifest manifest, TimeBlockWindow window, TextWidthMeasure measure, PlacementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(measure);
        if (!window.IsValid)
        {
            throw new ArgumentException($"The time-block window {window.StartHour} to {window.EndHour} is not valid.", nameof(window));
        }

        this.manifest = manifest;
        this.window = window;
        this.measure = measure;
        this.options = options ?? PlacementOptions.Default;
    }

    /// <summary>Desired text of every calendar slot for every day from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public IReadOnlyList<CalendarSlotText> Place(DateOnly from, DateOnly to, IReadOnlyList<CalendarEvent> events, PlacementTargets targets)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(targets);
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The window end must not precede its start.");
        }

        List<CalendarEvent> active = events.Where(e => !e.Cancelled).ToList();
        List<CalendarSlotText> slots = new();
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            List<CalendarEvent> onDay = active.Where(e => e.CoversDay(day)).ToList();
            List<CalendarEvent> timed = onDay.Where(e => !e.AllDay).OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
            List<CalendarEvent> allDay = onDay.Where(e => e.AllDay).OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();

            PageKey? dayPage = targets.DayPage(day);
            if (dayPage is not null)
            {
                PlaceDaily(slots, dayPage, day, timed, allDay);
            }

            PageKey? overview = targets.OverviewPage(day);
            if (overview is not null)
            {
                slots.Add(new CalendarSlotText(overview, CalendarSlots.OverviewDay(CalendarSlots.WeekdayIndex(day)), day, OverviewText(timed, allDay)));
            }

            PageKey? calendar = targets.CalendarPage(day);
            if (calendar is not null)
            {
                slots.Add(new CalendarSlotText(calendar, CalendarSlots.CalendarEvents(day), day, CalendarCellText(allDay)));
            }
        }

        return slots;
    }

    /// <summary>The non-empty slots of <see cref="Place"/> as Create changes (no previous text known).</summary>
    public IReadOnlyList<PlannedChange> PlaceChanges(DateOnly from, DateOnly to, IReadOnlyList<CalendarEvent> events, PlacementTargets targets) =>
        Place(from, to, events, targets).Where(s => s.Text.Length > 0).Select(s => s.ToPlannedChange()).ToList();

    private void PlaceDaily(List<CalendarSlotText> slots, PageKey dayPage, DateOnly day, List<CalendarEvent> timed, List<CalendarEvent> allDay)
    {
        TimeBlocksSlot column = manifest.Slots.TimeBlocks;
        double width = column.ColumnRight - column.EventTextX;
        Dictionary<int, List<string>> primary = new();
        Dictionary<int, List<string>> continuation = new();

        foreach (CalendarEvent e in timed)
        {
            int hour = e.Start.Hour;
            bool inside = hour >= window.StartHour && hour < window.EndHour;
            int row = inside ? hour : window.StartHour;
            string time = e.Start.ToString("H:mm", CultureInfo.InvariantCulture) + " ";
            Add(primary, row, FitWithPrefix(time, e.Title, width, column.EventSizePt));
            if (!inside)
            {
                continue;
            }

            int lastExclusive = Math.Min(e.LastCoveredHourExclusive(), window.EndHour);
            for (int h = hour + 1; h < lastExclusive; h++)
            {
                Add(continuation, h, FitWithPrefix(options.ContinuationPrefix, e.Title, width, column.EventSizePt));
            }
        }

        int rowLines = RowLines(column);
        for (int h = window.StartHour; h < window.EndHour; h++)
        {
            List<string> lines = Lines(primary, h).Concat(Lines(continuation, h)).ToList();
            slots.Add(new CalendarSlotText(dayPage, CalendarSlots.TimeBlockEvent(h), day, string.Join(LineSeparator, Cap(lines, rowLines))));
        }

        BandSlot band = manifest.Slots.AllDayBand;
        double bandWidth = band.MaxWidthPt ?? band.Rect.W;
        string bandText = Fit(string.Join(band.Separator, allDay.Select(e => e.Title)), bandWidth, band.SizePt);
        slots.Add(new CalendarSlotText(dayPage, CalendarSlots.AllDayBand, day, bandText));
    }

    private string OverviewText(List<CalendarEvent> timed, List<CalendarEvent> allDay)
    {
        OverviewDayColumnsSlot column = manifest.Slots.OverviewDayColumns;
        List<string> lines = allDay.Select(e => Fit(e.Title, column.ColumnW, column.SizePt)).ToList();
        IEnumerable<CalendarEvent> ordered = options.OverviewNewestFirst
            ? timed.OrderByDescending(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal)
            : timed;
        foreach (CalendarEvent e in ordered)
        {
            string time = e.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + " ";
            lines.Add(FitWithPrefix(time, e.Title, column.ColumnW, column.SizePt));
        }

        return string.Join(LineSeparator, Cap(lines, column.MaxLines));
    }

    private string CalendarCellText(List<CalendarEvent> allDay)
    {
        CalendarEventLinesSlot cell = manifest.Slots.CalendarEventLines;
        List<string> lines = allDay.Select(e => Fit(e.Title, cell.W, cell.SizePt)).ToList();
        return string.Join(LineSeparator, Cap(lines, cell.MaxLines));
    }

    /// <summary>
    /// How many event lines one Time Blocks row holds: as many as fit its pitch in the window (floor(pitch / line
    /// height), at least one), so a busy hour never runs into the next row; no cap when the manifest gives no line height.
    /// </summary>
    private int RowLines(TimeBlocksSlot column) =>
        column.EventLineHeight > 0 ? Math.Max(1, (int)Math.Floor(column.PitchFor(window.RowCount) / column.EventLineHeight)) : 0;

    /// <summary>At most <paramref name="maxLines"/> lines: when there are more, the last visible line is "+n more".</summary>
    private IReadOnlyList<string> Cap(List<string> lines, int maxLines)
    {
        if (maxLines <= 0 || lines.Count <= maxLines)
        {
            return lines;
        }

        int shown = Math.Max(0, maxLines - 1);
        List<string> capped = lines.Take(shown).ToList();
        capped.Add(string.Format(CultureInfo.InvariantCulture, options.MoreFormat, lines.Count - shown));
        return capped;
    }

    /// <summary>The text, or its longest prefix plus the ellipsis that fits the width.</summary>
    private string Fit(string text, double widthPt, double sizePt)
    {
        if (text.Length == 0 || measure(text, sizePt) <= widthPt)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length].TrimEnd() + options.Ellipsis;
            if (measure(candidate, sizePt) <= widthPt)
            {
                return candidate;
            }
        }

        return measure(options.Ellipsis, sizePt) <= widthPt ? options.Ellipsis : "";
    }

    /// <summary>Prefix plus title, cutting only the title so the time (or continuation marker) always survives.</summary>
    private string FitWithPrefix(string prefix, string title, double widthPt, double sizePt)
    {
        string full = prefix + title;
        if (measure(full, sizePt) <= widthPt)
        {
            return full;
        }

        for (int length = title.Length - 1; length > 0; length--)
        {
            string candidate = prefix + title[..length].TrimEnd() + options.Ellipsis;
            if (measure(candidate, sizePt) <= widthPt)
            {
                return candidate;
            }
        }

        return prefix + options.Ellipsis;
    }

    private static void Add(Dictionary<int, List<string>> rows, int hour, string line)
    {
        if (!rows.TryGetValue(hour, out List<string>? list))
        {
            list = new List<string>();
            rows[hour] = list;
        }

        list.Add(line);
    }

    private static IEnumerable<string> Lines(Dictionary<int, List<string>> rows, int hour) =>
        rows.TryGetValue(hour, out List<string>? list) ? list : Enumerable.Empty<string>();
}
