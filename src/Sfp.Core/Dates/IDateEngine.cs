namespace Sfp.Core.Dates;

/// <summary>Pure quarter, week, day and calendar math. No I/O, no system clock, no time zones.</summary>
public interface IDateEngine
{
    /// <summary>Builds the quarter for a first month. Throws <see cref="ArgumentOutOfRangeException"/> outside 1900 to 2199 or for an invalid month.</summary>
    Quarter Build(QuarterSpec spec);

    /// <summary>The 13 or 14 Monday-first weeks that cover the quarter, in order.</summary>
    IReadOnlyList<Week> Weeks(Quarter q);

    /// <summary>The 5-row Monday-first grid for one month of the quarter, with overflow handling per <see cref="SixthRowMode"/>.</summary>
    MonthGrid Grid(int year, int month, Quarter q);
}
