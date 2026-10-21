namespace Sfp.Refresh.Calendar;

/// <summary>Maps a parsed Setup line (<see cref="CalendarSource"/>) to the reader that can fetch it.</summary>
public interface ICalendarSourceFactory
{
    ICalendarSource Create(CalendarSource source);
}

/// <summary>
/// Production factory: .ics URLs and files go to <see cref="IcsCalendarSource"/>, "holidays" to
/// <see cref="HolidayCalendarSource"/> backed by templates/holidays-{id}.json, "outlook" to <see cref="OutlookCalendarSource"/>.
/// </summary>
public sealed class CalendarSourceFactory : ICalendarSourceFactory
{
    private readonly string templatesDir;
    private readonly CalendarImportOptions options;
    private readonly HttpClient? http;
    private readonly Func<string, string>? contentFetcher;

    /// <param name="templatesDir">Folder holding holidays-*.json.</param>
    /// <param name="options">Zone and privacy settings; default when null.</param>
    /// <param name="http">Shared client for .ics URLs; each source creates its own when null.</param>
    /// <param name="contentFetcher">Test hook: when set, .ics sources read every location through it instead of the network or disk.</param>
    public CalendarSourceFactory(string templatesDir, CalendarImportOptions? options = null, HttpClient? http = null, Func<string, string>? contentFetcher = null)
    {
        this.templatesDir = templatesDir;
        this.options = options ?? CalendarImportOptions.Default;
        this.http = http;
        this.contentFetcher = contentFetcher;
    }

    /// <inheritdoc />
    public ICalendarSource Create(CalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Kind switch
        {
            CalendarSourceKind.IcsUrl or CalendarSourceKind.IcsFile => contentFetcher is null
                ? new IcsCalendarSource(source, options, http)
                : new IcsCalendarSource(source, contentFetcher, options),
            CalendarSourceKind.Holidays => new HolidayCalendarSource(source, HolidayList.Load(HolidayFilePath(source))),
            CalendarSourceKind.Outlook => new OutlookCalendarSource(source, options),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Unknown calendar source kind."),
        };
    }

    /// <summary>
    /// The holiday list file for a Holidays source: its Location is a list id ("us") or a path to a .json file, taken
    /// under the templates folder when relative. A rooted path must be a full local path (<see cref="LocalPath"/>), else
    /// <see cref="IOException"/> says why, so the source fails with that reason.
    /// </summary>
    public string HolidayFilePath(CalendarSource source)
    {
        string location = (source.Location ?? "").Trim();
        if (location.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (!LocalPath.IsRooted(location))
            {
                return Path.Combine(templatesDir, location);
            }

            string? problem = LocalPath.Problem(location);
            return problem is null ? location : throw new IOException(problem);
        }

        string id = location.Length == 0 ? HolidayCalendarSource.DefaultListId : location.ToLowerInvariant();
        return Path.Combine(templatesDir, "holidays-" + id + ".json");
    }
}
