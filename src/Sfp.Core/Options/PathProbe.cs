namespace Sfp.Core.Options;

/// <summary>The file-system facts the Generate form validator needs, behind an interface so the rules are tested without a disk.</summary>
public interface IPathProbe
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>True when a file could be created in the directory (or, if it does not exist yet, in its nearest existing ancestor).</summary>
    bool CanWrite(string directory);
}

/// <summary>The real file system.</summary>
public sealed class PathProbe : IPathProbe
{
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public bool CanWrite(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string? probe = Path.GetFullPath(directory);
            while (probe is not null && !Directory.Exists(probe))
            {
                probe = Path.GetDirectoryName(probe);
            }

            if (probe is null)
            {
                return false;
            }

            string marker = Path.Combine(probe, ".sfp2onenote-write-check-" + Guid.NewGuid().ToString("N"));
            using (File.Create(marker, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
