using System.Text.RegularExpressions;

namespace Sfp.Core.Tests.Manifests;

/// <summary>
/// What a manifest load error says about a [JsonRequired] member left out of the file. The loader passes the
/// serializer's own sentence through after the JSON path, and the runtimes word it differently: .NET 8 says "was missing
/// required properties, including the following: x", .NET 9 "was missing required properties including: 'x'.". The
/// tests match what both say: the word "required", then the member's name as a whole word, quoted or not.
/// </summary>
internal static class MissingRequiredMember
{
    /// <summary>The regular expression a load error's message matches when it reports <paramref name="key"/> as a missing required member.</summary>
    public static string Pattern(string key) => @"\brequired\b.*\b" + Regex.Escape(key) + @"\b";
}
