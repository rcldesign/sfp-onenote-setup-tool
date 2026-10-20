using Sfp.Core.Options;

namespace Sfp.Core.Tests.Options;

/// <summary>An in-memory file system for the form validator: files, folders, and folders that refuse writes.</summary>
public sealed class FakePathProbe : IPathProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ReadOnlyDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool CanWrite(string directory) => !ReadOnlyDirectories.Contains(directory);
}
