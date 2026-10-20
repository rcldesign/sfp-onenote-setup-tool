namespace Sfp.OneNote.Xml;

/// <summary>The parts of an onenote: URL as GetHyperlinkToObject returns it.</summary>
/// <param name="Url">The URL as given.</param>
/// <param name="SectionId">The section-id fragment parameter, or null.</param>
/// <param name="PageId">The page-id fragment parameter, or null (a section link has none).</param>
/// <param name="BasePath">The base-path fragment parameter when present, or null.</param>
/// <param name="Title">The page title that opens the fragment ("Mon Jan 4"), decoded, or null.</param>
/// <param name="Path">What sits between "onenote:" and "#": a .one path or a URL, possibly empty.</param>
public sealed record OneNoteHyperlink(string Url, string? SectionId, string? PageId, string? BasePath, string? Title, string Path)
{
    /// <summary>True when the link opens a page rather than a section.</summary>
    public bool IsPageLink => PageId is not null;
}

/// <summary>
/// Pulls section-id and page-id out of an onenote: URL, for example
/// "onenote:#Mon%20Jan%204&amp;section-id={...}&amp;page-id={...}&amp;end" or the same after a .one path or https URL.
/// </summary>
public static class HyperlinkParser
{
    private const string Scheme = "onenote:";

    /// <summary>Parses the URL, or returns null when it is not an onenote: link.</summary>
    public static OneNoteHyperlink? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int hash = url.IndexOf('#', StringComparison.Ordinal);
        string path = hash < 0 ? url[Scheme.Length..] : url[Scheme.Length..hash];
        string fragment = hash < 0 ? "" : url[(hash + 1)..];

        string? sectionId = null;
        string? pageId = null;
        string? basePath = null;
        string? title = null;
        string[] parts = fragment.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            int eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                if (i == 0 && !string.Equals(part, "end", StringComparison.OrdinalIgnoreCase))
                {
                    title = Uri.UnescapeDataString(part);
                }

                continue;
            }

            string key = part[..eq];
            string value = part[(eq + 1)..];
            if (key.Equals("section-id", StringComparison.OrdinalIgnoreCase))
            {
                sectionId = value;
            }
            else if (key.Equals("page-id", StringComparison.OrdinalIgnoreCase))
            {
                pageId = value;
            }
            else if (key.Equals("base-path", StringComparison.OrdinalIgnoreCase))
            {
                basePath = Uri.UnescapeDataString(value);
            }
        }

        return new OneNoteHyperlink(url, sectionId, pageId, basePath, title, path);
    }

    /// <summary>Parses the URL or throws <see cref="FormatException"/> when it is not an onenote: link.</summary>
    public static OneNoteHyperlink Parse(string url) =>
        TryParse(url) ?? throw new FormatException($"'{url}' is not an onenote: hyperlink.");
}
