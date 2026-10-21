using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;
using NodaTime.TimeZones;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// A time zone built from a VTIMEZONE block, for a TZID no zone database knows (a legacy Outlook display name such as
/// "(GMT-05.00) Eastern Time (US &amp; Canada)"): every STANDARD and DAYLIGHT onset that Ical.Net expands from the
/// observance's DTSTART, RRULE and RDATE switches the wall offset from its TZOFFSETFROM to its TZOFFSETTO.
/// </summary>
internal sealed class VTimeZoneZone : DateTimeZone
{
    /// <summary>Onsets are expanded up to this year; later instants keep the last offset.</summary>
    private static readonly DateTime ExpandUntil = new(2200, 1, 1);

    private readonly Instant[] boundaries;
    private readonly ZoneInterval[] intervals;

    private VTimeZoneZone(string id, ZoneInterval[] intervals)
        : base(id, intervals.Length == 1, intervals.Min(i => i.WallOffset), intervals.Max(i => i.WallOffset))
    {
        this.intervals = intervals;
        boundaries = intervals.Skip(1).Select(i => i.Start).ToArray();
    }

    /// <summary>The zone the block defines, or null when it has no observance with both offsets and a start.</summary>
    public static VTimeZoneZone? From(string id, VTimeZone definition)
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
            foreach (Occurrence onset in observance.GetOccurrences(observance.DtStart, null).TakeWhile(o => o.Period.StartTime.Value < ExpandUntil))
            {
                Instant at = LocalDateTime.FromDateTime(onset.Period.StartTime.Value).WithOffset(before).ToInstant();
                onsets.Add((at, before, after));
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
}
