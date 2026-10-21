namespace Sfp.Core.Options;

/// <summary>
/// Makes a typed folder absolute the way the Generate form and the run agree on. OneNote is a separate process with its
/// own working folder, so a relative output folder would land somewhere else than the form checked: a fully qualified
/// path is kept as typed, anything else (Notebooks, ..\Notebooks, C:Notebooks, \Notebooks) goes through
/// <see cref="Path.GetFullPath(string)"/> against the app's working folder, as the CLI does with --out.
/// </summary>
public static class FolderPaths
{
    /// <summary>
    /// True for a path that names one place whatever the working folder: the host OS's own rule, plus a Windows drive
    /// path (C:\ or C:/) and a UNC path (\\server\share) on any OS, so the form gives the same answer where tests run.
    /// </summary>
    public static bool IsFullyQualified(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.IsPathFullyQualified(path)
            || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
            || (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]));
    }

    /// <summary>
    /// The trimmed path, made absolute when it is not fully qualified. Empty stays empty, and a path the OS refuses to
    /// resolve is returned trimmed for the validator to report.
    /// </summary>
    public static string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string trimmed = path.Trim();
        if (trimmed.Length == 0 || IsFullyQualified(trimmed))
        {
            return trimmed;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
