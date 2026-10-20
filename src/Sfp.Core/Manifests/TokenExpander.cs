using System.Text;

namespace Sfp.Core.Manifests;

/// <summary>
/// Expands the {Token} placeholders of manifest name and title patterns. A token is a run of letters between braces
/// ({NN}, {Mon}, {YYYY} ...); everything else is literal text. Braces that do not form a token are an error, so a typo
/// such as "{Month {YYYY}" is caught at manifest load instead of surfacing as a page title.
/// </summary>
public static class TokenExpander
{
    /// <summary>The tokens a pattern uses, in order of first appearance, each once.</summary>
    public static IReadOnlyList<string> TokensIn(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        List<string> tokens = new();
        foreach (Segment segment in Split(pattern))
        {
            if (segment.IsToken && !tokens.Contains(segment.Text, StringComparer.Ordinal))
            {
                tokens.Add(segment.Text);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Throws <see cref="ManifestException"/> when the pattern uses a token outside <paramref name="allowedTokens"/>
    /// or has a stray brace. The message names the token and the pattern.
    /// </summary>
    public static void Validate(string pattern, IEnumerable<string> allowedTokens)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(allowedTokens);
        HashSet<string> allowed = new(allowedTokens, StringComparer.Ordinal);
        foreach (string token in TokensIn(pattern))
        {
            if (!allowed.Contains(token))
            {
                string list = string.Join(", ", allowed.Select(t => "{" + t + "}"));
                throw new ManifestException($"Unknown token {{{token}}} in pattern '{pattern}'. Allowed tokens: {list}.");
            }
        }
    }

    /// <summary>
    /// Replaces every {Token} in the pattern with its value. Throws <see cref="ManifestException"/> when the pattern
    /// uses a token that <paramref name="values"/> does not hold, or has a stray brace.
    /// </summary>
    public static string Expand(string pattern, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(values);
        StringBuilder result = new(pattern.Length + 16);
        foreach (Segment segment in Split(pattern))
        {
            if (!segment.IsToken)
            {
                result.Append(segment.Text);
            }
            else if (values.TryGetValue(segment.Text, out string? value))
            {
                result.Append(value);
            }
            else
            {
                throw new ManifestException($"Pattern '{pattern}' uses token {{{segment.Text}}} but no value was supplied for it.");
            }
        }

        return result.ToString();
    }

    private static IEnumerable<Segment> Split(string pattern)
    {
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '}')
            {
                throw new ManifestException($"Pattern '{pattern}' has a closing brace without an opening one at position {i}.");
            }

            if (c != '{')
            {
                int end = pattern.IndexOfAny(Braces, i);
                int stop = end < 0 ? pattern.Length : end;
                yield return new Segment(pattern[i..stop], false);
                i = stop;
                continue;
            }

            int close = pattern.IndexOf('}', i + 1);
            if (close < 0)
            {
                throw new ManifestException($"Pattern '{pattern}' has an opening brace without a closing one at position {i}.");
            }

            string name = pattern[(i + 1)..close];
            if (name.Length == 0 || !name.All(char.IsAsciiLetter))
            {
                throw new ManifestException($"Pattern '{pattern}' has a malformed token '{{{name}}}' at position {i}: a token is letters between braces.");
            }

            yield return new Segment(name, true);
            i = close + 1;
        }
    }

    private static readonly char[] Braces = { '{', '}' };

    private readonly record struct Segment(string Text, bool IsToken);
}
