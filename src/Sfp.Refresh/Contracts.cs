using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;

namespace Sfp.Refresh;

/// <summary>Where calendar events come from.</summary>
public enum CalendarSourceKind
{
    IcsUrl,
    IcsFile,
    Outlook,
    Holidays,
}

/// <summary>A calendar source line from the Setup page: "Work | https://.../calendar.ics".</summary>
/// <param name="Label">Short label shown before events when more than one source is configured.</param>
/// <param name="Kind">Source kind.</param>
/// <param name="Location">URL, file path, Outlook folder name, or the holiday list id.</param>
/// <param name="ColorTag">Optional color tag (unused by v1 rendering, kept for the summary).</param>
/// <param name="CalendarName">Optional calendar name filter for multi-calendar sources.</param>
public sealed record CalendarSource(string Label, CalendarSourceKind Kind, string Location, string? ColorTag, string? CalendarName);

/// <summary>One event instance after recurrence expansion, in local time.</summary>
public sealed record CalendarEvent(
    string Uid,
    string Title,
    DateTime Start,
    DateTime End,
    bool AllDay,
    bool IsPrivate,
    bool Cancelled,
    string SourceLabel,
    string? ColorTag)
{
    /// <summary>The local date the event starts on.</summary>
    public DateOnly Date => DateOnly.FromDateTime(Start);
}

/// <summary>What the user wrote on the Setup page.</summary>
/// <param name="Objectives">8 slots; null where empty.</param>
/// <param name="StandingGoals">3 slots.</param>
/// <param name="MetricNames">3 slots.</param>
/// <param name="Habits">16 slots.</param>
/// <param name="Sources">Parsed calendar sources, empty slots omitted.</param>
/// <param name="AnyInked">True when any value came from ink recognition rather than typed text.</param>
public sealed record SetupValues(
    IReadOnlyList<string?> Objectives,
    IReadOnlyList<string?> StandingGoals,
    IReadOnlyList<string?> MetricNames,
    IReadOnlyList<string?> Habits,
    IReadOnlyList<CalendarSource> Sources,
    bool AnyInked)
{
    /// <summary>Time-block window typed on Setup, or null when blank (Generate reads it; Refresh reports it).</summary>
    public (int Start, int End)? TimeBlockWindow { get; init; }

    /// <summary>Slot ids whose value came from ink, for the preview.</summary>
    public IReadOnlyList<string> InkedSlots { get; init; } = Array.Empty<string>();

    /// <summary>Calendar source lines that could not be parsed, worded for the summary (added by M4.2).</summary>
    public IReadOnlyList<string> SourceProblems { get; init; } = Array.Empty<string>();

    public static SetupValues Empty { get; } = new(
        new string?[8], new string?[3], new string?[3], new string?[16], Array.Empty<CalendarSource>(), false);
}

/// <summary>Reads Setup page XML into values using the manifest's Setup slot rectangles. Typed text wins over ink.</summary>
public interface ISetupReader
{
    SetupValues Read(PageXml setupPage, Manifest m);
}

/// <summary>Kind of change Refresh plans for one slot.</summary>
public enum ChangeKind
{
    Create,
    Update,
    Remove,
}

/// <summary>One planned write, or a blocked one with the reason.</summary>
/// <param name="Page">Target page.</param>
/// <param name="SlotId">Slot id on that page ("objectiveLegend", "standingGoal:1", "metricName:2", "habitLegend", "annualRow:3", "objectiveHeading", "scorecardRow:3", "timeBlockEvent:9", "allDayBand", "overviewDay:1", "calendarEvents:2027-01-04").</param>
/// <param name="OldText">Current tool-owned text, or null when the slot is empty.</param>
/// <param name="NewText">Text to write (empty for Remove).</param>
/// <param name="Kind">Create, Update or Remove.</param>
/// <param name="Blocked">True when a user object sits in the slot rectangle; nothing is written.</param>
/// <param name="Reason">Why it is blocked, or null.</param>
public sealed record PlannedChange(PageKey Page, string SlotId, string? OldText, string NewText, ChangeKind Kind, bool Blocked, string? Reason)
{
    /// <summary>True when the new text comes from an inked Setup value, which the real run writes only when confirmed (added by M4.4).</summary>
    public bool Inked { get; init; }
}

/// <summary>Computes every change Refresh would make from Setup values, never touching user content.</summary>
public interface IPropagator
{
    IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s);

    /// <summary>
    /// The plan for a run that writes values which came from ink only when <paramref name="writeInkedValues"/> is true.
    /// Without that confirmation a multi-value legend is built from the confirmed (typed) values only, so one inked
    /// value does not hold back a whole legend; the inked value is still listed in its own slots. An implementation that
    /// does not distinguish plans as <see cref="Plan(NotebookPlan, SetupValues, Sidecar)"/> (added by the wave 3 fix round).
    /// </summary>
    IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues) => Plan(plan, v, s);
}

/// <summary>A calendar source that can list its events over a date window.</summary>
public interface ICalendarSource
{
    IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to);
}
