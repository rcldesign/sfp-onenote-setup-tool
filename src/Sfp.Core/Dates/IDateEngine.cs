namespace Sfp.Core.Dates;

/// <summary>Pure quarter, week, day and calendar math. No I/O, no system clock, no time zones.</summary>
public interface IDateEngine
{
    /// <summary>Builds the quarter for a first month. Throws <see cref="ArgumentOutOfRangeException"/> outside 1900 to 2199 or for an invalid month.</summary>
    Quarter Build(QuarterSpec spec);

    /// <summary>The 13 or 14 Monday-first weeks that cover the quarter, in order.</summary>
    IReadOnlyList<Week> Weeks(Quarter q);

    /// <summary>The 1-based number of the week that holds a quarter day. Throws <see cref="ArgumentOutOfRangeException"/> for a date outside the quarter.</summary>
    int WeekOf(Quarter q, DateOnly date);

    /// <summary>
    /// The 5-row Monday-first grid for one month of the quarter. Every cell holds a real date (InMonth and InQuarter say
    /// where it falls); a six-row month has its overflow stacked into row 5 (<see cref="SixthRowMode.Stack"/>).
    /// The renderer decides from the options whether to draw the stacked day.
    /// </summary>
    MonthGrid Grid(int year, int month, Quarter q);

    /// <summary>As <see cref="Grid"/>, with <see cref="SixthRowMode.Drop"/> leaving <see cref="DayCell.StackedDay"/> null everywhere.</summary>
    MonthGrid GridWithMode(int year, int month, Quarter q, SixthRowMode mode);
}
