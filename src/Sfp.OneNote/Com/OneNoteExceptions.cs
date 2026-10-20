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

/// <summary>OneNote desktop is not installed, not registered, or its COM server is not reachable.</summary>
public sealed class OneNoteUnavailableException : OneNoteException
{
    public const string UserMessage = "OneNote desktop (Office 2016 or later) is required. OneNote for Windows 10 is not supported.";

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
