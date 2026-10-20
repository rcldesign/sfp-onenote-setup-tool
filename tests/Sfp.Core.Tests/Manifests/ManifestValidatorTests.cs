using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Manifests;

/// <summary>Builds the correct 132-page fixture once for the class.</summary>
public sealed class ValidatorFixture : IDisposable
{
    public ValidatorFixture()
    {
        Manifest = new ManifestLoader().LoadShipped(TestPaths.TemplatesDir);
        TempDir = TestPaths.NewTempDir("validator");
        CorrectPdf = PdfFixtures.WriteTemplateLike(Path.Combine(TempDir, "correct.pdf"), Manifest);
    }

    public Manifest Manifest { get; }

    public string TempDir { get; }

    public string CorrectPdf { get; }

    public string Write(string name, PdfFixtureOptions options) =>
        PdfFixtures.WriteTemplateLike(Path.Combine(TempDir, name), Manifest, options);

    public void Dispose() => Directory.Delete(TempDir, recursive: true);
}

public class ManifestValidatorTests : IClassFixture<ValidatorFixture>
{
    private readonly ValidatorFixture _fx;
    private readonly ManifestValidator _validator = new();

    public ManifestValidatorTests(ValidatorFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void Correct_132_page_fixture_passes_validation()
    {
        ValidationResult result = _validator.Validate(_fx.Manifest, _fx.CorrectPdf);

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_131_page_pdf_fails_with_the_page_count_message()
    {
        string pdf = _fx.Write("short.pdf", new PdfFixtureOptions { PageCount = 131 });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Expected 132 pages, found 131");
    }

    [Fact]
    public void A_wrong_size_page_fails_with_the_size_message()
    {
        string pdf = _fx.Write("letter.pdf", new PdfFixtureOptions
        {
            PageSizeOverrides = new Dictionary<int, PtSize> { [5] = new(612, 792) },
        });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().ContainSingle().Which.Should().Be("Page 5 is 612 x 792 pt, expected 1112 x 770");
    }

    [Fact]
    public void A_page_within_one_point_of_the_manifest_size_passes()
    {
        string pdf = _fx.Write("nearly.pdf", new PdfFixtureOptions
        {
            PageSizeOverrides = new Dictionary<int, PtSize> { [7] = new(1112.9, 769.2) },
        });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_probe_fails_with_the_probe_message()
    {
        string pdf = _fx.Write("nomonday.pdf", new PdfFixtureOptions { OmitProbeOn = new HashSet<int> { 17 } });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().ContainSingle().Which.Should().Be("Page 17 does not contain 'Monday'");
    }

    [Fact]
    public void Fixed_page_types_are_probed_on_their_first_page_only()
    {
        string pdf = _fx.Write("probes.pdf", new PdfFixtureOptions { OmitProbeOn = new HashSet<int> { 1, 4, 14, 26 } });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().ContainSingle().Which.Should().Be("Page 1 does not contain 'Structured Focus Planner'");
    }

    [Fact]
    public void Weekly_probes_are_checked_on_week_run_one()
    {
        string pdf = _fx.Write("week1.pdf", new PdfFixtureOptions { OmitProbeOn = new HashSet<int> { 16, 24 } });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().BeEquivalentTo(
            "Page 16 does not contain 'Weekly Overview'",
            "Page 24 does not contain 'Weekly Reflection'");
    }

    [Fact]
    public void Several_mismatches_are_all_reported_with_the_page_count_first()
    {
        string pdf = _fx.Write("multi.pdf", new PdfFixtureOptions
        {
            PageCount = 130,
            PageSizeOverrides = new Dictionary<int, PtSize> { [2] = new(600, 400) },
            OmitProbeOn = new HashSet<int> { 13 },
        });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().Equal(
            "Expected 132 pages, found 130",
            "Page 2 is 600 x 400 pt, expected 1112 x 770",
            "Page 13 does not contain 'Monthly Calendar'");
    }

    [Fact]
    public void Probe_text_is_matched_without_regard_to_case()
    {
        string pdf = _fx.Write("upper.pdf", new PdfFixtureOptions { UpperCaseProbes = true });

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Weekly   Overview\nGoals", "Weekly Overview", true)]
    [InlineData("WEEKLY overview", "Weekly Overview", true)]
    [InlineData("Mon day", "Monday", true)]
    [InlineData("Tuesday Wednesday", "Monday", false)]
    [InlineData("", "Monday", false)]
    public void Probe_match_ignores_case_and_collapses_whitespace(string pageText, string probe, bool expected)
    {
        ManifestValidator.ContainsProbe(pageText, probe).Should().Be(expected);
    }

    [Fact]
    public void Missing_file_is_an_error_not_an_exception()
    {
        string path = Path.Combine(_fx.TempDir, "absent.pdf");

        ValidationResult result = _validator.Validate(_fx.Manifest, path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("absent.pdf");
    }

    [Fact]
    public void Unreadable_file_is_an_error_not_an_exception()
    {
        string path = Path.Combine(_fx.TempDir, "garbage.pdf");
        File.WriteAllText(path, "this is not a pdf");

        ValidationResult result = _validator.Validate(_fx.Manifest, path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Could not read PDF");
    }

    [Fact]
    public void Empty_path_is_an_error_not_an_exception()
    {
        ValidationResult result = _validator.Validate(_fx.Manifest, "");

        result.IsValid.Should().BeFalse();
    }

    [SkippableFact]
    public void Real_planner_pdf_passes_validation()
    {
        string pdf = SfpPdf.RequireOrSkip();

        ValidationResult result = _validator.Validate(_fx.Manifest, pdf);

        result.Errors.Should().BeEmpty();
    }
}
