using System.Diagnostics.CodeAnalysis;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// Parses one Calendar Sources line from the Setup page into a <see cref="CalendarSource"/>.
/// Accepted forms, fields separated by "|":
/// <list type="bullet">
/// <item>"Work | https://outlook.office.com/owa/calendar/.../calendar.ics" (label, then location)</item>
/// <item>"https://.../calendar.ics" or "C:\Users\me\family.ics" (bare URL or path; the label defaults to the host or file name)</item>
/// <item>"outlook" or "Shared | outlook:Team Calendar" (local Outlook, optionally a calendar folder name)</item>
/// <item>"holidays" or "holidays:us" (the bundled list)</item>
/// <item>optional third and fourth fields: a color tag, then a calendar name filter ("Work | https://x.ics | blue | Meetings")</item>
/// </list>
/// The kind comes from the URL scheme (http, https, webcal), the .ics extension, or the outlook and holidays keywords.
/// </summary>
public static class CalendarSourceLineParser
{
    /// <summary>The Setup page has four source slots.</summary>
    public const int MaxSources = 4;

    private const string OutlookKeyword = "outlook";
    private const string HolidaysKeyword = "holidays";

    /// <summary>Parses a line or throws <see cref="FormatException"/> with the reason.</summary>
    public static CalendarSource Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new FormatException("The calendar source line is empty.");
        }

        string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
        string label;
        string location;
        string? colorTag = null;
        string? calendarName = null;
        if (parts.Length == 1)
        {
            label = "";
            location = parts[0];
        }
        else
        {
            label = parts[0];
            location = parts[1];
            colorTag = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
            calendarName = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;
        }

        if (location.Length == 0)
        {
            throw new FormatException($"Calendar source '{line.Trim()}' has no URL, path or keyword after the label.");
        }

        if (TryKeyword(location, OutlookKeyword, out string? outlookName))
        {
            return new CalendarSource(DefaultLabel(label, "Outlook"), CalendarSourceKind.Outlook, OutlookKeyword, colorTag, calendarName ?? outlookName);
        }

        if (TryKeyword(location, HolidaysKeyword, out string? listId))
        {
            return new CalendarSource(DefaultLabel(label, "Holidays"), CalendarSourceKind.Holidays, listId ?? HolidayCalendarSource.DefaultListId, colorTag, calendarName);
        }

        if (IcsCalendarSource.IsUrl(location))
        {
            Uri uri = IcsCalendarSource.NormalizeUrl(location);
            return new CalendarSource(DefaultLabel(label, uri.Host), CalendarSourceKind.IcsUrl, location, colorTag, calendarName);
        }

        if (location.Contains("://", StringComparison.Ordinal))
        {
            throw new FormatException($"Calendar source '{location}' uses a scheme the tool cannot fetch: only http, https and webcal URLs are supported.");
        }

        if (location.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        {
            string fileLabel = Path.GetFileNameWithoutExtension(location.Replace('\\', '/'));
            return new CalendarSource(DefaultLabel(label, fileLabel), CalendarSourceKind.IcsFile, location, colorTag, calendarName);
        }

        throw new FormatException($"Calendar source '{location}' is not recognized: expected an http, https or webcal URL, a path ending in .ics, 'outlook' or 'holidays'.");
    }

    /// <summary>Parses a line; false (with no exception) when the line is blank or not recognized.</summary>
    public static bool TryParse(string? line, [NotNullWhen(true)] out CalendarSource? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            source = Parse(line);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses the Setup slots in order, skipping blank lines. Lines that do not parse are reported through
    /// <paramref name="problems"/> (when given) and skipped. At most <see cref="MaxSources"/> sources are returned.
    /// </summary>
    public static IReadOnlyList<CalendarSource> ParseAll(IEnumerable<string?> lines, ICollection<string>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<CalendarSource> sources = new();
        foreach (string? line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                sources.Add(Parse(line));
            }
            catch (FormatException ex)
            {
                problems?.Add(ex.Message);
            }

            if (sources.Count == MaxSources)
            {
                break;
            }
        }

        return sources;
    }

    private static string DefaultLabel(string label, string fallback) => label.Length > 0 ? label : fallback;

    /// <summary>Matches "keyword" or "keyword:argument" (case-insensitive) and returns the trimmed argument, if any.</summary>
    private static bool TryKeyword(string location, string keyword, out string? argument)
    {
        argument = null;
        if (location.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (location.Length > keyword.Length + 1
            && location.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            && location[keyword.Length] == ':')
        {
            string arg = location[(keyword.Length + 1)..].Trim();
            argument = arg.Length > 0 ? arg : null;
            return true;
        }

        return false;
    }
}
