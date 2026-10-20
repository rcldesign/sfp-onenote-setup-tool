using Sfp.Core.Dates;

namespace Sfp.Core.Tests.Dates;

/// <summary>M1.5: the 5-row Monday-first month grid with InMonth, InQuarter and sixth-row stacking.</summary>
public class DateEngineGridTests
{
    private readonly DateEngine engine = new();

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void January_2027_row_1_leads_with_Dec_28_to_31_outside_the_month_and_the_quarter()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        MonthGrid grid = engine.Grid(2027, 1, q);

        IReadOnlyList<DayCell> row1 = grid.Rows[0];
        row1.Select(c => c.Date).Should().Equal(
            D(2026, 12, 28), D(2026, 12, 29), D(2026, 12, 30), D(2026, 12, 31), D(2027, 1, 1), D(2027, 1, 2), D(2027, 1, 3));
        row1.Take(4).Should().OnlyContain(c => !c.InMonth && !c.InQuarter);
        row1.Skip(4).Should().OnlyContain(c => c.InMonth && c.InQuarter);
        grid.HasOverflow.Should().BeFalse();
    }

    [Fact]
    public void November_2026_starts_on_a_Sunday_and_stacks_30_on_the_23_cell()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 10));

        MonthGrid grid = engine.Grid(2026, 11, q);

        D(2026, 11, 1).DayOfWeek.Should().Be(DayOfWeek.Sunday);
        grid.Rows[0][6].Date.Should().Be(D(2026, 11, 1));
        DayCell monday23 = grid.Rows[4][0];
        monday23.Date.Should().Be(D(2026, 11, 23));
        monday23.StackedDay.Should().Be(30);
        monday23.StackedDate.Should().Be(D(2026, 11, 30));
        grid.Cells.Count(c => c.StackedDay is not null).Should().Be(1);
        grid.Cells.Should().NotContain(c => c.Date == D(2026, 11, 30));
        grid.Cells.Should().NotContain(c => c.StackedDay == 31, "November has no 31st");
        grid.HasOverflow.Should().BeTrue();
    }

    [Fact]
    public void November_2026_with_Drop_mode_leaves_the_30th_off_the_grid()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 10));

        MonthGrid grid = engine.GridWithMode(2026, 11, q, SixthRowMode.Drop);

        grid.Cells.Should().OnlyContain(c => c.StackedDay == null);
        grid.Cells.Should().NotContain(c => c.Date == D(2026, 11, 30));
        grid.HasOverflow.Should().BeFalse();
        grid.Rows[4][0].Date.Should().Be(D(2026, 11, 23));
    }

    [Fact]
    public void March_2026_starts_on_a_Sunday_with_31_days_and_stacks_30_and_31_on_the_23_and_24_cells()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 1));

        MonthGrid grid = engine.Grid(2026, 3, q);

        D(2026, 3, 1).DayOfWeek.Should().Be(DayOfWeek.Sunday);
        grid.Rows[4][0].Date.Should().Be(D(2026, 3, 23));
        grid.Rows[4][0].StackedDay.Should().Be(30);
        grid.Rows[4][1].Date.Should().Be(D(2026, 3, 24));
        grid.Rows[4][1].StackedDay.Should().Be(31);
        grid.Rows[4][1].StackedDate.Should().Be(D(2026, 3, 31));
        grid.Cells.Count(c => c.StackedDay is not null).Should().Be(2);
        grid.Rows[4].Skip(2).Should().OnlyContain(c => c.StackedDay == null);
    }

    [Fact]
    public void A_31_day_month_starting_on_Saturday_stacks_only_the_31st()
    {
        // August 2026: Aug 1 is a Saturday, so 1 to 30 fit in five rows and only 31 overflows (onto the 24 cell).
        Quarter q = engine.Build(new QuarterSpec(2026, 7));

        MonthGrid grid = engine.Grid(2026, 8, q);

        D(2026, 8, 1).DayOfWeek.Should().Be(DayOfWeek.Saturday);
        grid.Rows[4][6].Date.Should().Be(D(2026, 8, 30));
        grid.Rows[4][0].Date.Should().Be(D(2026, 8, 24));
        grid.Rows[4][0].StackedDay.Should().Be(31);
        grid.Cells.Count(c => c.StackedDay is not null).Should().Be(1);
    }

    [Fact]
    public void March_2027_row_5_trails_with_Apr_1_to_4_outside_the_quarter()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        MonthGrid grid = engine.Grid(2027, 3, q);

        IReadOnlyList<DayCell> row5 = grid.Rows[4];
        row5.Select(c => c.Date).Should().Equal(
            D(2027, 3, 29), D(2027, 3, 30), D(2027, 3, 31), D(2027, 4, 1), D(2027, 4, 2), D(2027, 4, 3), D(2027, 4, 4));
        row5.Take(3).Should().OnlyContain(c => c.InMonth && c.InQuarter);
        row5.Skip(3).Should().OnlyContain(c => !c.InMonth && !c.InQuarter);
        grid.HasOverflow.Should().BeFalse();
    }

    [Fact]
    public void October_2026_row_1_leads_with_Sep_28_to_30_outside_a_Q4_2026_quarter()
    {
        Quarter q = engine.Build(new QuarterSpec(2026, 10));

        MonthGrid grid = engine.Grid(2026, 10, q);

        IReadOnlyList<DayCell> row1 = grid.Rows[0];
        row1.Select(c => c.Date).Should().Equal(
            D(2026, 9, 28), D(2026, 9, 29), D(2026, 9, 30), D(2026, 10, 1), D(2026, 10, 2), D(2026, 10, 3), D(2026, 10, 4));
        row1.Take(3).Should().OnlyContain(c => !c.InMonth && !c.InQuarter);
        row1.Skip(3).Should().OnlyContain(c => c.InMonth && c.InQuarter);
    }

    [Fact]
    public void A_middle_month_marks_neighboring_days_in_quarter_but_out_of_month()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        MonthGrid grid = engine.Grid(2027, 2, q);

        // February 2027 starts on a Monday: row 1 is Feb 1 to 7, and row 5 is Mar 1 to 7.
        grid.Rows[0][0].Date.Should().Be(D(2027, 2, 1));
        grid.Rows[4].Select(c => c.Date).Should().Equal(Enumerable.Range(1, 7).Select(d => D(2027, 3, d)));
        grid.Rows[4].Should().OnlyContain(c => !c.InMonth && c.InQuarter);
        grid.Cells.Count(c => c.InMonth).Should().Be(28);
    }

    [Fact]
    public void A_month_starting_on_Monday_with_31_days_fills_row_5_with_the_next_month_and_has_no_overflow()
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        MonthGrid grid = engine.Grid(2027, 3, q);

        grid.Rows[0][0].Date.Should().Be(D(2027, 3, 1));
        grid.HasOverflow.Should().BeFalse();
        grid.Cells.Count(c => c.InMonth).Should().Be(31);
    }

    public static TheoryData<int, int> MonthsOfReferenceQuarters
    {
        get
        {
            var data = new TheoryData<int, int>();
            var engine = new DateEngine();
            foreach ((int year, int month) in new[] { (2027, 1), (2024, 4), (2026, 10), (2024, 1), (2027, 2), (2026, 12) })
            {
                foreach ((int y, int m) in engine.Build(new QuarterSpec(year, month)).Months)
                {
                    data.Add(y, m);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MonthsOfReferenceQuarters))]
    public void Every_grid_has_exactly_35_cells_in_5_rows_of_7_with_dates_increasing_by_one_day(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        MonthGrid grid = engine.Grid(year, month, q);

        grid.Year.Should().Be(year);
        grid.Month.Should().Be(month);
        grid.Rows.Should().HaveCount(MonthGrid.RowCount);
        grid.Rows.Should().OnlyContain(r => r.Count == MonthGrid.ColumnCount);
        List<DayCell> cells = grid.Cells.ToList();
        cells.Should().HaveCount(35);
        cells[0].Date.DayOfWeek.Should().Be(DayOfWeek.Monday);
        cells.Select(c => c.Date).Should().Equal(Enumerable.Range(0, 35).Select(cells[0].Date.AddDays));
        cells.Should().OnlyContain(c => c.InQuarter == q.Contains(c.Date));
        cells.Should().OnlyContain(c => c.InMonth == (c.Date.Year == year && c.Date.Month == month));
    }

    [Theory]
    [MemberData(nameof(MonthsOfReferenceQuarters))]
    public void Every_day_of_the_month_is_either_a_cell_or_stacked_on_a_row_5_cell_exactly_once(int year, int month)
    {
        Quarter q = engine.Build(new QuarterSpec(year, month));

        MonthGrid grid = engine.Grid(year, month, q);

        List<int> inCells = grid.Cells.Where(c => c.InMonth).Select(c => c.Date.Day).ToList();
        List<int> stacked = grid.Cells.Where(c => c.StackedDay is not null).Select(c => c.StackedDay!.Value).ToList();
        inCells.Concat(stacked).OrderBy(d => d).Should().Equal(Enumerable.Range(1, DateTime.DaysInMonth(year, month)));
        List<DayCell> stackedCells = grid.Cells.Where(c => c.StackedDay is not null).ToList();
        stackedCells.Should().NotContain(c => !grid.Rows[4].Contains(c), "overflow only ever stacks into row 5");
        stackedCells.Should().NotContain(c => c.StackedDate != c.Date.AddDays(7) || c.StackedDate!.Value.Day != c.StackedDay);
        int offset = ((int)new DateOnly(year, month, 1).DayOfWeek + 6) % 7;
        stackedCells.Count.Should().Be(Math.Max(0, offset + DateTime.DaysInMonth(year, month) - 35));
    }

    [Fact]
    public void A_cell_without_overflow_has_no_stacked_date()
    {
        var cell = new DayCell(D(2027, 1, 4), true, true, null);

        cell.StackedDate.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Grid_rejects_a_month_outside_1_to_12(int month)
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        Action act = () => engine.Grid(2027, month, q);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*1 and 12*");
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2200)]
    public void Grid_rejects_a_year_outside_1900_to_2199(int year)
    {
        Quarter q = engine.Build(new QuarterSpec(2027, 1));

        Action act = () => engine.Grid(year, 1, q);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*1900*2199*");
    }
}
