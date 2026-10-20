namespace Sfp.App.ViewModels.Services;

/// <summary>Operating-system integrations the success and failure views use: clipboard, launching OneNote, copying a file.</summary>
public interface IShellService
{
    void CopyToClipboard(string text);

    /// <summary>Opens a notebook folder in OneNote desktop (onenote.exe with the path, else the onenote: link). Throws when neither works.</summary>
    void OpenInOneNote(string notebookPath);

    /// <summary>Copies a file, overwriting the destination.</summary>
    void CopyFile(string sourcePath, string destinationPath);
}
