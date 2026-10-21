using System.Diagnostics;
using System.Runtime.InteropServices;
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

        object target;
        try
        {
            target = Activator.CreateInstance(type) ?? throw new OneNoteUnavailableException(
                OneNoteHResults.ActivateCall, OneNoteHResults.ServerExecFailed, OneNoteUnavailableException.UserMessage + " CreateInstance returned nothing.", null);
        }
        catch (COMException ex)
        {
            throw OneNoteHResults.Map(OneNoteHResults.ActivateCall, ex, null);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is COMException inner)
        {
            throw OneNoteHResults.Map(OneNoteHResults.ActivateCall, inner, null);
        }

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
