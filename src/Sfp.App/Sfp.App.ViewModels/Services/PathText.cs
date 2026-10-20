namespace Sfp.App.ViewModels.Services;

/// <summary>
/// Path text helpers that treat both slash kinds as separators, so the view models give the same answer for a
/// Windows path whatever OS the tests run on (System.IO.Path follows the host's rules).
/// </summary>
public static class PathText
{
    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>The path without trailing separators or surrounding whitespace.</summary>
    public static string Trim(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Trim().TrimEnd(Separators);
    }

    /// <summary>The folder holding the path ("C:\Notebooks" for "C:\Notebooks\SFP Q1 2027\"), or null when there is none.</summary>
    public static string? ParentOf(string path)
    {
        string trimmed = Trim(path);
        int cut = trimmed.LastIndexOfAny(Separators);
        if (cut < 0)
        {
            return null;
        }

        if (cut == 0)
        {
            return trimmed[..1];
        }

        string parent = trimmed[..cut];
        return parent.Length == 2 && parent[1] == ':' ? parent + trimmed[cut] : parent;
    }

    /// <summary>The last segment of the path ("run.log" for "C:\logs\run.log").</summary>
    public static string FileNameOf(string path)
    {
        string trimmed = Trim(path);
        int cut = trimmed.LastIndexOfAny(Separators);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }
}
