using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Setup;

namespace Sfp.Refresh.Propagation;

/// <summary>
/// Production <see cref="IPropagator"/>: one <see cref="PlannedChange"/> per target slot of the PRD Setup table whose
/// text would change. Objective titles go to the annual rows, the objective headings, the legend band on every overview
/// and daily page and the scorecard rows; standing goals and metric names to every daily page; habits to the legend on
/// every calendar page. The current page XML is read lazily through the injected reader (the dry run reads nothing it
/// does not need); a page that cannot be read is skipped with a blocked change naming the reason. A slot is blocked when
/// an object the tool does not own overlaps its rectangle, or when the tool's own object was moved by the user. The plan
/// is idempotent: a tool-owned object that already holds the text yields nothing, unless a user object now covers it,
/// which is reported as a blocked entry that writes nothing. A cleared Setup slot removes the tool-owned object it fed
/// (the object is the tool's, so removing it never touches user content).
/// </summary>
public sealed class Propagator : IPropagator
{
    /// <summary>Reason recorded when a page has no ID in the sidecar or GetPageContent failed.</summary>
    public const string UnreadablePageReason = "The page could not be read, so nothing was planned for it.";

    /// <summary>Reason recorded when the user moved the tool's own object away from its slot.</summary>
    public const string MovedReason = "The tool-owned object was moved by the user and is left where it is.";

    /// <summary>Reason recorded when the text of the tool's own object is no longer what the tool wrote (the user typed into it).</summary>
    public const string EditedReason = "The tool-owned object was edited by the user and is left as it is.";

    private readonly SlotCatalog catalog;
    private readonly Func<PageKey, PageXml?> pageReader;
    private readonly TextWidthMeasure measure;

    /// <param name="catalog">Slot geometry for the notebook's quarter.</param>
    /// <param name="pageReader">Reads the current XML of a page by key, null when the page cannot be read.</param>
    /// <param name="measure">Text width in points at a size, for the legend truncation.</param>
    public Propagator(SlotCatalog catalog, Func<PageKey, PageXml?> pageReader, TextWidthMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pageReader);
        ArgumentNullException.ThrowIfNull(measure);
        this.catalog = catalog;
        this.pageReader = pageReader;
        this.measure = measure;
    }

    /// <summary>The plan with every value written, inked ones included (as when the run confirms inked values).</summary>
    public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s) => Plan(plan, v, s, writeInkedValues: true);

    /// <summary>
    /// The plan for a run that writes inked values only when <paramref name="writeInkedValues"/> is true. The objective
    /// and habit legends are built from every value when it is, else from the typed values only, and are marked inked
    /// only when they include an inked value; the slots fed by one value (annual rows, headings, scorecard rows,
    /// standing goals, metric names) carry it either way and are marked inked when it came from ink. An inked value the
    /// sidecar records as confirmed (<see cref="Sidecar.ConfirmedInked"/>) counts as typed while its Setup slot still reads
    /// the confirmed text, so a run without the confirmation never takes it out of a legend.
    /// </summary>
    public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(s);
        Manifest m = catalog.Manifest;
        v = WithConfirmedInk(v, s);
        IReadOnlyList<string?> legendObjectives = writeInkedValues ? v.Objectives : TypedOnly(v.Objectives, v, SetupSlots.Objective);
        IReadOnlyList<string?> legendHabits = writeInkedValues ? v.Habits : TypedOnly(v.Habits, v, SetupSlots.Habit);
        Texts texts = new(
            LegendText.ObjectiveLegend(legendObjectives, m.Slots.ObjectiveLegend, measure),
            IncludesInked(legendObjectives, v, SetupSlots.Objective),
            LegendText.HabitLegend(legendHabits, m.Slots.HabitLegend, measure),
            IncludesInked(legendHabits, v, SetupSlots.Habit));

        List<PlannedChange> changes = new();
        foreach (PageSpec page in plan.Pages)
        {
            List<(string SlotId, SlotGeometry Geometry, string Text, bool Inked)> targets = new();
            foreach (string slotId in catalog.PropagatedSlotsOn(page))
            {
                (string text, bool inked) = Desired(slotId, page, v, texts);
                targets.Add((slotId, catalog.Slot(page, slotId)!, text, inked));
            }

            if (targets.Count == 0)
            {
                continue;
            }

            s.Pages.TryGetValue(page.Key.Value, out SidecarPage? record);
            PageXml? xml = record is null ? null : pageReader(page.Key);
            if (xml is null)
            {
                foreach ((string slotId, _, string text, bool inked) in targets.Where(t => t.Text.Length > 0))
                {
                    changes.Add(new PlannedChange(page.Key, slotId, null, text, ChangeKind.Create, true, UnreadablePageReason) { Inked = inked });
                }

                continue;
            }

            IReadOnlyCollection<string> ownedIds = AnnualPageFallback.OwnedIds(s, page.Key);
            foreach ((string slotId, SlotGeometry geometry, string text, bool inked) in targets)
            {
                string? ownedId = record!.Anchors.TryGetValue(slotId, out string? id) ? id : null;
                string? written = record.AnchorTexts.TryGetValue(slotId, out string? last) ? last : null;
                SlotState state = SlotInspector.Inspect(xml, geometry.Rect, ownedId, ownedIds, m.PageWidthPt);
                PlannedChange? change = Decide(page.Key, slotId, text, state, written);
                if (change is not null)
                {
                    changes.Add(change with { Inked = inked });
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// The change for one slot from its desired text and current state: nothing when the owned object already holds the
    /// text (or there is nothing to write), Create when the slot is empty, Update when the text differs, Remove when the
    /// desired text is empty; blocked when a user object overlaps the slot, the owned object was moved, or the user changed
    /// it (its text is no longer the <paramref name="toolText"/> the tool last wrote there, or it holds a picture, ink, a
    /// file, a table or a tag the user put inside it): it is left as it is, never updated and never deleted.
    /// When the owned object already holds the text but a user object overlaps it, a blocked entry reports the slot
    /// (nothing is written), so the summary names the slot the user wrote over (PRD Refresh acceptance item 2).
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="desired">The text the slot should hold; empty for none.</param>
    /// <param name="state">What the page holds at the slot.</param>
    /// <param name="toolText">The text the tool last wrote in the owned object (the sidecar's AnchorTexts), or null when not recorded (older sidecars), which skips the edited check.</param>
    public static PlannedChange? Decide(PageKey page, string slotId, string desired, SlotState state, string? toolText = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(slotId);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(state);
        string wanted = desired.Trim();
        if (state.Owned is null)
        {
            if (wanted.Length == 0)
            {
                return null;
            }

            return state.IsBlocked
                ? new PlannedChange(page, slotId, null, wanted, ChangeKind.Create, true, BlockedReason(state))
                : new PlannedChange(page, slotId, null, wanted, ChangeKind.Create, false, null);
        }

        string old = state.OwnedText ?? "";
        if (string.Equals(CalendarDiff.Comparable(old), CalendarDiff.Comparable(wanted), StringComparison.Ordinal))
        {
            return state.IsBlocked && !state.Moved
                ? new PlannedChange(page, slotId, old, wanted, ChangeKind.Update, true, CoveredReason(state))
                : null;
        }

        ChangeKind kind = wanted.Length == 0 ? ChangeKind.Remove : ChangeKind.Update;
        if (state.Moved)
        {
            return new PlannedChange(page, slotId, old, wanted, kind, true, MovedReason);
        }

        if (IsEdited(state, toolText))
        {
            return new PlannedChange(page, slotId, old, wanted, kind, true, EditedReason);
        }

        if (kind == ChangeKind.Update && state.IsBlocked)
        {
            return new PlannedChange(page, slotId, old, wanted, kind, true, BlockedReason(state));
        }

        return new PlannedChange(page, slotId, old, wanted, kind, false, null);
    }

    /// <summary>"A user object overlaps the slot: text 'my note'."</summary>
    public static string BlockedReason(SlotState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return "A user object overlaps the slot: " + state.IntruderSummary + ".";
    }

    /// <summary>"A user object overlaps the slot: text 'my note'. The tool's text there is already current and was left as it is."</summary>
    public static string CoveredReason(SlotState state) =>
        BlockedReason(state) + " The tool's text there is already current and was left as it is.";

    /// <summary>
    /// True when the tool recorded the text it last wrote in an outline and the outline now holds other text. Runs of
    /// whitespace (line breaks included) compare equal, so OneNote normalizing spaces is not taken for an edit.
    /// </summary>
    public static bool IsEdited(string? current, string? toolText) =>
        toolText is not null && !string.Equals(Collapse(current ?? ""), Collapse(toolText), StringComparison.Ordinal);

    /// <summary>
    /// True when the slot's tool-owned outline was changed by the user: it holds something other than text (a picture,
    /// ink, a file, a table or a tag put inside it, see SlotInspector.HasToolShape), or its text is no longer what the tool
    /// last wrote (<paramref name="toolText"/>, compared as above). False when the slot has no owned outline.
    /// </summary>
    public static bool IsEdited(SlotState state, string? toolText)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Owned is not null && (!SlotInspector.HasToolShape(state.Owned) || IsEdited(state.OwnedText, toolText));
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static (string Text, bool Inked) Desired(string slotId, PageSpec page, SetupValues v, Texts texts)
    {
        (string prefix, _) = PropagationSlots.Split(slotId);
        int? n = PropagationSlots.Index(slotId);
        switch (prefix)
        {
            case PropagationSlots.ObjectiveLegend:
                return (texts.Legend, texts.LegendInked);
            case PropagationSlots.HabitLegend:
                return (texts.Habits, texts.HabitsInked);
            case PropagationSlots.StandingGoalPrefix:
                return Value(v.StandingGoals, n, v, SetupSlots.StandingGoal);
            case PropagationSlots.MetricNamePrefix:
                return Value(v.MetricNames, n, v, SetupSlots.MetricName);
            case PropagationSlots.AnnualRowPrefix:
            case PropagationSlots.ScorecardRowPrefix:
                return Value(v.Objectives, n, v, SetupSlots.Objective);
            case PropagationSlots.ObjectiveHeading:
                return Value(v.Objectives, page.IntToken(Tokens.ObjectiveNumber), v, SetupSlots.Objective);
            default:
                return ("", false);
        }
    }

    private static (string Text, bool Inked) Value(IReadOnlyList<string?> values, int? n, SetupValues v, Func<int, string> setupSlot)
    {
        if (n is not int i || i < 1 || i > values.Count)
        {
            return ("", false);
        }

        string text = values[i - 1]?.Trim() ?? "";
        return (text, text.Length > 0 && v.InkedSlots.Contains(setupSlot(i), StringComparer.Ordinal));
    }

    /// <summary>The values with every one that came from ink left out (its slot number kept, so "O3" stays "O3").</summary>
    private static string?[] TypedOnly(IReadOnlyList<string?> values, SetupValues v, Func<int, string> setupSlot) =>
        values.Select((value, i) => v.InkedSlots.Contains(setupSlot(i + 1), StringComparer.Ordinal) ? null : value).ToArray();

    /// <summary>True when a non-empty value among <paramref name="values"/> came from ink.</summary>
    private static bool IncludesInked(IReadOnlyList<string?> values, SetupValues v, Func<int, string> setupSlot) =>
        values.Select((value, i) => !string.IsNullOrWhiteSpace(value) && v.InkedSlots.Contains(setupSlot(i + 1), StringComparer.Ordinal)).Any(inked => inked);

    /// <summary>
    /// The confirmed inked values to record in the sidecar after a run, Setup slot id to text: every inked objective,
    /// standing goal, metric name and habit when the run confirmed them (<paramref name="confirmedNow"/>), else the ones
    /// the sidecar already records whose slot still reads the same text. A slot whose text changed, or that is typed or
    /// empty now, is dropped (a changed inked value waits for its own confirmation).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ConfirmedInked(SetupValues values, Sidecar sidecar, bool confirmedNow)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(sidecar);
        return InkedTexts(values)
            .Where(value => confirmedNow || IsConfirmed(sidecar, value.Slot, value.Text))
            .ToDictionary(value => value.Slot, value => value.Text, StringComparer.Ordinal);
    }

    /// <summary>The values with every inked value the sidecar records as confirmed, with the same text, counted as typed.</summary>
    private static SetupValues WithConfirmedInk(SetupValues v, Sidecar s)
    {
        if (s.ConfirmedInked.Count == 0)
        {
            return v;
        }

        HashSet<string> confirmed = InkedTexts(v).Where(value => IsConfirmed(s, value.Slot, value.Text)).Select(value => value.Slot).ToHashSet(StringComparer.Ordinal);
        return confirmed.Count == 0 ? v : v with { InkedSlots = v.InkedSlots.Where(slot => !confirmed.Contains(slot)).ToList() };
    }

    private static bool IsConfirmed(Sidecar s, string slot, string text) =>
        s.ConfirmedInked.TryGetValue(slot, out string? confirmed) && string.Equals(confirmed, text, StringComparison.Ordinal);

    /// <summary>Every non-empty objective, standing goal, metric name and habit that came from ink, by Setup slot id, trimmed.</summary>
    private static IEnumerable<(string Slot, string Text)> InkedTexts(SetupValues v) =>
        Inked(v.Objectives, v, SetupSlots.Objective)
            .Concat(Inked(v.StandingGoals, v, SetupSlots.StandingGoal))
            .Concat(Inked(v.MetricNames, v, SetupSlots.MetricName))
            .Concat(Inked(v.Habits, v, SetupSlots.Habit));

    private static IEnumerable<(string Slot, string Text)> Inked(IReadOnlyList<string?> values, SetupValues v, Func<int, string> setupSlot) =>
        values.Select((value, i) => (Slot: setupSlot(i + 1), Text: value?.Trim() ?? ""))
            .Where(value => value.Text.Length > 0 && v.InkedSlots.Contains(value.Slot, StringComparer.Ordinal));

    private sealed record Texts(string Legend, bool LegendInked, string Habits, bool HabitsInked);
}
