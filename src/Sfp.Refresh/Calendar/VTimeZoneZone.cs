using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using NodaTime;
using NodaTime.TimeZones;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// A time zone built from a VTIMEZONE block, for a TZID no zone database knows (a legacy Outlook display name such as
/// "(GMT-05.00) Eastern Time (US &amp; Canada)"): every STANDARD and DAYLIGHT onset that Ical.Net expands from the
/// observance's DTSTART, RRULE and RDATE switches the wall offset from its TZOFFSETFROM to its TZOFFSETTO. Onsets are
/// expanded only over the window the run converts times in (the caller passes the fetch window plus a year on each
/// side), keeping the last onset before it; instants outside keep the nearest interval's offset. The blocks one file
/// expands share one budget of <see cref="MaxOnsets"/> onsets (the caller builds a block only when an event names its
/// TZID); the block that crosses the budget, or a rule that steps far more often than it matches, fails the source with
/// a message naming the TZID, so a broken or hostile feed cannot stall the run, however many blocks it holds.
/// </summary>
internal sealed class VTimeZoneZone : DateTimeZone
{
    /// <summary>The most onsets the blocks of one file may yield together over the expansion window (an hourly rule over two years is about 18,000).</summary>
    public const int MaxOnsets = 50_000;

    /// <summary>How many recurrence steps in a row may yield no onset before the rule counts as unreadable.</summary>
    private const int MaxUnmatchedSteps = 1_000;

    private static readonly EvaluationOptions Bounded = new() { MaxUnmatchedIncrementsLimit = MaxUnmatchedSteps };

    private readonly Instant[] boundaries;
    private readonly ZoneInterval[] intervals;

    private VTimeZoneZone(string id, ZoneInterval[] intervals)
        : base(id, intervals.Length == 1, intervals.Min(i => i.WallOffset), intervals.Max(i => i.WallOffset))
    {
        this.intervals = intervals;
        boundaries = intervals.Skip(1).Select(i => i.Start).ToArray();
    }

    /// <summary>
    /// The zone the block defines over [<paramref name="expandFrom"/>, <paramref name="expandTo"/>) (local times), or null
    /// when it has no observance with both offsets and a start. Every onset expanded is taken from
    /// <paramref name="budget"/>, the onsets the file's blocks may still yield together (<see cref="MaxOnsets"/> per file).
    /// Throws <see cref="InvalidDataException"/>, naming the TZID, when this block crosses the budget or changes its
    /// offset too often to be read.
    /// </summary>
    public static VTimeZoneZone? From(string id, VTimeZone definition, DateTime expandFrom, DateTime expandTo, ref int budget)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(definition);
        List<(Instant At, Offset Before, Offset After)> onsets = new();
        foreach (VTimeZoneInfo observance in definition.TimeZoneInfos)
        {
            if (observance.OffsetFrom is null || observance.OffsetTo is null || observance.DtStart is null)
            {
                return null;
            }

            Offset before = Offset.FromTimeSpan(observance.OffsetFrom.Offset);
            Offset after = Offset.FromTimeSpan(observance.OffsetTo.Offset);
            (Instant At, Offset Before, Offset After)? lastBefore = null;
            foreach (DateTime local in Onsets(id, observance, expandFrom, expandTo, Math.Max(budget, 0)))
            {
                if (--budget < 0)
                {
                    throw new InvalidDataException(TooOften(id, $"more than {MaxOnsets} offset changes from {expandFrom:yyyy-MM-dd} to {expandTo:yyyy-MM-dd}, counting the file's other VTIMEZONE blocks in use"));
                }

                Instant at = LocalDateTime.FromDateTime(local).WithOffset(before).ToInstant();
                if (local < expandFrom)
                {
                    lastBefore = (at, before, after);
                    continue;
                }

                onsets.Add((at, before, after));
            }

            if (lastBefore is { } last)
            {
                onsets.Add(last);
            }
        }

        if (onsets.Count == 0)
        {
            return null;
        }

        onsets.Sort((a, b) => a.At.CompareTo(b.At));
        List<ZoneInterval> intervals = new();
        Instant? start = null;
        Offset wall = onsets[0].Before;
        foreach ((Instant at, _, Offset after) in onsets)
        {
            if (start is Instant s && at <= s)
            {
                wall = after;
                continue;
            }

            intervals.Add(new ZoneInterval(id, start, at, wall, Offset.Zero));
            start = at;
            wall = after;
        }

        intervals.Add(new ZoneInterval(id, start, null, wall, Offset.Zero));
        return new VTimeZoneZone(id, intervals.ToArray());
    }

    /// <inheritdoc />
    public override ZoneInterval GetZoneInterval(Instant instant)
    {
        int found = Array.BinarySearch(boundaries, instant);
        return intervals[found >= 0 ? found + 1 : ~found];
    }

    /// <summary>
    /// The local start of the observance's onsets before <paramref name="expandTo"/> (its first onset when even that one
    /// is later). A plain rule (<see cref="PlainStep"/>) is expanded from its last step at or before
    /// <paramref name="expandFrom"/>, because Ical.Net steps from DTSTART, which Outlook sets in 1601; any other rule is
    /// expanded from DTSTART, bounded by <paramref name="limit"/> (the budget left; one onset more is returned so the
    /// caller sees it crossed) and by <see cref="MaxUnmatchedSteps"/>. The observance's DTSTART is moved for the expansion
    /// and put back.
    /// </summary>
    private static List<DateTime> Onsets(string id, VTimeZoneInfo observance, DateTime expandFrom, DateTime expandTo, int limit)
    {
        CalDateTime original = observance.DtStart!;
        if (PlainStep(observance) is TimeSpan step && original.HasTime && expandFrom - original.Value >= step)
        {
            long steps = (expandFrom - original.Value).Ticks / step.Ticks;
            observance.DtStart = new CalDateTime(original.Value + TimeSpan.FromTicks(steps * step.Ticks), original.TzId, true);
        }

        try
        {
            List<DateTime> found = new();
            foreach (Occurrence occurrence in observance.GetOccurrences(observance.DtStart, Bounded))
            {
                DateTime local = occurrence.Period.StartTime.Value;
                if (local >= expandTo && found.Count > 0)
                {
                    break;
                }

                found.Add(local);
                if (local >= expandTo || found.Count > limit)
                {
                    break;
                }
            }

            return found;
        }
        catch (EvaluationLimitExceededException ex)
        {
            throw new InvalidDataException(TooOften(id, $"a rule that steps more than {MaxUnmatchedSteps} times without changing the offset"), ex);
        }
        finally
        {
            observance.DtStart = original;
        }
    }

    /// <summary>
    /// The step of a plain rule: a single RRULE by the second, minute, hour, day or week, without COUNT, UNTIL or any BY
    /// part, so every whole number of steps after DTSTART is itself an onset and the expansion can start near the window.
    /// Null for any other observance.
    /// </summary>
    private static TimeSpan? PlainStep(VTimeZoneInfo observance)
    {
#pragma warning disable CS0618 // RecurrenceRules and ExceptionRules are obsolete, but they are the only way to see a second rule.
        if (observance.RecurrenceRules.Count != 1 || observance.ExceptionRules.Count > 0)
        {
            return null;
        }
#pragma warning restore CS0618

        RecurrenceRule? rule = observance.RecurrenceRule;
        if (rule is null || rule.Count is not null || rule.Until is not null
            || rule.BySecond.Count > 0 || rule.ByMinute.Count > 0 || rule.ByHour.Count > 0 || rule.ByDay.Count > 0
            || rule.ByMonthDay.Count > 0 || rule.ByYearDay.Count > 0 || rule.ByWeekNo.Count > 0 || rule.ByMonth.Count > 0
            || rule.BySetPosition.Count > 0)
        {
            return null;
        }

        TimeSpan unit = rule.Frequency switch
        {
            FrequencyType.Secondly => TimeSpan.FromSeconds(1),
            FrequencyType.Minutely => TimeSpan.FromMinutes(1),
            FrequencyType.Hourly => TimeSpan.FromHours(1),
            FrequencyType.Daily => TimeSpan.FromDays(1),
            FrequencyType.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero,
        };
        return unit == TimeSpan.Zero ? null : unit * Math.Max(1, rule.Interval);
    }

    private static string TooOften(string id, string detail) =>
        $"The calendar's time zone '{id}' is defined by a VTIMEZONE block that changes its offset too often to be read ({detail}), so its times cannot be placed.";
}
