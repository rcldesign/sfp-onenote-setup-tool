using Sfp.OneNote.Generate;

namespace Sfp.OneNote.Tests.Generate;

public class OutputFoldersTests
{
    [Theory]
    [InlineData("/home/user/OneDrive/Notebooks", "/home/user/OneDrive", true)]
    [InlineData("/home/user/OneDrive", "/home/user/OneDrive", true)]
    [InlineData("/home/user/OneDrive/", "/home/user/OneDrive", true)]
    [InlineData("/home/user/onedrive/x", "/home/user/OneDrive", true)]
    [InlineData("/home/user/OneDriveBackup/x", "/home/user/OneDrive", false)]
    [InlineData("/home/user/Documents/Notebooks", "/home/user/OneDrive", false)]
    [InlineData("/home/user/Documents", "", false)]
    public void Is_under_compares_whole_path_segments_ignoring_case(string path, string root, bool expected)
    {
        OutputFolders.IsUnder(path, root).Should().Be(expected);
    }

    [Fact]
    public void Default_folder_is_onenote_notebooks_under_documents()
    {
        OutputFolders.Default().Should().EndWith(OutputFolders.DefaultFolderName);
    }
}
