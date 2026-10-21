namespace Sfp.Refresh.Calendar;

/// <summary>
/// Where calendar files and holiday lists may be read from: full local paths only. Anyone the notebook is shared with can
/// edit its Setup page, and the daily task reads these paths as the user, so a network path (\\server\share or
/// //server/share, WebDAV included) or a device path (\\?\ or \\.\) would make it connect to any host with the user's
/// credentials, and a relative path would depend on the task's working folder.
/// </summary>
public static class LocalPath
{
    /// <summary>
    /// Null when <paramref name="path"/> is a full local path: a drive path such as C:\Users\me\work.ics (off Windows,
    /// where the tests run, an absolute path); otherwise the reason it is refused, for the source's failure line.
    /// </summary>
    public static string? Problem(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string p = path.Trim();
        if (p.Length >= 2 && IsSeparator(p[0]) && IsSeparator(p[1]))
        {
            return $"'{path}' is a network or device path; calendar files and holiday lists are read only from a local drive, such as C:\\Users\\me\\calendar.ics.";
        }

        if (IsDrivePath(p) || (!OperatingSystem.IsWindows() && p.StartsWith('/')))
        {
            return null;
        }

        return $"'{path}' is not a full local path; write it with its drive, such as C:\\Users\\me\\calendar.ics.";
    }

    /// <summary>True when the path starts at a root or names a drive, so it is not taken under another folder.</summary>
    public static bool IsRooted(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string p = path.Trim();
        return p.Length > 0 && (IsSeparator(p[0]) || (p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':'));
    }

    private static bool IsDrivePath(string p) => p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && IsSeparator(p[2]);

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
