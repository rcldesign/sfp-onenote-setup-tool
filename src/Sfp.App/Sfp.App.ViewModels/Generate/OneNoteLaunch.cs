namespace Sfp.App.ViewModels.Generate;

/// <summary>How the Open in OneNote button opens a notebook folder: onenote.exe with the path first, the onenote: link second.</summary>
public static class OneNoteLaunch
{
    /// <summary>The commands to try in order; the first that starts wins.</summary>
    public static IReadOnlyList<(string FileName, string Arguments)> Commands(string notebookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookPath);
        string trimmed = notebookPath.Trim().TrimEnd('\\', '/');
        return new[]
        {
            ("onenote.exe", "\"" + trimmed + "\""),
            (Link(trimmed), ""),
        };
    }

    /// <summary>The onenote: link for a notebook folder, "onenote:///C:\Users\me\Documents\OneNote Notebooks\SFP Q1 2027".</summary>
    public static string Link(string notebookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notebookPath);
        return "onenote:///" + notebookPath.Trim().TrimEnd('\\', '/');
    }
}
