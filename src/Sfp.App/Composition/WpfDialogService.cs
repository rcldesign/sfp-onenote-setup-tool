using System.IO;
using System.Windows;
using Microsoft.Win32;
using Sfp.App.ViewModels.Services;

namespace Sfp.App.Composition;

/// <summary>The standard WPF file dialogs, the .NET 8 folder dialog and a yes/no message box.</summary>
public sealed class WpfDialogService : IDialogService
{
    public string? PickFile(string title, string filter, string? initialPath)
    {
        OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            if (File.Exists(initialPath))
            {
                dialog.FileName = initialPath;
                dialog.InitialDirectory = Path.GetDirectoryName(initialPath) ?? "";
            }
            else if (Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialFolder)
    {
        OpenFolderDialog dialog = new()
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string title, string filter, string suggestedFileName)
    {
        SaveFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            FileName = suggestedFileName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string question)
    {
        Window? owner = Application.Current?.MainWindow;
        MessageBoxResult answer = owner is null
            ? MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, question, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }
}
