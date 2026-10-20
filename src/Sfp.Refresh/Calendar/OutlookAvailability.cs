namespace Sfp.Refresh.Calendar;

/// <summary>Feature check for the optional Outlook COM reader: Windows and a resolvable Outlook.Application ProgID.</summary>
public static class OutlookAvailability
{
    public const string ProgId = "Outlook.Application";

    /// <summary>True only on Windows with Outlook desktop registered. Never throws.</summary>
    public static bool IsInstalled => OperatingSystem.IsWindows() && Type.GetTypeFromProgID(ProgId) is not null;
}
