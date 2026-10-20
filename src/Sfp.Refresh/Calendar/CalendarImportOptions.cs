namespace Sfp.Refresh.Calendar;

/// <summary>
/// Settings shared by every calendar source: the local time zone events are normalized to, and how private events
/// are shown. Immutable; the CLI and app build one and pass it to the <see cref="CalendarSourceFactory"/>.
/// </summary>
public sealed record CalendarImportOptions
{
    /// <summary>Zone that event times are converted to. Defaults to the machine's zone; tests inject a fixed one.</summary>
    public TimeZoneInfo LocalZone { get; init; } = TimeZoneInfo.Local;

    /// <summary>Replace the title of a CLASS:PRIVATE (or CONFIDENTIAL) event with <see cref="PrivateTitle"/>. Default true per the PRD.</summary>
    public bool MaskPrivate { get; init; } = true;

    /// <summary>The title shown for a masked private event.</summary>
    public string PrivateTitle { get; init; } = "Private";

    /// <summary>Title used when an event has no SUMMARY.</summary>
    public string UntitledTitle { get; init; } = "(No title)";

    /// <summary>Default options: local zone, private events masked.</summary>
    public static CalendarImportOptions Default { get; } = new();
}
