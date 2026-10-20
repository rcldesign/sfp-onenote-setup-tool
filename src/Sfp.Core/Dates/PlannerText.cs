using System.Globalization;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Dates;

/// <summary>
/// Every piece of planner text that derives from a date, a week or a quarter, in one place so the plan builder, the
/// renderer and the link graph agree on the wording. All formatting is English and <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class PlannerText
{
    /// <summary>The separator drawn between date slot fields when the three fields are joined into one string.</summary>
    public const string DateSlotSeparator = " / ";

    private static readonly DateTimeFormatInfo Names = DateTimeFormatInfo.InvariantInfo;

    /// <summary>The quarter label for a first month: "Q1 2027" on cycle, otherwise the month span "Feb to Apr 2027" with the start year.</summary>
    public static string QuarterLabel(QuarterSpec spec)
    {
        if (spec.IsOnCycle)
        {
            int n = ((spec.Month - 1) / 3) + 1;
            return Inv($"Q{n} {spec.Year:0000}");
        }

        int lastMonth = ((spec.Month - 1 + 2) % 12) + 1;
        return Inv($"{MonthAbbrev(spec.Month)} to {MonthAbbrev(lastMonth)} {spec.Year:0000}");
    }

    /// <summary>
    /// The three date slot fields in the order they are drawn, left to right, plus the joined text ("01 / 04 / 27").
    /// The year is always two digits; month and day are zero-padded only when <paramref name="padded"/> is true.
    /// </summary>
    public static DateSlotText DateSlot(DateOnly date, DateFieldOrder order, bool padded)
    {
        string month = TwoOrOneDigits(date.Month, padded);
        string day = TwoOrOneDigits(date.Day, padded);
        string year = (date.Year % 100).ToString("00", CultureInfo.InvariantCulture);
        return order switch
        {
            DateFieldOrder.MonthDayYear => new DateSlotText(month, day, year),
            DateFieldOrder.DayMonthYear => new DateSlotText(day, month, year),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown date field order."),
        };
    }

    /// <summary>Daily page title, "Mon Jan 4".</summary>
    public static string DayTitle(DateOnly date) => Inv($"{DayAbbrev(date)} {ShortDate(date)}");

    /// <summary>"Jan 4".</summary>
    public static string ShortDate(DateOnly date) => Inv($"{MonthAbbrev(date.Month)} {date.Day}");

    /// <summary>"Jan 1, 2027".</summary>
    public static string LongDate(DateOnly date) => Inv($"{ShortDate(date)}, {date.Year:0000}");

    /// <summary>Calendar page title, "January 2027".</summary>
    public static string MonthYear(int year, int month) => Inv($"{MonthName(month)} {year:0000}");

    /// <summary>Two-digit week number, "02".</summary>
    public static string WeekNumber(int week) => week.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>Week section name using the in-quarter span, "Week 02 (Jan 4 to Jan 10)".</summary>
    public static string WeekSectionName(Week week) =>
        Inv($"Week {WeekNumber(week.Number)} ({ShortDate(week.FirstQuarterDay)} to {ShortDate(week.LastQuarterDay)})");

    /// <summary>The two cover lines: ("Q1 2027", "Jan 1, 2027 to Mar 31, 2027").</summary>
    public static (string Label, string Span) CoverLines(Quarter quarter) =>
        (quarter.Label, Inv($"{LongDate(quarter.Start)} to {LongDate(quarter.End)}"));

    /// <summary>
    /// Fills the manifest day counter format ("Day {dayOfQuarter} of {quarterLength} · Week {week} of {weekCount}").
    /// Throws when the format holds a token other than those four.
    /// </summary>
    public static string DayCounter(int dayOfQuarter, int quarterLength, int week, int weekCount, string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        string text = format
            .Replace("{dayOfQuarter}", dayOfQuarter.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{quarterLength}", quarterLength.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{week}", week.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{weekCount}", weekCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        int open = text.IndexOf('{', StringComparison.Ordinal);
        if (open >= 0)
        {
            int close = text.IndexOf('}', open);
            string token = close > open ? text[open..(close + 1)] : text[open..];
            throw new ArgumentException($"Unknown day counter token {token} in format '{format}'.", nameof(format));
        }

        return text;
    }

    /// <summary>The date tokens of a page: Ddd ("Mon"), Mon ("Jan"), D ("4"), Month ("January"), YYYY ("2027").</summary>
    public static IReadOnlyDictionary<string, string> TokenValues(DateOnly date) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Tokens.DayAbbrev] = DayAbbrev(date),
        [Tokens.MonthAbbrev] = MonthAbbrev(date.Month),
        [Tokens.DayOfMonth] = date.Day.ToString(CultureInfo.InvariantCulture),
        [Tokens.MonthName] = MonthName(date.Month),
        [Tokens.Year] = date.Year.ToString("0000", CultureInfo.InvariantCulture),
    };

    /// <summary>The quarter tokens: Qn ("Q1" or "Feb to Apr") and YYYY (the start year).</summary>
    public static IReadOnlyDictionary<string, string> TokenValues(Quarter quarter) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Tokens.QuarterLabel] = quarter.ShortLabel,
        [Tokens.Year] = quarter.Start.Year.ToString("0000", CultureInfo.InvariantCulture),
    };

    /// <summary>The week tokens: NN ("02"), start ("Jan 4") and end ("Jan 10"), both from the in-quarter span.</summary>
    public static IReadOnlyDictionary<string, string> TokenValues(Week week) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Tokens.WeekNumber] = WeekNumber(week.Number),
        [Tokens.WeekStart] = ShortDate(week.FirstQuarterDay),
        [Tokens.WeekEnd] = ShortDate(week.LastQuarterDay),
    };

    /// <summary>Three-letter English day name, "Mon".</summary>
    public static string DayAbbrev(DateOnly date) => Names.GetAbbreviatedDayName(date.DayOfWeek);

    /// <summary>Three-letter English month name, "Jan".</summary>
    public static string MonthAbbrev(int month) => Names.GetAbbreviatedMonthName(CheckMonth(month));

    /// <summary>Full English month name, "January".</summary>
    public static string MonthName(int month) => Names.GetMonthName(CheckMonth(month));

    private static int CheckMonth(int month) =>
        month is >= 1 and <= 12 ? month : throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");

    private static string TwoOrOneDigits(int value, bool padded) => value.ToString(padded ? "00" : "0", CultureInfo.InvariantCulture);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The three fields of a date slot in drawing order (left to right), and their joined text.</summary>
/// <param name="First">The leftmost field (month in MM / DD / YY order, day in DD / MM / YY order).</param>
/// <param name="Second">The middle field.</param>
/// <param name="Third">The two-digit year.</param>
public sealed record DateSlotText(string First, string Second, string Third)
{
    /// <summary>The fields as a tuple in drawing order.</summary>
    public (string First, string Second, string Third) Fields => (First, Second, Third);

    /// <summary>The fields joined with the slot separator, "01 / 04 / 27" (the text of a date link anchor).</summary>
    public string Text => First + PlannerText.DateSlotSeparator + Second + PlannerText.DateSlotSeparator + Third;

    public override string ToString() => Text;
}
