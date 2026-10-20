using Sfp.Core.Dates;

namespace Sfp.Core.Tests.Dates;

/// <summary>M1.4: Build and Weeks per the PRD's week and day math, checked against the reference quarters in the plan.</summary>
public class DateEngineQuarterTests
{
    private readonly DateEngine engine = new();

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void Q1_2027_runs_from_Jan_1_to_Mar_31_with_90_days_and_14_weeks()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        q.Start.Should().Be(D(2027, 1, 1));
        q.End.Should().Be(D(2027, 3, 31));
        q.Length.Should().Be(90);
        q.WeekCount.Should().Be(14);
        q.Label.Should().Be("Q1 2027");
        q.ShortLabel.Should().Be("Q1");
    }

    [Fact]
    public void Q1_2027_week_1_holds_Jan_1_to_3_starting_on_Monday_Dec_28_with_14_weeks_remaining()
    {
        Week week1 = engine.Weeks(engine.Build(new QuarterSpec(2027, 1)))[0];

        week1.Number.Should().Be(1);
        week1.MondayStart.Should().Be(D(2026, 12, 28));
        week1.SundayEnd.Should().Be(D(2027, 1, 3));
        week1.QuarterDays.Should().Equal(D(2027, 1, 1), D(2027, 1, 2), D(2027, 1, 3));
        week1.WeeksRemaining.Should().Be(14);
        week1.IsFull.Should().BeFalse();
    }

    [Fact]
    public void Q1_2027_week_14_holds_Mar_29_to_31_ending_on_Sunday_Apr_4_with_1_week_remaining()
    {
        IReadOnlyList<Week> weeks = engine.Weeks(engine.Build(new QuarterSpec(2027, 1)));
        Week week14 = weeks[13];

        weeks.Should().HaveCount(14);
        week14.Number.Should().Be(14);
        week14.MondayStart.Should().Be(D(2027, 3, 29));
        week14.SundayEnd.Should().Be(D(2027, 4, 4));
        week14.QuarterDays.Should().Equal(D(2027, 3, 29), D(2027, 3, 30), D(2027, 3, 31));
        week14.WeeksRemaining.Should().Be(1);
    }

    [Fact]
    public void Q1_2027_week_2_is_the_full_week_Jan_4_to_10_with_13_weeks_remaining()
    {
        Week week2 = engine.Weeks(engine.Build(new QuarterSpec(2027, 1)))[1];

        week2.MondayStart.Should().Be(D(2027, 1, 4));
        week2.SundayEnd.Should().Be(D(2027, 1, 10));
        week2.QuarterDays.Should().HaveCount(7);
        week2.IsFull.Should().BeTrue();
        week2.AllDays.Should().Equal(week2.QuarterDays);
        week2.WeeksRemaining.Should().Be(13);
    }

    [Fact]
    public void Q2_2024_starts_on_a_Monday_and_has_13_weeks()
    {
        Quarter q = engine.Build(new QuarterSpec(2024, 4));
        IReadOnlyList<Week> weeks = engine.Weeks(q);

        q.Start.DayOfWeek.Should().Be(DayOfWeek.Monday);
        q.Length.Should().Be(91);
        q.WeekCount.Should().Be(13);
        q.Label.Should().Be("Q2 2024");
        weeks.Should().HaveCount(13);
        weeks[0].MondayStart.Should().Be(q.Start);
        weeks[12].SundayEnd.Should().Be(q.End);
        weeks.Should().OnlyContain(w => w.IsFull);
    }

    [Fact]
    public void Q4_2026_has_14_weeks_and_week_14_holds_Dec_28_to_31()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 10));
        IReadOnlyList<Week> weeks = engine.Weeks(q);

        q.Label.Should().Be("Q4 2026");
        q.WeekCount.Should().Be(14);
        weeks[13].QuarterDays.Should().Equal(D(2026, 12, 28), D(2026, 12, 29), D(2026, 12, 30), D(2026, 12, 31));
        weeks[13].SundayEnd.Should().Be(D(2027, 1, 3));
    }

    [Fact]
    public void Q1_2024_leap_year_has_91_days_and_the_day_pages_total_91()
    {
        Quarter q = engine.Build(new QuarterSpec(2024, 1));
        IReadOnlyList<Week> weeks = engine.Weeks(q);

        q.Length.Should().Be(91);
        q.End.Should().Be(D(2024, 3, 31));
        weeks.Sum(w => w.QuarterDays.Count).Should().Be(91);
        weeks.SelectMany(w => w.QuarterDays).Should().Contain(D(2024, 2, 29));
    }

    [Theory]
    [InlineData(2027, 2, "Feb to Apr 2027")]
    [InlineData(2026, 12, "Dec to Feb 2026")]
    [InlineData(2027, 11, "Nov to Jan 2027")]
    [InlineData(2027, 5, "May to Jul 2027")]
    public void Off_cycle_start_is_labelled_by_its_month_span_with_the_start_year(int year, int month, string expected)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        q.Label.Should().Be(expected);
        q.ShortLabel.Should().Be(expected[..^5]);
    }

    [Theory]
    [InlineData(1, "Q1")]
    [InlineData(4, "Q2")]
    [InlineData(7, "Q3")]
    [InlineData(10, "Q4")]
    public void On_cycle_start_is_labelled_Qn(int month, string expectedShort)
    {
        Quarter q = engine.Build(new QuarterSpec(2027, month));

        q.Label.Should().Be(expectedShort + " 2027");
        q.ShortLabel.Should().Be(expectedShort);
    }

    [Fact]
    public void December_start_crosses_the_year_and_ends_on_the_last_day_of_February()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 12));

        q.End.Should().Be(D(2027, 2, 28));
        q.Months.Should().Equal((2026, 12), (2027, 1), (2027, 2));
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(2199)]
    public void Years_at_the_limits_are_accepted(int year)
    {
        Quarter q = engine.Build(new QuarterSpec(year, 1));

        q.Start.Year.Should().Be(year);
        q.WeekCount.Should().BeInRange(13, 14);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2200)]
    public void Years_outside_1900_to_2199_are_rejected_with_the_limit_named(int year)
    {
        Action act = () => engine.Build(new QuarterSpec(year, 1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*1900*2199*")
            .And.ActualValue.Should().Be(year);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Months_outside_1_to_12_are_rejected_with_the_limit_named(int month)
    {
        Action act = () => engine.Build(new QuarterSpec(2027, month));

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*1 and 12*");
    }

    public static TheoryData<int, int> ReferenceQuarters => new()
    {
        { 2027, 1 },
        { 2024, 4 },
        { 2026, 10 },
        { 2024, 1 },
        { 2027, 2 },
        { 2026, 12 },
    };

    [Theory]
    [MemberData(nameof(ReferenceQuarters))]
    public void Every_day_from_S_to_E_appears_in_exactly_one_week_in_order(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));
        IReadOnlyList<Week> weeks = engine.Weeks(q);

        List<DateOnly> expected = Enumerable.Range(0, q.Length).Select(q.Start.AddDays).ToList();
        weeks.SelectMany(w => w.QuarterDays).Should().Equal(expected);
        weeks.Should().OnlyContain(w => w.QuarterDays.Count >= 1 && w.QuarterDays.Count <= 7);
    }

    [Theory]
    [MemberData(nameof(ReferenceQuarters))]
    public void Weeks_are_consecutive_Monday_to_Sunday_and_numbered_from_1(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));
        IReadOnlyList<Week> weeks = engine.Weeks(q);

        weeks.Select(w => w.Number).Should().Equal(Enumerable.Range(1, q.WeekCount));
        weeks.Should().OnlyContain(w => w.MondayStart.DayOfWeek == DayOfWeek.Monday && w.SundayEnd == w.MondayStart.AddDays(6));
        for (int i = 1; i < weeks.Count; i++)
        {
            weeks[i].MondayStart.Should().Be(weeks[i - 1].SundayEnd.AddDays(1));
        }

        weeks.Select(w => w.WeeksRemaining).Should().Equal(Enumerable.Range(1, q.WeekCount).Reverse());
    }

    public static TheoryData<int, int> AllStarts2024To2028
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (int year = 2024; year <= 2028; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    data.Add(year, month);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllStarts2024To2028))]
    public void Quarter_length_is_89_to_92_days_and_week_count_is_13_or_14_for_every_start(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        q.Start.Should().Be(new DateOnly(year, month, 1));
        q.End.Should().Be(q.Start.AddMonths(3).AddDays(-1));
        q.Length.Should().BeInRange(89, 92);
        q.WeekCount.Should().BeOneOf(13, 14);
        engine.Weeks(q).Should().HaveCount(q.WeekCount);
    }

    [Theory]
    [MemberData(nameof(AllStarts2024To2028))]
    public void On_cycle_quarter_length_is_90_91_or_92_days(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        if (q.Spec.IsOnCycle)
        {
            q.Length.Should().BeOneOf(90, 91, 92);
        }
    }

    [Fact]
    public void February_start_in_a_common_year_is_the_one_89_day_quarter()
    {
        // Feb + Mar + Apr is 28 + 31 + 30; the PRD's "90, 91 or 92" describes on-cycle quarters.
        engine.Build(new QuarterSpec(2025, 2)).Length.Should().Be(89);
        engine.Build(new QuarterSpec(2024, 2)).Length.Should().Be(90);
    }

    [Theory]
    [MemberData(nameof(AllStarts2024To2028))]
    public void Week_count_is_13_exactly_when_offset_plus_length_fits_in_91_days(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        int offset = ((int)q.Start.DayOfWeek + 6) % 7;
        int expected = (int)Math.Ceiling((offset + q.Length) / 7.0);
        q.WeekCount.Should().Be(expected);
        q.WeekCount.Should().Be(offset + q.Length <= 91 ? 13 : 14);
    }

    [Theory]
    [InlineData(2026, 12)]
    [InlineData(2030, 1)]
    public void A_90_day_quarter_starting_on_a_Tuesday_also_has_13_weeks(int year, int month)
    {
        // o = 1 and L = 90 give ceil(91 / 7) = 13, so the Monday-only wording in the PRD prose is a simplification of its formula.
        Quarter q = engine.Build(new QuarterSpec(year, month));

        q.Start.DayOfWeek.Should().Be(DayOfWeek.Tuesday);
        q.Length.Should().Be(90);
        q.WeekCount.Should().Be(13);
        engine.Weeks(q)[12].QuarterDays.Should().EndWith(q.End);
    }

    [Fact]
    public void WeekOf_maps_a_quarter_day_to_the_week_that_holds_it()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        engine.WeekOf(q, D(2027, 1, 1)).Should().Be(1);
        engine.WeekOf(q, D(2027, 1, 3)).Should().Be(1);
        engine.WeekOf(q, D(2027, 1, 4)).Should().Be(2);
        engine.WeekOf(q, D(2027, 1, 10)).Should().Be(2);
        engine.WeekOf(q, D(2027, 3, 31)).Should().Be(14);
    }

    [Fact]
    public void WeekOf_agrees_with_Weeks_for_every_day_of_the_quarter()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 10));

        foreach (Week w in engine.Weeks(q))
        {
            foreach (DateOnly day in w.QuarterDays)
            {
                engine.WeekOf(q, day).Should().Be(w.Number);
            }
        }
    }

    [Fact]
    public void WeekOf_rejects_a_date_outside_the_quarter()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        Action act = () => engine.WeekOf(q, D(2026, 12, 31));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DayOfQuarter_counts_from_1_at_S_to_L_at_E()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        q.DayOfQuarter(D(2027, 1, 1)).Should().Be(1);
        q.DayOfQuarter(D(2027, 1, 4)).Should().Be(4);
        q.DayOfQuarter(D(2027, 3, 31)).Should().Be(90);
        q.Invoking(x => x.DayOfQuarter(D(2027, 4, 1))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
