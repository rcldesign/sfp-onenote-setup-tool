using Sfp.App.ViewModels.Generate;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Tests.Options.ViewModels;

namespace Sfp.Core.Tests.Options;

/// <summary>
/// Fix round 2 app-12: a typed relative output folder (and previous notebook) is made absolute before it reaches
/// OneNote, and the form checks and names the folder it resolves to. Fully qualified paths, Windows drive and UNC paths
/// included on every OS, are kept as typed.
/// </summary>
public class OutputFolderResolutionTests
{
    private const string Pdf = @"C:\planner\SFP_No_Dates.pdf";

    private readonly FakePathProbe _paths = new();
    private readonly GenerateFormValidator _validator;
    private readonly Harness _h = new();

    public OutputFolderResolutionTests()
    {
        _paths.Files.Add(Pdf);
        _validator = new GenerateFormValidator(new ScriptedManifestValidator(), _paths, new[] { @"C:\Users\me\OneDrive" });
    }

    private static GenerateFormInput Input(string folder) => new(Pdf, 2027, 1, "SFP Q1 2027", folder, 220, 1.0, TimeBlockWindow.Default);

    [Theory]
    [InlineData(@"C:\Users\me\Notebooks", true)]
    [InlineData("C:/Users/me/Notebooks", true)]
    [InlineData(@"\\server\share\Notebooks", true)]
    [InlineData("Notebooks", false)]
    [InlineData(@"..\Notebooks", false)]
    [InlineData("../Notebooks", false)]
    [InlineData("C:Notebooks", false)]
    [InlineData(@"\Notebooks", false)]
    public void Fully_qualified_means_the_same_place_whatever_the_working_folder(string path, bool expected)
    {
        FolderPaths.IsFullyQualified(path).Should().Be(expected);
    }

    [Fact]
    public void Resolve_trims_keeps_fully_qualified_paths_and_makes_the_rest_absolute()
    {
        FolderPaths.Resolve(@"  C:\Users\me\Notebooks  ").Should().Be(@"C:\Users\me\Notebooks");
        FolderPaths.Resolve(@"\\server\share\Notebooks").Should().Be(@"\\server\share\Notebooks");
        FolderPaths.Resolve("  Notebooks ").Should().Be(Path.GetFullPath("Notebooks"));
        FolderPaths.Resolve("../Notebooks").Should().Be(Path.GetFullPath("../Notebooks"));
        FolderPaths.Resolve("   ").Should().BeEmpty();
    }

    [Fact]
    public void A_relative_folder_is_checked_and_named_as_the_folder_it_resolves_to()
    {
        string full = Path.GetFullPath("Notebooks");

        GenerateFormValidation v = _validator.Validate(Input(" Notebooks "), ValidationResult.Ok);

        v.ErrorFor(GenerateFormFields.OutputFolder).Should().BeNull();
        v.WarningFor(GenerateFormFields.OutputFolder).Should().Be(
            "The folder is relative to the folder the app started in; the notebook is created in " + full + ".");
        v.CanGenerate.Should().BeTrue("the note is a warning");

        _paths.ReadOnlyDirectories.Add(full);
        _validator.Validate(Input("Notebooks"), ValidationResult.Ok).ErrorFor(GenerateFormFields.OutputFolder)
            .Should().Be("Output folder is not writable: " + full);

        _paths.ReadOnlyDirectories.Clear();
        _paths.Directories.Add(Path.Combine(full, "SFP Q1 2027"));
        _validator.Validate(Input("Notebooks"), ValidationResult.Ok).ErrorFor(GenerateFormFields.NotebookName)
            .Should().Contain("already exists", "the name collision is looked up in the resolved folder");
    }

    [Fact]
    public void A_fully_qualified_folder_gets_no_note()
    {
        _validator.Validate(Input(@"  D:\Notebooks "), ValidationResult.Ok).Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task The_options_carry_the_resolved_output_folder_and_previous_notebook()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        await vm.PendingPdfCheck;

        vm.OutputFolder = " Notebooks ";
        vm.PreviousNotebook = "../Old/SFP Q4 2026";

        GenerateOptions o = vm.ToOptions();
        o.OutputFolder.Should().Be(Path.GetFullPath("Notebooks"));
        o.PreviousNotebook.Should().Be(Path.GetFullPath("../Old/SFP Q4 2026"));
        vm.ToRequest().Options.OutputFolder.Should().Be(Path.GetFullPath("Notebooks"), "the run hands OneNote an absolute folder");
        vm.FolderWarning.Should().Contain(Path.GetFullPath("Notebooks"));

        vm.OutputFolder = @"\\server\share\Notebooks";
        vm.ToOptions().OutputFolder.Should().Be(@"\\server\share\Notebooks");
        vm.FolderWarning.Should().BeNull();
    }
}
