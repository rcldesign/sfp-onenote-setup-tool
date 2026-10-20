using System.Globalization;
using System.Text.RegularExpressions;

namespace Sfp.OneNote.Com;

/// <summary>Shortens XML for log lines: image and ink bytes are elided first, then the text is cut to a fixed length.</summary>
internal static partial class LogSafe
{
    public const int MaxXmlChars = 200;

    /// <summary>A loggable form of <paramref name="xml"/>: no one:Data payloads, at most <see cref="MaxXmlChars"/> characters.</summary>
    public static string Xml(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return "";
        }

        string elided = DataElement().Replace(xml, m => string.Format(
            CultureInfo.InvariantCulture, "{0}[{1} chars elided]{2}", m.Groups["open"].Value, m.Groups["body"].Length, m.Groups["close"].Value));
        return elided.Length <= MaxXmlChars
            ? elided
            : string.Concat(elided.AsSpan(0, MaxXmlChars), "... (", (xml.Length).ToString(CultureInfo.InvariantCulture), " chars)");
    }

    [GeneratedRegex(@"(?<open><(?:\w+:)?Data\b[^>]*>)(?<body>[^<]*)(?<close></(?:\w+:)?Data>)", RegexOptions.CultureInvariant)]
    private static partial Regex DataElement();
}
