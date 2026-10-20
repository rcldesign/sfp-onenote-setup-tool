using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Tests.Dates;

/// <summary>The planner text helpers: date slots, titles, section names, cover lines, the day counter and tokens.</summary>
public class PlannerTextTests
{
    private const string CounterFormat = "Day {dayOfQuarter} of {quarterLength} · Week {week} of {weekCount}";

    private readonly DateEngine engine = new();

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void DateSlot_in_month_day_year_order_pads_month_and_day_and_uses_a_two_digit_year()
    {
        DateSlotText slot = PlannerText.DateSlot(D(2027, 1, 4), DateFieldOrder.MonthDayYear, padded: true);

        slot.Fields.Should().Be(("01", "04", "27"));
        slot.Text.Should().Be("01 / 04 / 27");
        slot.ToString().Should().Be("01 / 04 / 27");
    }

    [Fact]
    public void DateSlot_unpadded_drops_leading_zeros_but_keeps_the_two_digit_year()
    {
        DateSlotText slot = PlannerText.DateSlot(D(2027, 1, 4), DateFieldOrder.MonthDayYear, padded: false);

        slot.Fields.Should().Be(("1", "4", "27"));
        slot.Text.Should().Be("1 / 4 / 27");
    }

    [Fact]
    public void DateSlot_in_day_month_year_order_swaps_the_first_two_fields()
    {
        PlannerText.DateSlot(D(2027, 1, 4), DateFieldOrder.DayMonthYear, padded: false).Text.Should().Be("4 / 1 / 27");
        PlannerText.DateSlot(D(2027, 1, 4), DateFieldOrder.DayMonthYear, padded: true).Fields.Should().Be(("04", "01", "27"));
    }

    [Theory]
    [InlineData(2026, 12, 28, "12 / 28 / 26")]
    [InlineData(2027, 4, 4, "04 / 04 / 27")]
    [InlineData(2000, 1, 1, "01 / 01 / 00")]
    [InlineData(2109, 11, 30, "11 / 30 / 09")]
    public void DateSlot_matches_the_PRD_worked_examples(int year, int month, int day, string expected)
    {
        PlannerText.DateSlot(D(year, month, day), DateFieldOrder.MonthDayYear, padded: true).Text.Should().Be(expected);
    }

    [Fact]
    public void DayTitle_is_the_three_letter_day_and_month_with_an_unpadded_day()
    {
        PlannerText.DayTitle(D(2027, 1, 4)).Should().Be("Mon Jan 4");
        PlannerText.DayTitle(D(2027, 1, 1)).Should().Be("Fri Jan 1");
        PlannerText.DayTitle(D(2026, 12, 31)).Should().Be("Thu Dec 31");
    }

    [Fact]
    public void ShortDate_and_LongDate_use_three_letter_months()
    {
        PlannerText.ShortDate(D(2027, 1, 4)).Should().Be("Jan 4");
        PlannerText.ShortDate(D(2027, 3, 31)).Should().Be("Mar 31");
        PlannerText.LongDate(D(2027, 1, 1)).Should().Be("Jan 1, 2027");
        PlannerText.LongDate(D(2027, 3, 31)).Should().Be("Mar 31, 2027");
    }

    [Fact]
    public void MonthYear_uses_the_full_English_month_name()
    {
        PlannerText.MonthYear(2027, 1).Should().Be("January 2027");
        PlannerText.MonthYear(2026, 11).Should().Be("November 2026");
    }

    [Fact]
    public void MonthYear_rejects_a_month_outside_1_to_12()
    {
        Action act = () => PlannerText.MonthYear(2027, 13);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WeekNumber_is_two_digits()
    {
        PlannerText.WeekNumber(2).Should().Be("02");
        PlannerText.WeekNumber(14).Should().Be("14");
    }

    [Fact]
    public void WeekSectionName_uses_the_in_quarter_span_not_the_Monday_to_Sunday_span()
    {
        IReadOnlyList<Week> weeks = engine.Weeks(engine.Build(new QuarterSpec(2027, 1)));

        PlannerText.WeekSectionName(weeks[0]).Should().Be("Week 01 (Jan 1 to Jan 3)");
        PlannerText.WeekSectionName(weeks[1]).Should().Be("Week 02 (Jan 4 to Jan 10)");
        PlannerText.WeekSectionName(weeks[13]).Should().Be("Week 14 (Mar 29 to Mar 31)");
    }

    [Fact]
    public void CoverLines_give_the_quarter_label_and_the_long_date_span()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        (string label, string span) = PlannerText.CoverLines(q);

        label.Should().Be("Q1 2027");
        span.Should().Be("Jan 1, 2027 to Mar 31, 2027");
    }

    [Fact]
    public void CoverLines_for_an_off_cycle_quarter_use_the_month_span_label()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 12));

        (string label, string span) = PlannerText.CoverLines(q);

        label.Should().Be("Dec to Feb 2026");
        span.Should().Be("Dec 1, 2026 to Feb 28, 2027");
    }

    [Fact]
    public void QuarterLabel_matches_the_engine_label()
    {
        PlannerText.QuarterLabel(new QuarterSpec(2027, 1)).Should().Be("Q1 2027");
        PlannerText.QuarterLabel(new QuarterSpec(2027, 2)).Should().Be("Feb to Apr 2027");
        PlannerText.QuarterLabel(new QuarterSpec(2027, 10)).Should().Be("Q4 2027");
    }

    [Fact]
    public void DayCounter_fills_the_manifest_format()
    {
        PlannerText.DayCounter(4, 90, 2, 14, CounterFormat).Should().Be("Day 4 of 90 · Week 2 of 14");
        PlannerText.DayCounter(91, 91, 13, 13, CounterFormat).Should().Be("Day 91 of 91 · Week 13 of 13");
    }

    [Fact]
    public void DayCounter_uses_the_shipped_manifest_format_default()
    {
        var slot = new DayCounterSlot { AppliesTo = new[] { "mon" }, RightX = 0, BaselineY = 0, SizePt = 8 };

        PlannerText.DayCounter(4, 90, 2, 14, slot.Format).Should().Be("Day 4 of 90 · Week 2 of 14");
    }

    [Fact]
    public void DayCounter_accepts_a_custom_format_with_a_subset_of_tokens()
    {
        PlannerText.DayCounter(4, 90, 2, 14, "D{dayOfQuarter}/W{week}").Should().Be("D4/W2");
    }

    [Fact]
    public void DayCounter_rejects_an_unknown_token_and_names_it()
    {
        Action act = () => PlannerText.DayCounter(4, 90, 2, 14, "Day {dayOfQuarter} of {length}");

        act.Should().Throw<ArgumentException>().WithMessage("*{length}*");
    }

    [Fact]
    public void TokenValues_for_a_date_give_Ddd_Mon_D_Month_and_YYYY()
    {
        IReadOnlyDictionary<string, string> tokens = PlannerText.TokenValues(D(2027, 1, 4));

        tokens.Should().HaveCount(5);
        tokens[Tokens.DayAbbrev].Should().Be("Mon");
        tokens[Tokens.MonthAbbrev].Should().Be("Jan");
        tokens[Tokens.DayOfMonth].Should().Be("4");
        tokens[Tokens.MonthName].Should().Be("January");
        tokens[Tokens.Year].Should().Be("2027");
    }

    [Fact]
    public void TokenValues_for_a_quarter_give_Qn_and_the_start_year()
    {
        IReadOnlyDictionary<string, string> onCycle = PlannerText.TokenValues(engine.Build(new QuarterSpec(2027, 1)));
        IReadOnlyDictionary<string, string> offCycle = PlannerText.TokenValues(engine.Build(new QuarterSpec(2026, 12)));

        onCycle[Tokens.QuarterLabel].Should().Be("Q1");
        onCycle[Tokens.Year].Should().Be("2027");
        offCycle[Tokens.QuarterLabel].Should().Be("Dec to Feb");
        offCycle[Tokens.Year].Should().Be("2026");
    }

    [Fact]
    public void TokenValues_for_a_week_give_NN_start_and_end_from_the_in_quarter_span()
    {
        Week week1 = engine.Weeks(engine.Build(new QuarterSpec(2027, 1)))[0];

        IReadOnlyDictionary<string, string> tokens = PlannerText.TokenValues(week1);

        tokens[Tokens.WeekNumber].Should().Be("01");
        tokens[Tokens.WeekStart].Should().Be("Jan 1");
        tokens[Tokens.WeekEnd].Should().Be("Jan 3");
    }

    [Fact]
    public void Formatting_is_English_regardless_of_the_current_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            PlannerText.DayTitle(D(2027, 3, 3)).Should().Be("Wed Mar 3");
            PlannerText.MonthYear(2027, 3).Should().Be("March 2027");
            PlannerText.DateSlot(D(2027, 3, 3), DateFieldOrder.MonthDayYear, padded: true).Text.Should().Be("03 / 03 / 27");
            engine.Build(new QuarterSpec(2027, 5)).Label.Should().Be("May to Jul 2027");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = original;
        }
    }
}
