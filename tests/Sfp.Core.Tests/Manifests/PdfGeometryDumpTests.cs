using Sfp.Core.Manifests;
using Sfp.Core.Pdf;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Manifests;

public class PdfGeometryDumpTests : IDisposable
{
    private static readonly PtSize Landscape = new(1112, 770);
    private readonly string _tempDir = TestPaths.NewTempDir("geometry");
    private readonly PdfGeometryDump _dump = new();

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Word_placed_at_a_known_point_is_reported_within_one_point()
    {
        string pdf = Write("monday.pdf", PdfFixtures.BuildPage(
            Landscape,
            new[] { new FixtureText("Monday", 23, 47, 32) },
            Array.Empty<FixtureLine>()));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        WordGeometry word = page.Words.Should().ContainSingle(w => w.Text == "Monday").Subject;
        word.X.Should().BeApproximately(23, 1.0);
        word.Baseline.Should().BeApproximately(47, 1.0);
        word.Top.Should().BeInRange(47 - 32, 47);
        word.Bottom.Should().BeGreaterThanOrEqualTo(word.Top);
        word.Right.Should().BeGreaterThan(word.X + 32, "six letters at 32 pt are far wider than one em");
        word.FontSizePt.Should().BeApproximately(32, 0.01);
        word.FontName.Should().Contain("Helvetica");
    }

    [Fact]
    public void Words_are_ordered_by_baseline_then_left_to_right()
    {
        string pdf = Write("order.pdf", PdfFixtures.BuildPage(
            Landscape,
            new[]
            {
                new FixtureText("Plan", 404, 80, 15),
                new FixtureText("Daily", 40, 80, 15),
                new FixtureText("Monday", 23, 47, 32),
            },
            Array.Empty<FixtureLine>()));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        page.Words.Select(w => w.Text).Should().Equal("Monday", "Daily", "Plan");
    }

    [Fact]
    public void Line_segment_is_reported_in_top_left_coordinates_with_its_stroke_width()
    {
        string pdf = Write("slash.pdf", PdfFixtures.BuildPage(
            Landscape,
            Array.Empty<FixtureText>(),
            new[] { new FixtureLine(900.2, 16.9, 918.8, 41.0, 1) }));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        LineGeometry line = page.Lines.Should().ContainSingle().Subject;
        line.X1.Should().BeApproximately(900.2, 0.05);
        line.Y1.Should().BeApproximately(16.9, 0.05);
        line.X2.Should().BeApproximately(918.8, 0.05);
        line.Y2.Should().BeApproximately(41.0, 0.05);
        line.StrokeWidth.Should().BeApproximately(1, 0.01);
    }

    [Fact]
    public void Thin_filled_rectangle_is_reported_as_a_rule()
    {
        string pdf = Write("rule.pdf", PdfFixtures.BuildPageWithFilledRectangle(Landscape, 779.5, 50.65, 303.2, 0.5));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        LineGeometry rule = page.Lines.Should().ContainSingle().Subject;
        rule.X1.Should().BeApproximately(779.5, 0.05);
        rule.X2.Should().BeApproximately(1082.7, 0.05);
        rule.Y1.Should().BeApproximately(50.9, 0.05);
        rule.Y2.Should().BeApproximately(50.9, 0.05);
        rule.StrokeWidth.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void Large_filled_rectangle_is_not_a_line()
    {
        string pdf = Write("block.pdf", PdfFixtures.BuildPageWithFilledRectangle(Landscape, 100, 100, 200, 50));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        page.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Page_size_and_number_are_reported()
    {
        string pdf = Write("size.pdf", PdfFixtures.BuildPage(new PtSize(612, 792), Array.Empty<FixtureText>(), Array.Empty<FixtureLine>()));

        PageGeometry page = _dump.Dump(pdf, new[] { 1 }).Single();

        page.PageNumber.Should().Be(1);
        page.WidthPt.Should().Be(612);
        page.HeightPt.Should().Be(792);
    }

    [Fact]
    public void Template_fixture_pages_carry_their_probe_and_slashes_where_the_manifest_says()
    {
        Manifest m = new ManifestLoader().LoadShipped(TestPaths.TemplatesDir);
        string pdf = PdfFixtures.WriteTemplateLike(Path.Combine(_tempDir, "template.pdf"), m);
        int monday = m.PageTypes.Weekly.PdfPageFor(1, PageNames.Mon);

        PageGeometry page = _dump.Dump(pdf, new[] { monday }).Single();

        WordGeometry heading = page.Words.Should().ContainSingle(w => w.Text == "Monday").Subject;
        heading.X.Should().BeApproximately(m.Generated.Heading.X, 1.0);
        heading.Baseline.Should().BeApproximately(m.Generated.Heading.BaselineY, 1.0);
        heading.FontSizePt.Should().BeApproximately(m.Generated.Heading.SizePt, 0.01);
        foreach (LinePt slash in m.Slots.Date.Slashes)
        {
            page.Lines.Should().Contain(l =>
                Math.Abs(l.X1 - slash.X1) < 0.05 && Math.Abs(l.Y1 - slash.Y1) < 0.05 && Math.Abs(l.X2 - slash.X2) < 0.05 && Math.Abs(l.Y2 - slash.Y2) < 0.05);
        }

        page.Words.Where(w => w.FontSizePt == m.Slots.WeeksRemaining.SizePt && int.TryParse(w.Text, out _))
            .Should().HaveCount(m.Slots.WeeksRemaining.NumberX.Count, "the strip numbers are placed at numberX");
    }

    [Fact]
    public void Requesting_a_page_outside_the_document_throws()
    {
        string pdf = Write("one.pdf", PdfFixtures.BuildPage(Landscape, Array.Empty<FixtureText>(), Array.Empty<FixtureLine>()));

        Action act = () => _dump.Dump(pdf, new[] { 1, 2 });

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Page 2*1 pages*");
    }

    [Fact]
    public void Missing_file_throws_file_not_found()
    {
        Action act = () => _dump.Dump(Path.Combine(_tempDir, "absent.pdf"), new[] { 1 });

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Table_printer_lists_words_and_lines_under_headers()
    {
        string pdf = Write("table.pdf", PdfFixtures.BuildPage(
            Landscape,
            new[] { new FixtureText("Monday", 23, 47, 32) },
            new[] { new FixtureLine(900.2, 16.9, 918.8, 41.0, 1) }));
        IReadOnlyList<PageGeometry> pages = _dump.Dump(pdf, new[] { 1 });

        string text = PdfGeometryTable.Format(pages);

        text.Should().StartWith("== Page 1: 1112 x 770 pt, 1 words, 1 lines ==");
        text.Should().Contain("WORDS").And.Contain("LINES");
        text.Should().MatchRegex(@"Monday\s+2[23]\.\d\s+4[67]\.\d");
        text.Should().MatchRegex(@"900\.2\s+16\.9\s+918\.8\s+41\.0\s+1\.00");
    }

    [Fact]
    public void Default_pages_are_the_cover_calendar_and_first_week_pages()
    {
        PdfGeometryDump.DefaultPages.Should().Equal(1, 13, 16, 17, 24);
    }

    [SkippableFact]
    public void Real_planner_monday_page_has_the_day_name_at_the_heading_position()
    {
        string pdf = SfpPdf.RequireOrSkip();
        Manifest m = new ManifestLoader().LoadShipped(TestPaths.TemplatesDir);
        int monday = m.PageTypes.Weekly.PdfPageFor(1, PageNames.Mon);

        PageGeometry page = _dump.Dump(pdf, new[] { monday }).Single();

        WordGeometry heading = page.Words.Should().ContainSingle(w => string.Equals(w.Text, "Monday", StringComparison.OrdinalIgnoreCase)).Subject;
        heading.X.Should().BeApproximately(m.Generated.Heading.X, 1.0);
        heading.Baseline.Should().BeApproximately(m.Generated.Heading.BaselineY, 1.0);
        heading.FontSizePt.Should().BeApproximately(m.Generated.Heading.SizePt, 0.5);
    }

    private string Write(string name, byte[] pdf)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, pdf);
        return path;
    }
}
