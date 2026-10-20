using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options;

/// <summary>M6.2: every row of the PRD main screen table, one rule at a time.</summary>
public class GenerateFormValidatorTests
{
    private const string Pdf = @"C:\planner\SFP_No_Dates.pdf";
    private const string Folder = @"C:\Users\me\Documents\OneNote Notebooks";

    private readonly FakePathProbe _paths = new();
    private readonly ScriptedPdfValidator _pdf = new();
    private readonly GenerateFormValidator _validator;

    public GenerateFormValidatorTests()
    {
        _paths.Files.Add(Pdf);
        _paths.Directories.Add(Folder);
        _validator = new GenerateFormValidator(_pdf, _paths, new[] { @"C:\Users\me\OneDrive" });
    }

    private static GenerateFormInput Good() => new(Pdf, 2027, 1, "SFP Q1 2027", Folder, 220, 1.0, TimeBlockWindow.Default);

    [Fact]
    public void A_good_form_with_a_matching_pdf_can_generate_and_has_no_messages()
    {
        GenerateFormValidation v = _validator.Validate(Good(), ValidationResult.Ok);

        v.Messages.Should().BeEmpty();
        v.CanGenerate.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pdf_row_requires_a_path(string? path)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { PdfPath = path }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.PdfPath).Should().Be("Choose the SFP template PDF.");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Pdf_row_requires_the_file_to_exist()
    {
        GenerateFormValidation v = _validator.Validate(Good() with { PdfPath = @"C:\nope.pdf" }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.PdfPath).Should().Be(@"PDF not found: C:\nope.pdf");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Pdf_row_is_pending_until_the_manifest_check_has_run()
    {
        GenerateFormValidation v = _validator.Validate(Good(), pdfResult: null);

        v.PendingFor(GenerateFormFields.PdfPath).Should().Contain("Checking the PDF");
        v.ErrorFor(GenerateFormFields.PdfPath).Should().BeNull();
        v.BlockingFor(GenerateFormFields.PdfPath).Should().NotBeNull();
        v.CanGenerate.Should().BeFalse("Generate waits for the check");
    }

    [Fact]
    public void Pdf_row_shows_every_mismatch_and_disables_generate()
    {
        ValidationResult mismatch = ValidationResult.Fail("Expected 132 pages, found 130", "Page 17 does not contain 'Monday'");

        GenerateFormValidation v = _validator.Validate(Good(), mismatch);

        v.ErrorFor(GenerateFormFields.PdfPath).Should().Be("Expected 132 pages, found 130\nPage 17 does not contain 'Monday'");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Pdf_row_passes_validator_warnings_through_without_blocking()
    {
        ValidationResult warned = new(Array.Empty<string>(), new[] { "No probe text for weekly page 'sat'; page not checked." });

        GenerateFormValidation v = _validator.Validate(Good(), warned);

        v.WarningFor(GenerateFormFields.PdfPath).Should().Contain("sat");
        v.CanGenerate.Should().BeTrue();
    }

    [Fact]
    public void CheckPdf_reports_a_missing_path_or_file_without_opening_anything()
    {
        _validator.CheckPdf(TestManifest.Shipped, null).Errors.Should().ContainSingle().Which.Should().Be("Choose the SFP template PDF.");
        _validator.CheckPdf(TestManifest.Shipped, @"C:\nope.pdf").Errors.Should().ContainSingle().Which.Should().Be(@"PDF not found: C:\nope.pdf");
        _pdf.Calls.Should().Be(0);
    }

    [Fact]
    public void CheckPdf_delegates_an_existing_file_to_the_manifest_validator()
    {
        _pdf.Result = ValidationResult.Fail("Expected 132 pages, found 130");

        ValidationResult result = _validator.CheckPdf(TestManifest.Shipped, Pdf);

        result.Errors.Should().Equal("Expected 132 pages, found 130");
        _pdf.Calls.Should().Be(1);
        _pdf.LastPath.Should().Be(Pdf);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2200)]
    [InlineData(null)]
    public void Start_row_rejects_years_outside_1900_to_2199_and_unparsed_text(int? year)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { Year = year }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.Start).Should().Be("Year must be a whole number between 1900 and 2199.");
        v.CanGenerate.Should().BeFalse();
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(2199)]
    public void Start_row_accepts_the_year_limits(int year)
    {
        _validator.Validate(Good() with { Year = year }, ValidationResult.Ok).ErrorFor(GenerateFormFields.Start).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Start_row_rejects_an_impossible_month(int month)
    {
        _validator.Validate(Good() with { Month = month }, ValidationResult.Ok).ErrorFor(GenerateFormFields.Start).Should().Contain("month");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_row_requires_a_name(string? name)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { NotebookName = name }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.NotebookName).Should().Be("Enter a notebook name.");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Name_row_allows_64_characters_and_refuses_65()
    {
        string sixtyFour = new('a', 64);
        _validator.Validate(Good() with { NotebookName = sixtyFour }, ValidationResult.Ok).ErrorFor(GenerateFormFields.NotebookName).Should().BeNull();

        GenerateFormValidation v = _validator.Validate(Good() with { NotebookName = sixtyFour + "b" }, ValidationResult.Ok);
        v.ErrorFor(GenerateFormFields.NotebookName).Should().Contain("1 to 64 characters").And.Contain("65");
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('|')]
    [InlineData('#')]
    [InlineData('%')]
    public void Name_row_refuses_each_forbidden_character(char c)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { NotebookName = "SFP " + c + " 2027" }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.NotebookName).Should().Contain("cannot contain " + c);
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Name_row_lists_every_forbidden_character_found_once()
    {
        GenerateFormValidator.ForbiddenCharsIn("a:b:c/d").Should().Equal(':', '/');
        GenerateFormValidator.ForbiddenCharsIn("plain name").Should().BeEmpty();
    }

    [Fact]
    public void Name_row_blocks_a_notebook_folder_that_already_exists_in_the_output_folder()
    {
        _paths.Directories.Add(Path.Combine(Folder, "SFP Q1 2027"));

        GenerateFormValidation v = _validator.Validate(Good(), ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.NotebookName).Should().Be("A notebook named SFP Q1 2027 already exists here. Rename or choose another folder.");
        v.CanGenerate.Should().BeFalse();
        _validator.Validate(Good() with { NotebookName = "SFP Q1 2027 (2)" }, ValidationResult.Ok).CanGenerate.Should().BeTrue();
    }

    [Fact]
    public void Name_collision_check_trims_the_typed_name()
    {
        _paths.Directories.Add(Path.Combine(Folder, "SFP Q1 2027"));

        _validator.Validate(Good() with { NotebookName = "  SFP Q1 2027  " }, ValidationResult.Ok)
            .ErrorFor(GenerateFormFields.NotebookName).Should().Contain("already exists");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Folder_row_requires_a_folder(string? folder)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { OutputFolder = folder }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.OutputFolder).Should().Be("Choose an output folder.");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Folder_row_requires_a_writable_folder()
    {
        _paths.ReadOnlyDirectories.Add(@"C:\Windows");

        GenerateFormValidation v = _validator.Validate(Good() with { OutputFolder = @"C:\Windows" }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.OutputFolder).Should().Be(@"Output folder is not writable: C:\Windows");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Folder_row_rejects_invalid_path_characters()
    {
        GenerateFormValidation v = _validator.Validate(Good() with { OutputFolder = "C:\\bad\0name" }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.OutputFolder).Should().Be("Output folder is not a valid path.");
    }

    [Fact]
    public void Folder_row_warns_under_a_onedrive_root_but_allows_generate()
    {
        string underOneDrive = Path.Combine(@"C:\Users\me\OneDrive", "Documents", "Notebooks");

        GenerateFormValidation v = _validator.Validate(Good() with { OutputFolder = underOneDrive }, ValidationResult.Ok);

        v.WarningFor(GenerateFormFields.OutputFolder).Should().Contain("OneDrive").And.Contain("refuses");
        v.ErrorFor(GenerateFormFields.OutputFolder).Should().BeNull();
        v.CanGenerate.Should().BeTrue();
    }

    [Fact]
    public void OneDrive_detection_matches_the_root_itself_a_child_and_a_segment_named_like_onedrive()
    {
        _validator.IsUnderOneDrive(@"C:\Users\me\OneDrive").Should().BeTrue();
        _validator.IsUnderOneDrive(@"C:\Users\me\OneDrive\Documents").Should().BeTrue();
        _validator.IsUnderOneDrive(@"C:\Users\me\OneDrive - Contoso\Notebooks").Should().BeTrue();
        _validator.IsUnderOneDrive(@"C:\Users\me\Documents\OneNote Notebooks").Should().BeFalse();
        _validator.IsUnderOneDrive(@"C:\Users\me\OneDriveX").Should().BeTrue("a segment that starts with OneDrive is a sync folder name");
        _validator.IsUnderOneDrive(@"C:\Users\me\Documents\MyOneDrive").Should().BeFalse();
    }

    [Fact]
    public void OneDrive_detection_ignores_blank_roots()
    {
        GenerateFormValidator v = new(_pdf, _paths, new[] { "", "  " });
        v.IsUnderOneDrive(@"C:\Users\me\Documents").Should().BeFalse();
    }

    [Theory]
    [InlineData(72)]
    [InlineData(0)]
    [InlineData(600)]
    public void Dpi_row_allows_only_144_220_and_300(int dpi)
    {
        _validator.Validate(Good() with { Dpi = dpi }, ValidationResult.Ok).ErrorFor(GenerateFormFields.Dpi).Should().Be("Render DPI must be 144, 220 or 300.");
        foreach (int ok in new[] { 144, 220, 300 })
        {
            _validator.Validate(Good() with { Dpi = ok }, ValidationResult.Ok).ErrorFor(GenerateFormFields.Dpi).Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(0.24)]
    [InlineData(2.01)]
    [InlineData(double.NaN)]
    public void Page_scale_row_requires_a_number_between_a_quarter_and_two(double? scale)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { PageScale = scale }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.PageScale).Should().Be("Page scale must be a number between 0.25 and 2.");
        v.CanGenerate.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.72)]
    [InlineData(2.0)]
    public void Page_scale_row_accepts_the_bounds(double scale)
    {
        _validator.Validate(Good() with { PageScale = scale }, ValidationResult.Ok).ErrorFor(GenerateFormFields.PageScale).Should().BeNull();
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(10, 9)]
    [InlineData(-1, 10)]
    [InlineData(6, 25)]
    public void Time_block_row_requires_a_well_formed_window(int start, int end)
    {
        GenerateFormValidation v = _validator.Validate(Good() with { TimeBlocks = new TimeBlockWindow(start, end) }, ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.TimeBlocks).Should().Contain("time-block window");
        v.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Several_bad_rows_report_every_message_at_once()
    {
        GenerateFormInput bad = new(null, 1800, 1, "", "", 72, null, new TimeBlockWindow(9, 9));

        GenerateFormValidation v = _validator.Validate(bad, null);

        v.Messages.Select(m => m.Field).Should().Equal(
            GenerateFormFields.PdfPath,
            GenerateFormFields.Start,
            GenerateFormFields.NotebookName,
            GenerateFormFields.OutputFolder,
            GenerateFormFields.Dpi,
            GenerateFormFields.PageScale,
            GenerateFormFields.TimeBlocks);
        v.Messages.Should().AllSatisfy(m => m.Kind.Should().Be(FormMessageKind.Error));
    }

    private sealed class ScriptedPdfValidator : IManifestValidator
    {
        public ValidationResult Result { get; set; } = ValidationResult.Ok;

        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        public ValidationResult Validate(Manifest m, string pdfPath)
        {
            Calls++;
            LastPath = pdfPath;
            return Result;
        }
    }
}
