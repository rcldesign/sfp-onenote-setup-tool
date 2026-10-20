using System.Globalization;

namespace Sfp.OneNote.Com;

/// <summary>A OneNote COM call failed. <see cref="Exception.HResult"/> holds the COM HRESULT; <see cref="Call"/> names the call.</summary>
public class OneNoteException : Exception
{
    public OneNoteException()
    {
    }

    public OneNoteException(string message)
        : base(message)
    {
    }

    public OneNoteException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public OneNoteException(string call, int hresult, string message, Exception? innerException)
        : base(message, innerException)
    {
        Call = call;
        HResult = hresult;
    }

    /// <summary>The wrapper call that failed ("UpdatePageContent"), or null.</summary>
    public string? Call { get; }

    /// <summary>The HRESULT as "0x80042001".</summary>
    public string HResultHex => OneNoteHResults.Hex(HResult);
}

/// <summary>
/// OneNote desktop is not installed, not registered, or its COM server could not be reached: at activation (the
/// install message, <see cref="UserMessage"/>) or because it went away during the run (<see cref="DisconnectedMessage"/>).
/// </summary>
public sealed class OneNoteUnavailableException : OneNoteException
{
    /// <summary>Where OneNote desktop can be installed (the PRD's link to install OneNote).</summary>
    public const string InstallUrl = "https://www.onenote.com/download";

    /// <summary>PRD error table, OneNote desktop not installed or only the UWP app: for activation and ProgID failures.</summary>
    public const string UserMessage = "OneNote desktop (Office 2016 or later) is required. OneNote for Windows 10 is not supported. Install OneNote desktop from " + InstallUrl + ".";

    /// <summary>OneNote was running when the run reached it, then its COM server went away (closed, crashed or hung).</summary>
    public const string DisconnectedMessage = "OneNote stopped responding or was closed during the run.";

    public OneNoteUnavailableException()
        : base(UserMessage)
    {
    }

    public OneNoteUnavailableException(string message)
        : base(message)
    {
    }

    public OneNoteUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public OneNoteUnavailableException(string call, int hresult, string message, Exception? innerException)
        : base(call, hresult, message, innerException)
    {
    }
}

/// <summary>UpdatePageContent or UpdateHierarchy rejected the XML. <see cref="Xml"/> is kept so the orchestrator can save it to the log.</summary>
public sealed class OneNoteXmlRejectedException : OneNoteException
{
    public OneNoteXmlRejectedException()
    {
    }

    public OneNoteXmlRejectedException(string message)
        : base(message)
    {
    }

    public OneNoteXmlRejectedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public OneNoteXmlRejectedException(string call, int hresult, string message, string? xml, Exception? innerException)
        : base(call, hresult, message, innerException)
    {
        Xml = xml;
    }

    /// <summary>The XML OneNote rejected, or null when the call carried none.</summary>
    public string? Xml { get; }
}

/// <summary>OneNote reported RPC_E_CALL_REJECTED or RPC_E_SERVERCALL_RETRYLATER twice in a row for one call.</summary>
public sealed class OneNoteBusyException : OneNoteException
{
    public OneNoteBusyException()
    {
    }

    public OneNoteBusyException(string message)
        : base(message)
    {
    }

    public OneNoteBusyException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public OneNoteBusyException(string call, int hresult, Exception? innerException)
        : base(call, hresult, string.Format(CultureInfo.InvariantCulture, "OneNote is busy and rejected {0} twice ({1}).", call, OneNoteHResults.Describe(hresult)), innerException)
    {
    }
}
