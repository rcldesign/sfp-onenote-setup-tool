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

    /// <summary>The leading times of Time Blocks lines ("9:00 ") and Weekly Overview lines ("09:00 ").</summary>
    private static readonly string[] LeadingTimeFormats = { "H:mm", "HH:mm" };

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
    /// <param name="from">First day.</param>
    /// <param name="to">Last day.</param>
    /// <param name="events">The events of the sources that answered.</param>
    /// <param name="targets">The pages of each day.</param>
    /// <param name="keepLines">
    /// Null on a normal run. When a calendar source failed, the text each slot holds now: the failed source's events are
    /// unknown this run, so no line of these texts is dropped (M4.6). The lines the events no longer give are merged back
    /// in the slot's order, so a slot's text only gains lines; when the slot's line cap is hit, the old lines keep their
    /// places and the new ones wait (counted on a "+n more" line when one fits). A slot that already ends in a "+n more"
    /// line, and an all-day band that would lose an entry, keep their text as it is.
    /// </param>
    public IReadOnlyList<CalendarSlotText> Place(DateOnly from, DateOnly to, IReadOnlyList<CalendarEvent> events, PlacementTargets targets, IReadOnlyDictionary<SlotRef, string>? keepLines = null)
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
                PlaceDaily(slots, dayPage, day, timed, allDay, keepLines);
            }

            PageKey? overview = targets.OverviewPage(day);
            if (overview is not null)
            {
                string slotId = CalendarSlots.OverviewDay(CalendarSlots.WeekdayIndex(day));
                slots.Add(new CalendarSlotText(overview, slotId, day, OverviewText(timed, allDay, Kept(keepLines, overview, slotId))));
            }

            PageKey? calendar = targets.CalendarPage(day);
            if (calendar is not null)
            {
                string slotId = CalendarSlots.CalendarEvents(day);
                slots.Add(new CalendarSlotText(calendar, slotId, day, CalendarCellText(allDay, Kept(keepLines, calendar, slotId))));
            }
        }

        return slots;
    }

    /// <summary>The non-empty slots of <see cref="Place"/> as Create changes (no previous text known).</summary>
    public IReadOnlyList<PlannedChange> PlaceChanges(DateOnly from, DateOnly to, IReadOnlyList<CalendarEvent> events, PlacementTargets targets) =>
        Place(from, to, events, targets).Where(s => s.Text.Length > 0).Select(s => s.ToPlannedChange()).ToList();

    private void PlaceDaily(List<CalendarSlotText> slots, PageKey dayPage, DateOnly day, List<CalendarEvent> timed, List<CalendarEvent> allDay, IReadOnlyDictionary<SlotRef, string>? keepLines)
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
            string slotId = CalendarSlots.TimeBlockEvent(h);
            slots.Add(new CalendarSlotText(dayPage, slotId, day, Capped(lines, rowLines, Kept(keepLines, dayPage, slotId), RowOrder)));
        }

        BandSlot band = manifest.Slots.AllDayBand;
        double bandWidth = band.MaxWidthPt ?? band.Rect.W;
        string bandText = Fit(string.Join(band.Separator, allDay.Select(e => e.Title)), bandWidth, band.SizePt);
        string? oldBand = Kept(keepLines, dayPage, CalendarSlots.AllDayBand);
        slots.Add(new CalendarSlotText(dayPage, CalendarSlots.AllDayBand, day, oldBand is null ? bandText : KeepBand(bandText, oldBand, band.Separator)));
    }

    private string OverviewText(List<CalendarEvent> timed, List<CalendarEvent> allDay, string? old)
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

        return Capped(lines, column.MaxLines, old, OverviewOrder);
    }

    private string CalendarCellText(List<CalendarEvent> allDay, string? old)
    {
        CalendarEventLinesSlot cell = manifest.Slots.CalendarEventLines;
        List<string> lines = allDay.Select(e => Fit(e.Title, cell.W, cell.SizePt)).ToList();
        return Capped(lines, cell.MaxLines, old, string.CompareOrdinal);
    }

    /// <summary>The text a slot holds now when its lines must be kept (see <see cref="Place"/>), else null.</summary>
    private static string? Kept(IReadOnlyDictionary<SlotRef, string>? keepLines, PageKey page, string slotId) =>
        keepLines is not null && keepLines.TryGetValue(new SlotRef(page, slotId), out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    /// <summary>The lines capped at <paramref name="maxLines"/>, or merged with <paramref name="old"/> when its lines must be kept.</summary>
    private string Capped(List<string> lines, int maxLines, string? old, Comparison<string> order) =>
        old is null ? string.Join(LineSeparator, Cap(lines, maxLines)) : KeepOldLines(lines, maxLines, old, order);

    /// <summary>
    /// A slot whose lines must all stay: the old lines the events no longer give are put back where
    /// <paramref name="order"/> places them among the new lines, which keep their own order. When the result passes the
    /// cap, the old lines keep their places and only new lines are left out, counted on a "+n more" line when there is a
    /// line left for it. A slot that already ends in "+n more" was full and stays as it is, since the events behind
    /// its count are unknown; so does a slot that would gain nothing (no rewrite just to reorder).
    /// </summary>
    private string KeepOldLines(List<string> lines, int maxLines, string old, Comparison<string> order)
    {
        string[] oldLines = old.Split(LineSeparator);
        if (oldLines.Any(IsMoreLine))
        {
            return old;
        }

        Dictionary<string, int> given = Tally(lines);
        List<string> merged = new(lines);
        foreach (string line in oldLines.Where(l => !Take(given, l)))
        {
            int at = merged.FindIndex(l => order(l, line) > 0);
            merged.Insert(at < 0 ? merged.Count : at, line);
        }

        Dictionary<string, int> oldLeft = Tally(oldLines);
        bool[] isOld = merged.Select(l => Take(oldLeft, l)).ToArray();
        List<string> visible = merged;
        if (maxLines > 0 && merged.Count > maxLines)
        {
            int room = maxLines - 1 - oldLines.Length;
            int shown = 0;
            visible = new List<string>();
            for (int i = 0; i < merged.Count; i++)
            {
                if (isOld[i])
                {
                    visible.Add(merged[i]);
                }
                else if (shown < room)
                {
                    visible.Add(merged[i]);
                    shown++;
                }
            }

            if (room >= 0)
            {
                visible.Add(string.Format(CultureInfo.InvariantCulture, options.MoreFormat, merged.Count - oldLines.Length - room));
            }
        }

        return visible.Count == oldLines.Length ? old : string.Join(LineSeparator, visible);
    }

    /// <summary>The all-day band, one line: the new text when it keeps every entry of <paramref name="old"/>, else the old text (a new entry waits rather than push an old one out).</summary>
    private static string KeepBand(string text, string old, string separator)
    {
        Dictionary<string, int> entries = Tally(text.Split(separator));
        return old.Split(separator).All(e => Take(entries, e)) ? text : old;
    }

    /// <summary>True for a line the cap wrote ("+3 more").</summary>
    private bool IsMoreLine(string line)
    {
        int at = options.MoreFormat.IndexOf("{0}", StringComparison.Ordinal);
        if (at < 0)
        {
            return string.Equals(line, options.MoreFormat, StringComparison.Ordinal);
        }

        string head = options.MoreFormat[..at];
        string tail = options.MoreFormat[(at + 3)..];
        return line.Length > head.Length + tail.Length
            && line.StartsWith(head, StringComparison.Ordinal)
            && line.EndsWith(tail, StringComparison.Ordinal)
            && line[head.Length..^tail.Length].All(char.IsAsciiDigit);
    }

    /// <summary>Time Blocks row order: lines by their start time ("9:00 Staff"), then the continuation lines ("... Workshop").</summary>
    private int RowOrder(string a, string b)
    {
        bool aContinues = a.StartsWith(options.ContinuationPrefix, StringComparison.Ordinal);
        bool bContinues = b.StartsWith(options.ContinuationPrefix, StringComparison.Ordinal);
        if (aContinues || bContinues)
        {
            return aContinues.CompareTo(bContinues);
        }

        return ByTime(a, b, newestFirst: false);
    }

    /// <summary>Weekly Overview order: all-day titles first, then timed lines newest first (or oldest first, per the options).</summary>
    private int OverviewOrder(string a, string b) => ByTime(a, b, options.OverviewNewestFirst);

    /// <summary>Lines without a leading time first, then by time, ties by text.</summary>
    private static int ByTime(string a, string b, bool newestFirst)
    {
        int? ta = LeadingMinutes(a);
        int? tb = LeadingMinutes(b);
        if (ta is null || tb is null)
        {
            return ta is null && tb is null ? string.CompareOrdinal(a, b) : (ta is null ? -1 : 1);
        }

        int byTime = newestFirst ? tb.Value.CompareTo(ta.Value) : ta.Value.CompareTo(tb.Value);
        return byTime != 0 ? byTime : string.CompareOrdinal(a, b);
    }

    /// <summary>Minutes after midnight of a line's leading "9:00 " or "09:00 " time, or null when it has none.</summary>
    private static int? LeadingMinutes(string line)
    {
        int space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space is < 4 or > 5)
        {
            return null;
        }

        return TimeOnly.TryParseExact(line.AsSpan(0, space), LeadingTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time)
            ? (time.Hour * 60) + time.Minute
            : null;
    }

    /// <summary>How often each line occurs.</summary>
    private static Dictionary<string, int> Tally(IEnumerable<string> lines)
    {
        Dictionary<string, int> tally = new(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            tally[line] = tally.GetValueOrDefault(line) + 1;
        }

        return tally;
    }

    /// <summary>Takes one occurrence of <paramref name="line"/> out of <paramref name="tally"/>; false when none is left.</summary>
    private static bool Take(Dictionary<string, int> tally, string line)
    {
        if (!tally.TryGetValue(line, out int count) || count == 0)
        {
            return false;
        }

        tally[line] = count - 1;
        return true;
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
