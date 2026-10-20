namespace Sfp.Core.Dates;

/// <summary>
/// The production <see cref="IDateEngine"/>: the PRD's quarter, week and month-grid formulas as pure functions.
/// Leap years and month lengths come from <see cref="DateOnly"/>, never from hand-written tables.
/// </summary>
public sealed class DateEngine : IDateEngine
{
    private const int DaysPerWeek = 7;

    /// <inheritdoc />
    public Quarter Build(QuarterSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Year < QuarterSpec.MinYear || spec.Year > QuarterSpec.MaxYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec),
                spec.Year,
                $"Year must be between {QuarterSpec.MinYear} and {QuarterSpec.MaxYear}; got {spec.Year}.");
        }

        if (spec.Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.Month, $"Month must be between 1 and 12; got {spec.Month}.");
        }

        DateOnly start = new(spec.Year, spec.Month, 1);
        DateOnly end = start.AddMonths(3).AddDays(-1);
        int length = end.DayNumber - start.DayNumber + 1;
        int offset = MondayBasedWeekday(start);
        int weekCount = CeilingDivide(offset + length, DaysPerWeek);
        return new Quarter(start, end, weekCount, PlannerText.QuarterLabel(spec));
    }

    /// <inheritdoc />
    public IReadOnlyList<Week> Weeks(Quarter q)
    {
        ArgumentNullException.ThrowIfNull(q);
        DateOnly firstMonday = MondayOnOrBefore(q.Start);
        var weeks = new List<Week>(q.WeekCount);
        for (int k = 1; k <= q.WeekCount; k++)
        {
            DateOnly monday = firstMonday.AddDays(DaysPerWeek * (k - 1));
            DateOnly sunday = monday.AddDays(DaysPerWeek - 1);
            List<DateOnly> quarterDays = Enumerable.Range(0, DaysPerWeek)
                .Select(monday.AddDays)
                .Where(q.Contains)
                .ToList();
            weeks.Add(new Week(k, monday, sunday, quarterDays, q.WeekCount - k + 1));
        }

        return weeks;
    }

    /// <inheritdoc />
    public int WeekOf(Quarter q, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (!q.Contains(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, Quarter.OutsideMessage(q));
        }

        return ((date.DayNumber - MondayOnOrBefore(q.Start).DayNumber) / DaysPerWeek) + 1;
    }

    /// <inheritdoc />
    public MonthGrid Grid(int year, int month, Quarter q) => GridWithMode(year, month, q, SixthRowMode.Stack);

    /// <inheritdoc />
    public MonthGrid GridWithMode(int year, int month, Quarter q, SixthRowMode mode)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, $"Month must be between 1 and 12; got {month}.");
        }

        if (year < QuarterSpec.MinYear || year > QuarterSpec.MaxYear)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, $"Year must be between {QuarterSpec.MinYear} and {QuarterSpec.MaxYear}; got {year}.");
        }

        DateOnly first = new(year, month, 1);
        int offset = MondayBasedWeekday(first);
        DateOnly gridStart = first.AddDays(-offset);
        int cellCount = MonthGrid.RowCount * MonthGrid.ColumnCount;
        int daysInMonth = DateTime.DaysInMonth(year, month);

        // Cell index of day d (1-based) is offset + d - 1. Days whose index falls past the last cell overflow into a
        // sixth row the printed grid does not have; in Stack mode they ride in the fifth-row cell of the same weekday.
        var stacked = new Dictionary<int, int>();
        if (mode == SixthRowMode.Stack)
        {
            for (int day = 1; day <= daysInMonth; day++)
            {
                int index = offset + day - 1;
                if (index >= cellCount)
                {
                    stacked[index - MonthGrid.ColumnCount] = day;
                }
            }
        }

        var rows = new List<IReadOnlyList<DayCell>>(MonthGrid.RowCount);
        for (int row = 0; row < MonthGrid.RowCount; row++)
        {
            var cells = new List<DayCell>(MonthGrid.ColumnCount);
            for (int column = 0; column < MonthGrid.ColumnCount; column++)
            {
                int index = (row * MonthGrid.ColumnCount) + column;
                DateOnly date = gridStart.AddDays(index);
                bool inMonth = date.Year == year && date.Month == month;
                int? stackedDay = stacked.TryGetValue(index, out int d) ? d : null;
                cells.Add(new DayCell(date, inMonth, q.Contains(date), stackedDay));
            }

            rows.Add(cells);
        }

        return new MonthGrid(year, month, rows);
    }

    /// <summary>Monday = 0 ... Sunday = 6 (the PRD's offset o).</summary>
    private static int MondayBasedWeekday(DateOnly date) => ((int)date.DayOfWeek + 6) % DaysPerWeek;

    private static DateOnly MondayOnOrBefore(DateOnly date) => date.AddDays(-MondayBasedWeekday(date));

    private static int CeilingDivide(int dividend, int divisor) => (dividend + divisor - 1) / divisor;
}
