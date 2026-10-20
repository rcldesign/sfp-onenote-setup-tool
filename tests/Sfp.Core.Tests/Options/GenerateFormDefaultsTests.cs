using Sfp.Core.Dates;
using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options;

/// <summary>M6.2: default start month, default notebook name, default folder, page count and the size estimate line.</summary>
public class GenerateFormDefaultsTests
{
    private readonly DateEngine _dates = new();

    [Theory]
    [InlineData(2026, 10, 20, 2027, 1)]
    [InlineData(2026, 12, 31, 2027, 1)]
    [InlineData(2027, 1, 1, 2027, 4)]
    [InlineData(2027, 3, 31, 2027, 4)]
    [InlineData(2027, 4, 1, 2027, 7)]
    [InlineData(2027, 8, 15, 2027, 10)]
    public void Next_quarter_start_is_the_first_month_of_the_next_calendar_quarter(int y, int m, int d, int year, int month)
    {
        GenerateFormDefaults.NextQuarterStart(new DateOnly(y, m, d)).Should().Be(new QuarterSpec(year, month));
    }

    [Fact]
    public void Notebook_name_follows_the_manifest_pattern_and_the_quarter_label()
    {
        string pattern = TestManifest.Shipped.Notebook.NamePattern;

        GenerateFormDefaults.NotebookName(pattern, _dates.Build(new QuarterSpec(2027, 1))).Should().Be("SFP Q1 2027");
        GenerateFormDefaults.NotebookName(pattern, _dates.Build(new QuarterSpec(2027, 2))).Should().Be("SFP Feb to Apr 2027");
        GenerateFormDefaults.NotebookName(pattern, _dates.Build(new QuarterSpec(2026, 12))).Should().Be("SFP Dec to Feb 2026");
    }

    [Fact]
    public void Output_folder_default_is_OneNote_Notebooks_under_Documents()
    {
        GenerateFormDefaults.OutputFolder(@"C:\Users\me\Documents").Should().Be(Path.Combine(@"C:\Users\me\Documents", "OneNote Notebooks"));
    }

    [Theory]
    [InlineData(2027, 1, 144)]
    [InlineData(2024, 4, 143)]
    [InlineData(2026, 10, 146)]
    public void Page_count_matches_the_PRD_worked_example(int year, int month, int expected)
    {
        GenerateFormDefaults.PageCount(_dates.Build(new QuarterSpec(year, month))).Should().Be(expected);
    }

    [Theory]
    [InlineData(144, 144, "About 20 MB for 144 pages at 144 DPI")]
    [InlineData(220, 144, "About 42 MB for 144 pages at 220 DPI")]
    [InlineData(300, 144, "About 73 MB for 144 pages at 300 DPI (above the 60 MB target)")]
    public void Size_estimate_multiplies_pages_by_the_per_page_figure_for_the_dpi(int dpi, int pages, string expected)
    {
        NotebookSizeEstimate.Describe(dpi, pages).Should().Be(expected);
    }

    [Fact]
    public void Size_estimate_in_bytes_uses_300_KB_per_page_at_220_dpi()
    {
        NotebookSizeEstimate.Bytes(220, 144).Should().Be(144L * 300 * 1024);
        NotebookSizeEstimate.Bytes(144, 1).Should().Be(140L * 1024);
        NotebookSizeEstimate.Bytes(300, 1).Should().Be(520L * 1024);
    }

    [Fact]
    public void Size_estimate_rejects_an_unknown_dpi_or_a_negative_page_count()
    {
        Action dpi = () => NotebookSizeEstimate.Bytes(72, 144);
        Action pages = () => NotebookSizeEstimate.Bytes(220, -1);
        dpi.Should().Throw<ArgumentOutOfRangeException>();
        pages.Should().Throw<ArgumentOutOfRangeException>();
    }
}
