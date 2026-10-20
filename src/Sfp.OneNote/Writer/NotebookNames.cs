using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.OneNote.Writer;

/// <summary>
/// The PRD naming rules for the notebook and its sections: 1 to 64 characters, none of the characters OneNote refuses in
/// a file name (\ / : * ? " &lt; &gt; | # %). Section names are sanitized rather than refused because they come from the
/// manifest, not the user; the notebook name is the user's and is validated at preflight.
/// </summary>
public static class NotebookNames
{
    public const int MaxLength = 64;

    /// <summary>The user-facing rule, shown when a notebook name is refused.</summary>
    public const string RuleMessage = "Notebook name must be 1 to 64 characters and contain none of \\ / : * ? \" < > | # %.";

    /// <summary>Name used for a section whose sanitized name would be empty.</summary>
    public const string EmptySectionName = "Section";

    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '#', '%'];

    /// <summary>The characters a notebook or section name may not contain.</summary>
    public static IReadOnlyList<char> ForbiddenChars => Forbidden;

    /// <summary>True when the name satisfies the PRD rule.</summary>
    public static bool IsValid(string? name) => Problem(name) is null;

    /// <summary>The rule message when the name breaks it, else null.</summary>
    public static string? Problem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength || name.IndexOfAny(Forbidden) >= 0 || name.Trim().Length == 0)
        {
            return RuleMessage;
        }

        return null;
    }

    /// <summary>
    /// A section name OneNote accepts as a .one file name: forbidden characters become hyphens, ends are trimmed of
    /// spaces and dots, the length is capped, and an empty result becomes <see cref="EmptySectionName"/>.
    /// </summary>
    public static string SanitizeSectionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(Forbidden, chars[i]) >= 0 || char.IsControl(chars[i]))
            {
                chars[i] = '-';
            }
        }

        string cleaned = new string(chars).Trim().TrimEnd('.', ' ');
        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength].TrimEnd('.', ' ');
        }

        return cleaned.Length == 0 ? EmptySectionName : cleaned;
    }

    /// <summary>The default notebook name from the manifest pattern ("SFP {Qn} {YYYY}" gives "SFP Q1 2027").</summary>
    public static string Default(Manifest manifest, Quarter quarter)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(quarter);
        return TokenExpander.Expand(manifest.Notebook.NamePattern, PlannerText.TokenValues(quarter));
    }

    /// <summary>The user's override when given (trimmed), else the manifest default.</summary>
    public static string Resolve(Manifest manifest, Quarter quarter, GenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.NotebookName) ? Default(manifest, quarter) : options.NotebookName.Trim();
    }
}
