using System.Globalization;
using Sfp.Core.Options;

namespace Sfp.Core.Tests.Options;

/// <summary>The Generate sentences shown by more than one front end.</summary>
public class GenerateMessagesTests
{
    [Fact]
    public void The_partial_notebook_warning_keeps_the_orchestrators_wording_and_is_recognized()
    {
        const string path = @"D:\Notebooks\SFP Q1 2027";
        const string reason = "Access to the path is denied.";

        string warning = GenerateMessages.PartialNotebookNotDeleted(path, reason);

        // GenerateOrchestrator.DeletePartialNotebook formats "The partial notebook at {0} could not be deleted: {1}".
        warning.Should().Be(string.Format(CultureInfo.InvariantCulture, "The partial notebook at {0} could not be deleted: {1}", path, reason));
        GenerateMessages.IsPartialNotebookNotDeleted(warning).Should().BeTrue();
    }

    [Theory]
    [InlineData("Font 'Bookman Old Style' is not installed; using the bundled 'TeX Gyre Bonum' instead.")]
    [InlineData("The partial notebook at D:\\x was left in place for inspection.")]
    [InlineData("")]
    public void Other_warnings_are_not_taken_for_a_failed_deletion(string warning)
    {
        GenerateMessages.IsPartialNotebookNotDeleted(warning).Should().BeFalse();
        GenerateMessages.IsPartialNotebookNotDeleted(GenerateMessages.OneDriveWarning(@"C:\Users\me\OneDrive")).Should().BeFalse();
    }
}
