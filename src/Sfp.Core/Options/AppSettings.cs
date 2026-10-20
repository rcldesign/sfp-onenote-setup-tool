using Sfp.Core.Dates;

namespace Sfp.Core.Options;

/// <summary>
/// Last-used values saved to %APPDATA%\sfp2onenote\settings.json. Nothing else is written outside the output folder
/// and the log folder. Every member is optional so an older file still loads.
/// </summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;

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
