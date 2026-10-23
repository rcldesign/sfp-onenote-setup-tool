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
/// OneNote desktop is not installed or not registered (the install message, <see cref="UserMessage"/>), is installed but
/// could not be started (<see cref="NotStartedMessage"/>), went away during the run (<see cref="DisconnectedMessage"/>),
/// or its OneNote.Application object has no automation members (<see cref="NoAutomationMessage"/>).
/// </summary>
public sealed class OneNoteUnavailableException : OneNoteException
{
    /// <summary>Where OneNote desktop can be installed (the PRD's link to install OneNote).</summary>
    public const string InstallUrl = "https://www.onenote.com/download";

    /// <summary>PRD error table, OneNote desktop not installed or only the UWP app: for activation and ProgID failures.</summary>
    public const string UserMessage = "OneNote desktop (Office 2016 or later) is required. OneNote for Windows 10 is not supported. Install OneNote desktop from " + InstallUrl + ".";

    /// <summary>OneNote was running when the run reached it, then its COM server went away (closed, crashed or hung).</summary>
    public const string DisconnectedMessage = "OneNote stopped responding or was closed during the run.";

    /// <summary>
    /// The OneNote.Application ProgID resolved, so OneNote is installed, but creating it failed: most often the tool and
    /// OneNote run at different elevations, or OneNote hung while starting.
    /// </summary>
    public const string NotStartedMessage = "OneNote is installed but could not be started. Close OneNote, run this tool and OneNote at the same elevation (both as administrator or neither), then retry.";

    /// <summary>
    /// The OneNote.Application object was created but has no member the wrapper calls (DISP_E_UNKNOWNNAME,
    /// DISP_E_MEMBERNOTFOUND, or the runtime binder or the COM binder finding no such member): the registration is not
    /// OneNote desktop's, or the installation is damaged.
    /// </summary>
    public const string NoAutomationMessage = "The OneNote installed here has no automation interface, so it is not OneNote desktop (Office 2016 or later), or its installation is damaged. OneNote for Windows 10 and the Store app cannot be automated. Install or repair OneNote desktop from " + InstallUrl + ".";

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

/// <summary>
/// OneNote refused one call twice in a row with a busy result (<see cref="OneNoteHResults.IsBusy"/>): RPC_E_CALL_REJECTED,
/// RPC_E_SERVERCALL_RETRYLATER, or hrAppInModalUI, whose message asks for the dialog to be closed.
/// </summary>
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
        : base(call, hresult, OneNoteHResults.BusyMessage(call, hresult), innerException)
    {
    }
}
