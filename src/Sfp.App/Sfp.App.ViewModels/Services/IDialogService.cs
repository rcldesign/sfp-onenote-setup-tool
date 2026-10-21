namespace Sfp.App.ViewModels.Services;

/// <summary>The file and folder pickers, behind an interface so view models never touch WPF dialogs.</summary>
public interface IDialogService
{
    /// <summary>Open-file dialog; returns the chosen path or null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filter">WPF filter string, "PDF files (*.pdf)|*.pdf".</param>
    /// <param name="initialPath">A file or folder to start in, or null.</param>
    string? PickFile(string title, string filter, string? initialPath);

    /// <summary>Folder dialog; returns the chosen folder or null when cancelled.</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>Save-file dialog; returns the chosen path or null when cancelled.</summary>
    string? PickSaveFile(string title, string filter, string suggestedFileName);

    /// <summary>A yes/no question; true when the user answers yes.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="question">The question, worded so that yes is the action named.</param>
    bool Confirm(string title, string question);
}
