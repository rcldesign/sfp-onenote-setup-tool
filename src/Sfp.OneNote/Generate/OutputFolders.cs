namespace Sfp.OneNote.Generate;

/// <summary>Where notebooks go when the user names no folder: Documents\OneNote Notebooks, OneNote's own default.</summary>
public static class OutputFolders
{
    public const string DefaultFolderName = "OneNote Notebooks";

    /// <summary>The default output folder on this machine.</summary>
    public static string Default() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DefaultFolderName);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lies beneath it (case-insensitive, separators normalized).</summary>
    public static bool IsUnder(string path, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string full = Normalize(path);
        string rootFull = Normalize(root);
        return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(rootFull + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = path;
        }

        return full.Replace('\\', '/').TrimEnd('/');
    }
}
