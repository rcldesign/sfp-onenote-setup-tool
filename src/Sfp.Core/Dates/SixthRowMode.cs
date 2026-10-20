namespace Sfp.Core.Dates;

/// <summary>What to do with a Monday-first month that needs six rows on a five-row grid (November 2026 does).</summary>
public enum SixthRowMode
{
    /// <summary>Stack the overflow days in the fifth-row box of the same weekday ("23 / 30"), the paper-planner way. Default.</summary>
    Stack,

    /// <summary>Leave the overflow days off the grid.</summary>
    Drop,
}
