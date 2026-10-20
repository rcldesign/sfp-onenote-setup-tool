using Sfp.Core.Dates;
using Sfp.Core.Options;

namespace Sfp.App.ViewModels.Generate;

/// <summary>A dropdown entry: the value the view model keeps and the label the user sees.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A date format dropdown entry: field order plus padding, from the date engine section of the PRD.</summary>
public sealed record DateFormatChoice(DateFieldOrder Order, bool Padded, string Label)
{
    public static IReadOnlyList<DateFormatChoice> All { get; } = new[]
    {
        new DateFormatChoice(DateFieldOrder.MonthDayYear, true, "MM / DD / YY"),
        new DateFormatChoice(DateFieldOrder.MonthDayYear, false, "M / D / YY"),
        new DateFormatChoice(DateFieldOrder.DayMonthYear, true, "DD / MM / YY"),
        new DateFormatChoice(DateFieldOrder.DayMonthYear, false, "D / M / YY"),
    };

    /// <summary>The entry for an order and padding (the default MM / DD / YY when nothing matches).</summary>
    public static DateFormatChoice For(DateFieldOrder order, bool padded) =>
        All.FirstOrDefault(c => c.Order == order && c.Padded == padded) ?? All[0];

    public override string ToString() => Label;
}

/// <summary>The fixed dropdown contents of the Generate tab.</summary>
public static class GenerateChoices
{
    public static IReadOnlyList<Choice<int>> Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new Choice<int>(m, PlannerText.MonthName(m)))
        .ToList();

    public static IReadOnlyList<Choice<WeeksRemainingMode>> WeeksRemaining { get; } = new[]
    {
        new Choice<WeeksRemainingMode>(WeeksRemainingMode.Corrected, "Corrected (counts down to 1)"),
        new Choice<WeeksRemainingMode>(WeeksRemainingMode.AsPrinted, "As printed (circles the week number)"),
    };

    /// <summary>Fixed at three; spill days are greyed in the adjacent month grid.</summary>
    public static IReadOnlyList<string> CalendarMonths { get; } = new[] { "Three" };

    public static IReadOnlyList<int> Dpis => GenerateOptions.AllowedDpis;

    public static IReadOnlyList<Choice<SixthRowMode>> SixthRow { get; } = new[]
    {
        new Choice<SixthRowMode>(SixthRowMode.Stack, "Stack in the fifth row (23 / 30)"),
        new Choice<SixthRowMode>(SixthRowMode.Drop, "Leave off the grid"),
    };

    public static IReadOnlyList<Choice<OutOfMonthDayStyle>> OutOfMonthDays { get; } = new[]
    {
        new Choice<OutOfMonthDayStyle>(OutOfMonthDayStyle.Greyed, "Greyed (40% ink)"),
        new Choice<OutOfMonthDayStyle>(OutOfMonthDayStyle.Blank, "Blank"),
    };

    /// <summary>First row hour, 0 to 23.</summary>
    public static IReadOnlyList<int> StartHours { get; } = Enumerable.Range(0, 24).ToList();

    /// <summary>Hour after the last row, 1 to 24.</summary>
    public static IReadOnlyList<int> EndHours { get; } = Enumerable.Range(1, 24).ToList();
}
