using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Sfp.App.ViewModels.Generate;
using Sfp.App.ViewModels.Services;

namespace Sfp.App.Composition;

/// <summary>Clipboard, OneNote launch and file copy for the success and failure views.</summary>
public sealed class WpfShellService : IShellService
{
    public void CopyToClipboard(string text) => Clipboard.SetText(text);

    public void OpenInOneNote(string notebookPath)
    {
        List<string> failures = new();
        foreach ((string fileName, string arguments) in OneNoteLaunch.Commands(notebookPath))
        {
            try
            {
                using Process? process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
                if (process is not null || fileName.StartsWith("onenote:", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                failures.Add(fileName + " did not start");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                failures.Add(fileName + ": " + ex.Message);
            }
        }

        throw new InvalidOperationException("OneNote desktop could not be started (" + string.Join("; ", failures) + "). Open the notebook folder from OneNote's File, Open menu instead.");
    }

    public void CopyFile(string sourcePath, string destinationPath) => File.Copy(sourcePath, destinationPath, overwrite: true);
}
