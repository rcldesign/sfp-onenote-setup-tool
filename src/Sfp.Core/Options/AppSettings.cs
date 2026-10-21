using Sfp.Core.Dates;

namespace Sfp.Core.Options;

/// <summary>
/// Last-used values saved to %APPDATA%\sfp2onenote\settings.json. Nothing else is written outside the output folder
/// and the log folder. Every member is optional so an older file still loads.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The settings schema this version writes. A file with a higher version comes from a newer version of the app.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? PdfPath { get; init; }

    public QuarterSpec? Start { get; init; }

    public string? NotebookName { get; init; }

    public string? OutputFolder { get; init; }

    public DateFieldOrder DateOrder { get; init; } = DateFieldOrder.MonthDayYear;

    public bool PadDateFields { get; init; } = true;

    public WeeksRemainingMode WeeksRemaining { get; init; } = WeeksRemainingMode.Corrected;

    public int Dpi { get; init; } = 220;

    public bool GroupWeeks { get; init; }

    public bool Links { get; init; } = true;

    public bool HideTitle { get; init; }

    public PageSizeMode PageSize { get; init; } = PageSizeMode.Automatic;

    public double PageScale { get; init; } = 1.0;

    public SixthRowMode SixthRow { get; init; } = SixthRowMode.Stack;

    public OutOfMonthDayStyle OutOfMonthDays { get; init; } = OutOfMonthDayStyle.Greyed;

    public bool ReflectionDateIsWeekStart { get; init; }

    public bool IncludeOutOfQuarterDays { get; init; }

    public TimeBlockWindow TimeBlocks { get; init; } = TimeBlockWindow.Default;

    /// <summary>Output folders where notebooks were generated, for the Refresh tab's notebook list.</summary>
    public IReadOnlyList<string> KnownOutputFolders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// These settings with every member that must hold a value but is null replaced by its default, and null entries
    /// dropped from <see cref="KnownOutputFolders"/>. The JSON reader fills a member with null when the file says null (a
    /// hand edit, another version), whatever the member's annotation. Members declared nullable keep their null, which
    /// already means the default.
    /// </summary>
    public AppSettings WithDefaultsForNulls()
    {
        IReadOnlyList<string>? folders = KnownOutputFolders;
        TimeBlockWindow? timeBlocks = TimeBlocks;
        if (folders is not null && !folders.Any(f => f is null) && timeBlocks is not null)
        {
            return this;
        }

        return this with
        {
            KnownOutputFolders = folders is null ? Array.Empty<string>() : folders.Where(f => f is not null).ToArray(),
            TimeBlocks = timeBlocks ?? TimeBlockWindow.Default,
        };
    }

    /// <summary>Applies the saved values over a GenerateOptions with the given start.</summary>
    public GenerateOptions ToGenerateOptions(QuarterSpec start) => new()
    {
        Start = start,
        PdfPath = PdfPath ?? "",
        NotebookName = NotebookName,
        OutputFolder = OutputFolder,
        DateOrder = DateOrder,
        PadDateFields = PadDateFields,
        WeeksRemaining = WeeksRemaining,
        Dpi = Dpi,
        GroupWeeks = GroupWeeks,
        Links = Links,
        HideTitle = HideTitle,
        PageSize = PageSize,
        PageScale = PageScale,
        SixthRow = SixthRow,
        OutOfMonthDays = OutOfMonthDays,
        ReflectionDateIsWeekStart = ReflectionDateIsWeekStart,
        IncludeOutOfQuarterDays = IncludeOutOfQuarterDays,
        TimeBlocks = TimeBlocks,
    };

    /// <summary>Captures the values of a GenerateOptions for saving.</summary>
    public static AppSettings From(GenerateOptions o, IReadOnlyList<string> knownOutputFolders) => new()
    {
        PdfPath = o.PdfPath,
        Start = o.Start,
        NotebookName = o.NotebookName,
        OutputFolder = o.OutputFolder,
        DateOrder = o.DateOrder,
        PadDateFields = o.PadDateFields,
        WeeksRemaining = o.WeeksRemaining,
        Dpi = o.Dpi,
        GroupWeeks = o.GroupWeeks,
        Links = o.Links,
        HideTitle = o.HideTitle,
        PageSize = o.PageSize,
        PageScale = o.PageScale,
        SixthRow = o.SixthRow,
        OutOfMonthDays = o.OutOfMonthDays,
        ReflectionDateIsWeekStart = o.ReflectionDateIsWeekStart,
        IncludeOutOfQuarterDays = o.IncludeOutOfQuarterDays,
        TimeBlocks = o.TimeBlocks,
        KnownOutputFolders = knownOutputFolders,
    };
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. The path is injectable for tests.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);

    string Path { get; }
}
