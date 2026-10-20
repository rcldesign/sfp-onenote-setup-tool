using System.Globalization;
using Sfp.Refresh.Setup;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// The dry-run listing and the run summary as lines of text, shared by the refresh verb and the Refresh tab. Every
/// planned change is listed with its page title, slot, old and new text, blocked reason and an ink marker.
/// </summary>
public static class RefreshText
{
    /// <summary>Marker appended to a change whose value came from ink.</summary>
    public const string InkMarker = "[ink]";

    /// <summary>The Setup values as read, one line per group, inked values marked.</summary>
    public static IEnumerable<string> ValueLines(SetupValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        yield return "Setup values:";
        yield return "  Objectives: " + Join(values.Objectives, values, SetupSlots.Objective);
        yield return "  Standing goals: " + Join(values.StandingGoals, values, SetupSlots.StandingGoal);
        yield return "  Metric names: " + Join(values.MetricNames, values, SetupSlots.MetricName);
        yield return "  Habits: " + Join(values.Habits, values, SetupSlots.Habit);
        yield return "  Calendar sources: " + (values.Sources.Count == 0 ? "(none)" : string.Join("; ", values.Sources.Select(s => s.Label + " (" + s.Kind + ": " + s.Location + ")")));
        foreach (string problem in values.SourceProblems)
        {
            yield return "  Calendar source problem: " + problem;
        }

        yield return "  Time blocks: " + (values.TimeBlockWindow is (int start, int end) ? Inv($"{start} to {end}") : "(default)");
    }

    /// <summary>The dry-run listing: the values, every planned change and the totals.</summary>
    public static IEnumerable<string> DryRunLines(RefreshSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        foreach (string line in ValueLines(summary.Values))
        {
            yield return line;
        }

        foreach (string warning in summary.Warnings)
        {
            yield return "Warning: " + warning;
        }

        yield return Inv($"Planned changes ({summary.Changes.Count}):");
        foreach (PlannedChange change in summary.Changes)
        {
            yield return "  " + ChangeLine(change, summary.PageTitles);
        }

        yield return Totals(summary.Changes);
        if (summary.FailedSources.Count > 0)
        {
            yield return "Failed sources: " + string.Join("; ", summary.FailedSources);
        }

        yield return "Dry run: nothing was written.";
    }

    /// <summary>"Mon Jan 4  objectiveLegend  create  \"\" -> \"O1 Ship the connector\"  [ink]" or the blocked form.</summary>
    public static string ChangeLine(PlannedChange change, IReadOnlyDictionary<string, string>? titles = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        string title = titles is not null && titles.TryGetValue(change.Page.Value, out string? t) ? t : change.Page.Value;
        string kind = change.Kind switch
        {
            ChangeKind.Create => "create",
            ChangeKind.Update => "update",
            ChangeKind.Remove => "remove",
            _ => change.Kind.ToString().ToLowerInvariant(),
        };
        string line = Inv($"{title}  {change.SlotId}  {kind}  {Quote(change.OldText)} -> {Quote(change.NewText)}");
        if (change.Inked)
        {
            line += "  " + InkMarker;
        }

        if (change.Blocked)
        {
            line += "  BLOCKED: " + (change.Reason ?? "a user object is in the way");
        }

        return line;
    }

    /// <summary>"Totals: 12 creates, 3 updates, 1 removal, 2 blocked, 4 inked".</summary>
    public static string Totals(IReadOnlyList<PlannedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        int creates = changes.Count(c => !c.Blocked && c.Kind == ChangeKind.Create);
        int updates = changes.Count(c => !c.Blocked && c.Kind == ChangeKind.Update);
        int removals = changes.Count(c => !c.Blocked && c.Kind == ChangeKind.Remove);
        int blocked = changes.Count(c => c.Blocked);
        int inked = changes.Count(c => c.Inked);
        return Inv($"Totals: {creates} creates, {updates} updates, {removals} removals, {blocked} blocked, {inked} inked");
    }

    /// <summary>The summary after a real run: what was read, touched, written and skipped.</summary>
    public static IEnumerable<string> SummaryLines(RefreshResult result, string notebookName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(notebookName);
        RefreshSummary? summary = result.Summary;
        if (!result.Success)
        {
            yield return "Refresh of " + notebookName + " failed: " + (result.Error ?? "unknown error");
        }
        else
        {
            yield return Inv($"Refreshed {notebookName} in {result.Elapsed.TotalSeconds:0.0} s.");
        }

        if (summary is null)
        {
            if (!string.IsNullOrEmpty(result.LogPath))
            {
                yield return "Log: " + result.LogPath;
            }

            yield break;
        }

        SetupValues v = summary.Values;
        yield return Inv($"Values read from Setup: {Count(v.Objectives)} objectives, {Count(v.StandingGoals)} standing goals, {Count(v.MetricNames)} metric names, {Count(v.Habits)} habits, {v.Sources.Count} calendar sources{(v.AnyInked ? " (some inked)" : "")}.");
        yield return Inv($"Pages touched: {summary.PagesTouched}. Values written: {summary.ValuesWritten}. Events written: {summary.EventsWritten}. Skipped: {summary.Skipped}.");
        foreach (PlannedChange change in summary.Changes.Where(c => c.Blocked))
        {
            yield return "Skipped: " + ChangeLine(change, summary.PageTitles);
        }

        if (summary.FailedSources.Count > 0)
        {
            yield return "Failed sources: " + string.Join("; ", summary.FailedSources);
        }

        foreach (string warning in summary.Warnings)
        {
            yield return "Warning: " + warning;
        }

        if (!string.IsNullOrEmpty(result.LogPath))
        {
            yield return "Log: " + result.LogPath;
        }
    }

    private static string Join(IReadOnlyList<string?> values, SetupValues v, Func<int, string> slot)
    {
        List<string> parts = new();
        for (int i = 0; i < values.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
            {
                continue;
            }

            string part = Inv($"{i + 1} {values[i]}");
            if (v.InkedSlots.Contains(slot(i + 1), StringComparer.Ordinal))
            {
                part += " " + InkMarker;
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? "(none)" : string.Join("; ", parts);
    }

    private static int Count(IReadOnlyList<string?> values) => values.Count(x => !string.IsNullOrWhiteSpace(x));

    private static string Quote(string? text) => text is null ? "(none)" : "\"" + text.Replace("\n", " / ", StringComparison.Ordinal) + "\"";

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
