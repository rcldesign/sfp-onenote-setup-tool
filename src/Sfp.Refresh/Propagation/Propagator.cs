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
    /// standing goals, metric names) carry it either way and are marked inked when it came from ink.
    /// </summary>
    public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(s);
        Manifest m = catalog.Manifest;
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
                SlotState state = SlotInspector.Inspect(xml, geometry.Rect, ownedId, ownedIds, m.PageWidthPt);
                PlannedChange? change = Decide(page.Key, slotId, text, state);
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
    /// desired text is empty; blocked when a user object overlaps the slot or the owned object was moved. When the owned
    /// object already holds the text but a user object overlaps it, a blocked entry reports the slot (nothing is
    /// written), so the summary names the slot the user wrote over (PRD Refresh acceptance item 2).
    /// </summary>
    public static PlannedChange? Decide(PageKey page, string slotId, string desired, SlotState state)
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
        if (string.Equals(old, wanted, StringComparison.Ordinal))
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

    private sealed record Texts(string Legend, bool LegendInked, string Habits, bool HabitsInked);
}
