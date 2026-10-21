using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Sfp.OneNote.Com;

/// <summary>Resolves OneNote.Application by ProgID and creates it late-bound. Windows only.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ProgIdActivator : IOneNoteActivator
{
    public const string ProgId = "OneNote.Application";

    public ActivatedApp Activate()
    {
        Type? type = Type.GetTypeFromProgID(ProgId, throwOnError: false);
        if (type is null)
        {
            throw new OneNoteUnavailableException(
                OneNoteHResults.ActivateCall,
                OneNoteHResults.ClassNotRegistered,
                OneNoteUnavailableException.UserMessage + " The ProgID " + ProgId + " is not registered.",
                null);
        }

        object? created;
        try
        {
            created = Activator.CreateInstance(type);
        }
        catch (Exception ex) when (OneNoteHResults.IsActivationFailure(ex))
        {
            // COMException for most HRESULTs, but the runtime raises E_ACCESSDENIED as UnauthorizedAccessException and
            // ERROR_FILE_NOT_FOUND as FileNotFoundException; all of them are OneNote not starting (exit 3).
            throw OneNoteHResults.MapActivation(ex);
        }

        object target = created ?? throw new OneNoteUnavailableException(
            OneNoteHResults.ActivateCall, OneNoteHResults.ServerExecFailed, OneNoteUnavailableException.NotStartedMessage + " CreateInstance returned nothing.", null);
        return new ActivatedApp(target, ReadVersion());
    }

    /// <summary>
    /// The OneNote.Application COM interface has no version member, so the version comes from onenote.exe:
    /// the App Paths registry key names the executable and its file version is the product version.
    /// </summary>
    private static string ReadVersion()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\onenote.exe");
            if (key?.GetValue(null) is string path && File.Exists(path))
            {
                string? version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The version is informational only; a locked registry or file must not stop the run.
        }

        return "unknown";
    }
}
