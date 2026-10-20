namespace Sfp.TestSupport;

/// <summary>Locates repository folders from a test assembly's output directory.</summary>
public static class TestPaths
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    /// <summary>The repository root (the folder holding Sfp2OneNote.sln).</summary>
    public static string RepoRoot => RepoRootLazy.Value;

    /// <summary>templates/ in the repository.</summary>
    public static string TemplatesDir => Path.Combine(RepoRoot, "templates");

    /// <summary>templates/sfp-quarterly.json.</summary>
    public static string ManifestPath => Path.Combine(TemplatesDir, "sfp-quarterly.json");

    /// <summary>A fresh temporary folder for a test, deleted by the caller.</summary>
    public static string NewTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sfp2onenote-tests", prefix + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Sfp2OneNote.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not find Sfp2OneNote.sln above " + AppContext.BaseDirectory);
    }
}
