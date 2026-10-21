using System.Globalization;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Where a text may be cut to fit a width: only between text elements (<see cref="StringInfo"/>), so a cut never splits a
/// surrogate pair, an emoji sequence or a letter from its combining marks. A lone surrogate would be written as U+FFFD,
/// so the page would never read back the text the tool wanted and every later run would write it again.
/// </summary>
public static class TextCut
{
    /// <summary>The lengths of the non-empty proper prefixes of <paramref name="text"/> that end between text elements, longest first.</summary>
    public static IReadOnlyList<int> PrefixLengths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<int> ends = new();
        for (int at = 0; at < text.Length;)
        {
            at += StringInfo.GetNextTextElementLength(text, at);
            if (at < text.Length)
            {
                ends.Add(at);
            }
        }

        ends.Reverse();
        return ends;
    }
}
