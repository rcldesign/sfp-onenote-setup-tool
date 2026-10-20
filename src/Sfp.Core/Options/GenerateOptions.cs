using Sfp.Core.Dates;

namespace Sfp.Core.Options;

/// <summary>Order of the three fields in a date slot.</summary>
public enum DateFieldOrder
{
    /// <summary>MM / DD / YY (default).</summary>
    MonthDayYear,

    /// <summary>DD / MM / YY.</summary>
    DayMonthYear,
}

/// <summary>Which number the weeks-remaining strip circles on week k.</summary>
public enum WeeksRemainingMode
{
    /// <summary>Strip of N numbers counting down; week k circles N - k + 1 (default).</summary>
    Corrected,

    /// <summary>As printed in the PDF: week k circles k.</summary>
    AsPrinted,
}

/// <summary>How leading and trailing days of neighboring months are drawn in a month grid.</summary>
public enum OutOfMonthDayStyle
{
    /// <summary>Drawn in ink at 40% opacity (default).</summary>
    Greyed,

    /// <summary>Left blank.</summary>
    Blank,
}

/// <summary>OneNote page size setting.</summary>
public enum PageSizeMode
{
    /// <summary>PageSize Automatic (default; iPad ignores custom sizes).</summary>
    Automatic,

    /// <summary>Custom 1112 x 770 pt page for people who print.</summary>
    Custom,
}

/// <summary>Hour window for the rebuilt Time Blocks column, inclusive start, exclusive end, 24-hour clock.</summary>
/// <param name="StartHour">First row hour, 0 to 23.</param>
/// <param name="EndHour">Hour after the last row, 1 to 24.</param>
public sealed record TimeBlockWindow(int StartHour, int EndHour)
{
    public static TimeBlockWindow Default { get; } = new(6, 22);

    /// <summary>Number of hourly rows in the window.</summary>
    public int RowCount => EndHour - StartHour;

    /// <summary>True when the window is well formed and holds at least one row.</summary>
    public bool IsValid => StartHour is >= 0 and <= 23 && EndHour is >= 1 and <= 24 && EndHour > StartHour;
}

/// <summary>
/// Every switch that changes what Generate produces. Defaults match the PRD. The record is immutable; the UI and CLI
/// build one and pass it down. Settings persistence serializes it to %APPDATA%\sfp2onenote\settings.json.
/// </summary>
public sealed record GenerateOptions
{
    /// <summary>Quarter start month. Required.</summary>
    public required QuarterSpec Start { get; init; }

    /// <summary>Path to the SFP PDF. Required for rendering; the plan builder ignores it.</summary>
    public string PdfPath { get; init; } = "";

    /// <summary>Notebook name override. Null means "SFP {Qn} {YYYY}" from the manifest.</summary>
    public string? NotebookName { get; init; }

    /// <summary>Output folder for the notebook. Null means Documents\OneNote Notebooks.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Path of a user manifest merged over the shipped one, or null.</summary>
    public string? UserManifestPath { get; init; }

    public DateFieldOrder DateOrder { get; init; } = DateFieldOrder.MonthDayYear;

    /// <summary>Zero-pad month and day ("01 / 04 / 27"). The year is always two digits.</summary>
    public bool PadDateFields { get; init; } = true;

    public WeeksRemainingMode WeeksRemaining { get; init; } = WeeksRemainingMode.Corrected;

    /// <summary>Render DPI: 144, 220 or 300.</summary>
    public int Dpi { get; init; } = 220;

    /// <summary>Add the days of week 1 and week N that fall outside the quarter, dated at 40% ink.</summary>
    public bool IncludeOutOfQuarterDays { get; init; }

    /// <summary>Put the week sections inside one "Weeks" section group.</summary>
    public bool GroupWeeks { get; init; }

    /// <summary>Emit link anchors. Off gives a purely locked sheet.</summary>
    public bool Links { get; init; } = true;

    /// <summary>Create pages without a title area and place the sheet at y = 0 (pages then show as "Untitled page").</summary>
    public bool HideTitle { get; init; }

    public PageSizeMode PageSize { get; init; } = PageSizeMode.Automatic;

    /// <summary>Placed size factor for the sheet image; 1.0 places it at 1112 x 770 pt.</summary>
    public double PageScale { get; init; } = 1.0;

    public SixthRowMode SixthRow { get; init; } = SixthRowMode.Stack;

    public OutOfMonthDayStyle OutOfMonthDays { get; init; } = OutOfMonthDayStyle.Greyed;

    /// <summary>Date the Weekly Reflection with the week's Monday instead of its Sunday.</summary>
    public bool ReflectionDateIsWeekStart { get; init; }

    public TimeBlockWindow TimeBlocks { get; init; } = TimeBlockWindow.Default;

    /// <summary>Names of the ruled pages in the Notes section.</summary>
    public IReadOnlyList<string> NotesPages { get; init; } = new[] { "Backlog", "Ideas", "Notes", "Contacts" };

    /// <summary>Path of the previous quarter's notebook (for the "Next quarter" link on its Quarterly Review), or null.</summary>
    public string? PreviousNotebook { get; init; }

    /// <summary>Set the page creation time to the planner date at 06:00 local (dropped if OneNote rejects it).</summary>
    public bool SetPageDates { get; init; } = true;

    /// <summary>Color week sections by month.</summary>
    public bool ColorSectionsByMonth { get; init; } = true;

    /// <summary>Suppress console output except errors.</summary>
    public bool Quiet { get; init; }

    public static IReadOnlyList<int> AllowedDpis { get; } = new[] { 144, 220, 300 };
}
