using System.Text;
using Sfp.Core.Plan;

namespace Sfp.Refresh.Calendar;

/// <summary>What one diff run decided.</summary>
/// <param name="Changes">Only the writes needed: Create, Update or Remove per slot.</param>
/// <param name="FrozenThrough">The new sidecar value: days on or before it are never written again (yesterday, or the previous value if later).</param>
/// <param name="FirstWritableDay">The first day the run may touch (the day after <paramref name="FrozenThrough"/>).</param>
/// <param name="LastWritableDay">Today plus the horizon.</param>
public sealed record CalendarDiffResult(IReadOnlyList<PlannedChange> Changes, DateOnly FrozenThrough, DateOnly FirstWritableDay, DateOnly LastWritableDay)
{
    public int Creates => Changes.Count(c => c.Kind == ChangeKind.Create);

    public int Updates => Changes.Count(c => c.Kind == ChangeKind.Update);

    public int Removes => Changes.Count(c => c.Kind == ChangeKind.Remove);
}

/// <summary>
/// Freeze and diff (M5.3): compares the placement engine's desired slot texts with the texts the tool last wrote and
/// keeps only the writes needed. Days before today are never touched even when the source changed; an unchanged text
/// yields nothing; a slot that had text and now has none yields Remove; new text yields Create (slot empty before) or
/// Update. Pure: today is passed in.
/// </summary>
public static class CalendarDiff
{
    /// <param name="today">The run date.</param>
    /// <param name="horizonDays">Days after today that are still written (the refresh --horizon value).</param>
    /// <param name="frozenThrough">The sidecar's FrozenThrough, or null on the first calendar run.</param>
    /// <param name="previous">Text the tool last wrote per slot; slots with no entry (or an empty one) count as empty.</param>
    /// <param name="desired">Every slot the engine placed for the days it was asked about, empty texts included.</param>
    public static CalendarDiffResult Diff(
        DateOnly today,
        int horizonDays,
        DateOnly? frozenThrough,
        IReadOnlyDictionary<SlotRef, string> previous,
        IReadOnlyList<CalendarSlotText> desired)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(desired);
        if (horizonDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizonDays), horizonDays, "The horizon cannot be negative.");
        }

        DateOnly yesterday = today.AddDays(-1);
        DateOnly frozen = frozenThrough is DateOnly f && f > yesterday ? f : yesterday;
        DateOnly first = frozen.AddDays(1);
        DateOnly last = today.AddDays(horizonDays);

        HashSet<SlotRef> seen = new();
        List<PlannedChange> changes = new();
        foreach (CalendarSlotText slot in desired)
        {
            if (!seen.Add(slot.Ref))
            {
                throw new ArgumentException($"Slot {slot.Ref} was placed twice.", nameof(desired));
            }

            if (slot.Date < first || slot.Date > last)
            {
                continue;
            }

            previous.TryGetValue(slot.Ref, out string? old);
            bool hadText = !string.IsNullOrEmpty(old);
            bool hasText = slot.Text.Length > 0;
            if (hadText && !hasText)
            {
                changes.Add(new PlannedChange(slot.Page, slot.SlotId, old, "", ChangeKind.Remove, false, null));
            }
            else if (!hadText && hasText)
            {
                changes.Add(new PlannedChange(slot.Page, slot.SlotId, null, slot.Text, ChangeKind.Create, false, null));
            }
            else if (hadText && !string.Equals(Comparable(old ?? ""), Comparable(slot.Text), StringComparison.Ordinal))
            {
                changes.Add(new PlannedChange(slot.Page, slot.SlotId, old, slot.Text, ChangeKind.Update, false, null));
            }
        }

        return new CalendarDiffResult(changes, frozen, first, last);
    }

    /// <summary>
    /// A text as the page gives it back once written, for comparing the desired text with the page's: PlainText reads a
    /// no-break space as a space, the write turns a lone surrogate into U+FFFD, and OneNote may collapse runs of spaces,
    /// so every run of white space (line breaks and no-break spaces included) becomes one space, a lone surrogate
    /// becomes U+FFFD and the ends are trimmed. Texts equal in this form are not written again.
    /// </summary>
    public static string Comparable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join(' ', ReplaceLoneSurrogates(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ReplaceLoneSurrogates(string text)
    {
        if (!text.Any(char.IsSurrogate))
        {
            return text;
        }

        StringBuilder sb = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else
            {
                sb.Append(char.IsSurrogate(c) ? '�' : c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The slot texts after the (unblocked) changes are written, for the next run's <c>previous</c> map.</summary>
    public static IReadOnlyDictionary<SlotRef, string> Apply(IReadOnlyDictionary<SlotRef, string> previous, IEnumerable<PlannedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(changes);
        Dictionary<SlotRef, string> next = new(previous);
        foreach (PlannedChange change in changes)
        {
            if (change.Blocked)
            {
                continue;
            }

            SlotRef slot = new(change.Page, change.SlotId);
            if (change.Kind == ChangeKind.Remove)
            {
                next.Remove(slot);
            }
            else
            {
                next[slot] = change.NewText;
            }
        }

        return next;
    }

    /// <summary>A previous-text map from (page key value, slot id, text) rows, for callers that read the sidecar or page XML.</summary>
    public static IReadOnlyDictionary<SlotRef, string> PreviousFrom(IEnumerable<(string PageKey, string SlotId, string Text)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Dictionary<SlotRef, string> map = new();
        foreach ((string page, string slot, string text) in rows)
        {
            map[new SlotRef(new PageKey(page), slot)] = text;
        }

        return map;
    }
}
