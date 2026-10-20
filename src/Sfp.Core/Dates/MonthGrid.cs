namespace Sfp.Core.Dates;

/// <summary>A Monday-first month grid of exactly 5 rows by 7 columns, as printed on the Monthly Calendar page.</summary>
/// <param name="Year">Calendar year of the month.</param>
/// <param name="Month">Calendar month, 1 to 12.</param>
/// <param name="Rows">Five rows of seven cells each.</param>
public sealed record MonthGrid(int Year, int Month, IReadOnlyList<IReadOnlyList<DayCell>> Rows)
{
    public const int RowCount = 5;
    public const int ColumnCount = 7;

    /// <summary>All cells, row-major.</summary>
    public IEnumerable<DayCell> Cells => Rows.SelectMany(r => r);

    /// <summary>True when the month needed a sixth row and its overflow days were stacked or dropped.</summary>
    public bool HasOverflow => Cells.Any(c => c.StackedDay is not null);
}

/// <summary>One cell of a month grid.</summary>
/// <param name="Date">The date in this cell.</param>
/// <param name="InMonth">False for leading days of the previous month and trailing days of the next.</param>
/// <param name="InQuarter">False for days outside the quarter (these are baked greyed and never linked).</param>
/// <param name="StackedDay">For a six-row month, the overflow day number stacked into this fifth-row cell ("23 / 30"), otherwise null.</param>
public sealed record DayCell(DateOnly Date, bool InMonth, bool InQuarter, int? StackedDay)
{
    /// <summary>The date of the stacked overflow day (always one week after <see cref="Date"/>), or null when nothing is stacked.</summary>
    public DateOnly? StackedDate => StackedDay is null ? null : Date.AddDays(MonthGrid.ColumnCount);
}
