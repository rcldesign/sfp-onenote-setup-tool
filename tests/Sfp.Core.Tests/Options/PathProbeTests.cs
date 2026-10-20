using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options;

/// <summary>The real file-system probe behind the form validator.</summary>
public class PathProbeTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("probe");
    private readonly PathProbe _probe = new();

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Reports_files_and_directories()
    {
        string file = Path.Combine(_dir, "a.pdf");
        File.WriteAllText(file, "x");

        _probe.FileExists(file).Should().BeTrue();
        _probe.FileExists(_dir).Should().BeFalse();
        _probe.DirectoryExists(_dir).Should().BeTrue();
        _probe.DirectoryExists(file).Should().BeFalse();
        _probe.FileExists("").Should().BeFalse();
        _probe.DirectoryExists(" ").Should().BeFalse();
    }

    [Fact]
    public void Can_write_to_an_existing_folder_and_to_a_folder_that_could_be_created_under_it()
    {
        _probe.CanWrite(_dir).Should().BeTrue();
        _probe.CanWrite(Path.Combine(_dir, "new", "deeper")).Should().BeTrue();
        Directory.GetFileSystemEntries(_dir).Should().BeEmpty("the write check leaves nothing behind");
        _probe.CanWrite("").Should().BeFalse();
    }
}
