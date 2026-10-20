namespace Sfp.Core.Dates;

/// <summary>The one input of the date engine: the quarter's first month. The day is always the 1st.</summary>
/// <param name="Year">Calendar year, 1900 to 2199.</param>
/// <param name="Month">Calendar month, 1 to 12. Any month is accepted; January, April, July and October give a Qn label.</param>
public sealed record QuarterSpec(int Year, int Month)
{
    public const int MinYear = 1900;
    public const int MaxYear = 2199;

    /// <summary>True when the month is January, April, July or October.</summary>
    public bool IsOnCycle => Month is 1 or 4 or 7 or 10;

    /// <summary>Parses "YYYY-MM" (the CLI --start form).</summary>
    public static QuarterSpec Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string[] parts = text.Trim().Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int month))
        {
            throw new FormatException($"Start month must be YYYY-MM, for example 2027-01; got '{text}'.");
        }

        return new QuarterSpec(year, month);
    }

    public override string ToString() => $"{Year:0000}-{Month:00}";
}
