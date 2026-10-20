using System.Diagnostics;
using System.Security;
using System.Text;
using Serilog;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// STUB, to be verified on the owner's PC in M5.6: shows a Windows toast by running a short PowerShell script against the
/// WinRT ToastNotificationManager under PowerShell's own application id (an unpackaged exe has no registered AUMID of
/// its own). Off Windows it logs and does nothing. The script text is a pure function so the escaping is unit-tested.
/// </summary>
public sealed class WindowsToastNotifier : ISourceFailureNotifier
{
    /// <summary>Application id the toast is shown under: the AUMID of Windows PowerShell, which every Windows install registers.</summary>
    public const string PowerShellAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly ILogger logger;
    private readonly Func<string, bool> runScript;

    /// <param name="logger">Run log.</param>
    /// <param name="runScript">Test hook: receives the script text and returns whether it ran; null runs powershell.exe.</param>
    public WindowsToastNotifier(ILogger logger, Func<string, bool>? runScript = null)
    {
        this.logger = logger;
        this.runScript = runScript ?? RunPowerShell;
    }

    /// <summary>
    /// The PowerShell script that shows the toast. Title and message are XML-escaped, which also turns apostrophes into
    /// &amp;apos; so the single-quoted PowerShell string literal around the XML stays intact.
    /// </summary>
    public static string BuildScript(string title, string message)
    {
        string safeTitle = SecurityElement.Escape(title) ?? "";
        string safeMessage = SecurityElement.Escape(message) ?? "";
        StringBuilder sb = new();
        sb.AppendLine("[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null");
        sb.AppendLine("[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null");
        sb.AppendLine("$xml = New-Object Windows.Data.Xml.Dom.XmlDocument");
        sb.Append("$xml.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>")
          .Append(safeTitle)
          .Append("</text><text>")
          .Append(safeMessage)
          .AppendLine("</text></binding></visual></toast>')");
        sb.AppendLine("$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)");
        sb.Append("[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('")
          .Append(PowerShellAppId)
          .AppendLine("').Show($toast)");
        return sb.ToString();
    }

    /// <inheritdoc />
    public void Notify(string title, string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.Information("Toast suppressed off Windows: {Title}: {Message}", title, message);
            return;
        }

        try
        {
            bool shown = runScript(BuildScript(title, message));
            logger.Information("Toast {Outcome}: {Title}", shown ? "shown" : "not shown", title);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or TimeoutException)
        {
            logger.Warning(ex, "Toast could not be shown: {Title}", title);
        }
    }

    private static bool RunPowerShell(string script)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        ProcessStartInfo psi = new()
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using Process? process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill();
            throw new TimeoutException("powershell.exe did not finish showing the toast in time.");
        }

        return process.ExitCode == 0;
    }
}
