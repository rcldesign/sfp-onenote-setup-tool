using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Writer;
using Sfp.OneNote.Writer;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>Every row of the PRD's pre-run checks, each driven by an injected OneNote locator and file system.</summary>
public class GeneratePreflightTests
{
    private static readonly Manifest M = Support.TestManifest.Load();
    private static readonly string Out = Path.Combine("/notebooks", "out");

    private sealed class Harness
    {
        public FakeOneNoteApp App { get; } = new();

        public FakeOneNoteLocator Locator { get; set; }

        public FakePreflightFileSystem Fs { get; } = new();

        public FakeManifestValidator Validator { get; set; } = new();

        public List<string> FontWarnings { get; } = new();

        public Harness()
        {
            Locator = new FakeOneNoteLocator(true, App);
        }

        public GeneratePreflight Build() => new(Validator, Locator, Fs, new SidecarStore(), new DateEngine(), FontWarnings);

        public static GenerateRequest Request(GenerateOptions? options = null, bool renderOnly = false, string? renderDir = null) =>
            new(options ?? Options(), M, TestPaths.TemplatesDir, renderOnly, renderDir);

        public static GenerateOptions Options() => new() { Start = new QuarterSpec(2027, 1), PdfPath = "/planner/SFP_No_Dates.pdf", OutputFolder = Out };
    }

    [Fact]
    public void A_ready_machine_passes_with_no_errors_and_no_warnings()
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Ok.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        result.Warnings.Should().BeEmpty();
        h.Locator.OpenCalls.Should().Be(1);
        h.App.CallsOf("GetHierarchy").Should().Equal(":Notebooks");
        h.Validator.Paths.Should().Equal("/planner/SFP_No_Dates.pdf");
        h.Fs.EnsuredWritable.Should().Equal(Out);
    }

    [Fact]
    public void OneNote_not_installed_gives_the_prd_message_with_exit_3_and_never_opens_it()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Contain((OneNoteUnavailableException.UserMessage, 3));
        h.Locator.OpenCalls.Should().Be(0);
    }

    [Fact]
    public void No_open_notebook_means_not_signed_in()
    {
        Harness h = new();
        h.App.PreexistingNotebooks.Clear();

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Contain((GenerateText.SignInMessage, 3));
    }

    [Fact]
    public void A_com_session_that_cannot_start_is_reported_as_unavailable()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(true, () => throw new OneNoteUnavailableException("activate", OneNoteHResults.ServerExecFailed, "OneNote desktop is required. CreateInstance failed", null)) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().ContainSingle(e => e.ExitCode == 3 && e.Message.Contains("CreateInstance failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hierarchy_call_that_fails_for_another_reason_is_reported_with_its_message()
    {
        Harness h = new();
        h.App.GetHierarchyFailure = () => new OneNoteBusyException("GetHierarchy", OneNoteHResults.RpcCallRejected, null);

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().ContainSingle(e => e.ExitCode == 3 && e.Message.StartsWith("OneNote could not be reached", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdf_mismatches_are_reported_one_per_line_with_exit_2()
    {
        Harness h = new() { Validator = new FakeManifestValidator(new ValidationResult(new[] { "Expected 132 pages, found 130", "Page 17 does not contain 'Monday'" }, new[] { "No probe text for weekly page 'sun'; page not checked." })) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Equal(("Expected 132 pages, found 130", 2), ("Page 17 does not contain 'Monday'", 2));
        result.Warnings.Should().Equal("No probe text for weekly page 'sun'; page not checked.");
    }

    [Theory]
    [InlineData(1899, 1)]
    [InlineData(2200, 1)]
    [InlineData(2027, 13)]
    [InlineData(2027, 0)]
    public void Start_month_outside_the_range_is_refused(int year, int month)
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { Start = new QuarterSpec(year, month) }));

        result.Errors.Should().Contain((GenerateText.StartMonthOutOfRange(year, month), 2));
    }

    [Theory]
    [InlineData("SFP:Q1")]
    [InlineData("a/b")]
    [InlineData("ThisNameIsFarTooLongToBeAcceptedBecauseItGoesWellPastSixtyFourCharacters")]
    public void A_notebook_name_breaking_the_rule_is_refused(string name)
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { NotebookName = name }));

        result.Errors.Should().Contain((NotebookNames.RuleMessage, 2));
    }

    [Fact]
    public void Output_folder_that_cannot_be_written_is_refused_and_the_default_is_used_when_none_is_given()
    {
        Harness h = new();
        h.Fs.Unwritable[h.Fs.DefaultOutputFolder] = "Access to the path is denied.";

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { OutputFolder = null }));

        result.Errors.Should().Contain((GenerateText.FolderNotWritable(h.Fs.DefaultOutputFolder, "Access to the path is denied."), 2));
        h.Fs.EnsuredWritable.Should().Equal(h.Fs.DefaultOutputFolder);
    }

    [Fact]
    public void A_folder_under_onedrive_only_warns()
    {
        Harness h = new();
        h.Fs.OneDrive.Add("/home/user/OneDrive");
        string folder = "/home/user/OneDrive/Documents/Notebooks";

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { OutputFolder = folder }));

        result.Ok.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be(GenerateText.OneDriveWarning(folder));
    }

    [Fact]
    public void An_existing_notebook_folder_of_the_same_name_blocks_with_the_prd_message()
    {
        Harness h = new();
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Equal((GenerateText.Collision("SFP Q1 2027"), 2));
    }

    [Fact]
    public void An_existing_sidecar_of_the_same_name_blocks_too()
    {
        Harness h = new();
        h.Fs.Files.Add(new SidecarStore().PathFor(Path.Combine(Out, "My Quarter")));

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { NotebookName = "My Quarter" }));

        result.Errors.Should().Equal((GenerateText.Collision("My Quarter"), 2));
    }

    [Fact]
    public void Font_fallback_warnings_are_passed_through()
    {
        Harness h = new();
        h.FontWarnings.Add("Font 'Bookman Old Style' is not installed; using the bundled 'TeX Gyre Bonum' instead.");

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Ok.Should().BeTrue();
        result.Warnings.Should().Equal(h.FontWarnings);
    }

    [Fact]
    public void Render_only_skips_the_onenote_and_notebook_checks_and_needs_a_writable_output_folder()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")) };
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult ok = h.Build().Check(Harness.Request(renderOnly: true, renderDir: "/png"));
        PreflightResult missing = h.Build().Check(Harness.Request(renderOnly: true, renderDir: null));
        h.Fs.Unwritable["/png"] = "read only";
        PreflightResult unwritable = h.Build().Check(Harness.Request(renderOnly: true, renderDir: "/png"));

        ok.Ok.Should().BeTrue(string.Join("; ", ok.Errors.Select(e => e.Message)));
        missing.Errors.Should().ContainSingle(e => e.ExitCode == 2 && e.Message.Contains("output folder", StringComparison.Ordinal));
        unwritable.Errors.Should().Equal((GenerateText.FolderNotWritable("/png", "read only"), 2));
        h.Locator.OpenCalls.Should().Be(0);
    }

    [Fact]
    public void Errors_keep_the_prd_order_onenote_first_then_validation()
    {
        Harness h = new() { Validator = new FakeManifestValidator(ValidationResult.Fail("Expected 132 pages, found 130")) };
        h.App.PreexistingNotebooks.Clear();
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Select(e => e.ExitCode).Should().Equal(3, 2, 2);
        result.Errors[0].Message.Should().Be(GenerateText.SignInMessage);
    }

    [Theory]
    [InlineData("<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"/>", false)]
    [InlineData("<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"><one:Notebook name=\"A\" ID=\"{a}\"/></one:Notebooks>", true)]
    [InlineData("", false)]
    [InlineData("not xml", false)]
    public void Has_open_notebook_reads_the_hierarchy(string xml, bool expected)
    {
        GeneratePreflight.HasOpenNotebook(xml).Should().Be(expected);
    }

    [Fact]
    public void Production_file_system_creates_the_folder_probes_a_write_and_lists_onedrive_folders()
    {
        PreflightFileSystem fs = new();
        string folder = Path.Combine(TestPaths.NewTempDir("preflight"), "new", "nested");

        fs.EnsureWritable(folder).Should().BeNull();
        fs.DirectoryExists(folder).Should().BeTrue();
        Directory.EnumerateFiles(folder).Should().BeEmpty("the probe file is removed");
        fs.FileExists(Path.Combine(folder, "nothing")).Should().BeFalse();
        fs.OneDriveFolders.Should().NotContainNulls();
        fs.DefaultOutputFolder.Should().EndWith(OutputFolders.DefaultFolderName);
    }
}
