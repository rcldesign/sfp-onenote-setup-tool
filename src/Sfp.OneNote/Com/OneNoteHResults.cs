using System.Globalization;
using System.Runtime.InteropServices;

namespace Sfp.OneNote.Com;

/// <summary>
/// HRESULT values the wrapper understands and the rule that maps a COM failure to a typed exception.
/// The 0x8004xxxx values are the OneNote 2013 developer reference's error codes; the others are standard COM.
/// </summary>
public static class OneNoteHResults
{
    // Standard COM: the two "try again" results and the "no server" family.
    public const int RpcCallRejected = unchecked((int)0x80010001);
    public const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
    public const int ClassNotRegistered = unchecked((int)0x80040154);
    public const int ServerExecFailed = unchecked((int)0x80080005);
    public const int RpcServerUnavailable = unchecked((int)0x800706BA);
    public const int RpcDisconnected = unchecked((int)0x80010108);
    public const int ObjectNotConnected = unchecked((int)0x800401FD);
    public const int MonikerUnavailable = unchecked((int)0x800401E3);

    // OneNote: XML and content insertion.
    public const int MalformedXml = unchecked((int)0x80042000);
    public const int InvalidXml = unchecked((int)0x80042001);
    public const int CreatingSection = unchecked((int)0x80042002);
    public const int OpeningSection = unchecked((int)0x80042003);
    public const int SectionDoesNotExist = unchecked((int)0x80042004);
    public const int PageDoesNotExist = unchecked((int)0x80042005);
    public const int FileDoesNotExist = unchecked((int)0x80042006);
    public const int InsertingImage = unchecked((int)0x80042007);
    public const int InsertingInk = unchecked((int)0x80042008);
    public const int InsertingHtml = unchecked((int)0x80042009);
    public const int NavigatingToPage = unchecked((int)0x8004200A);
    public const int SectionReadOnly = unchecked((int)0x8004200B);
    public const int PageReadOnly = unchecked((int)0x8004200C);
    public const int InsertingOutlineText = unchecked((int)0x8004200D);
    public const int PageObjectDoesNotExist = unchecked((int)0x8004200E);
    public const int BinaryObjectDoesNotExist = unchecked((int)0x8004200F);
    public const int LastModifiedDateDidNotMatch = unchecked((int)0x80042010);
    public const int GroupDoesNotExist = unchecked((int)0x80042011);
    public const int PageDoesNotExistInGroup = unchecked((int)0x80042012);
    public const int NoActiveSelection = unchecked((int)0x80042013);
    public const int NoHyperlinkToObject = unchecked((int)0x80042014);
    public const int ObjectDoesNotExist = unchecked((int)0x80042015);
    public const int NotebookDoesNotExist = unchecked((int)0x80042016);
    public const int FileAlreadyExists = unchecked((int)0x8004201A);
    public const int SectionEncryptedAndLocked = unchecked((int)0x8004201B);
    public const int DisabledByPolicy = unchecked((int)0x8004201C);
    public const int NotYetSynchronized = unchecked((int)0x8004201D);
    public const int LegacySection = unchecked((int)0x8004201E);
    public const int MergeFailed = unchecked((int)0x8004201F);

    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [RpcCallRejected] = "RPC_E_CALL_REJECTED",
        [RpcServerCallRetryLater] = "RPC_E_SERVERCALL_RETRYLATER",
        [ClassNotRegistered] = "REGDB_E_CLASSNOTREG",
        [ServerExecFailed] = "CO_E_SERVER_EXEC_FAILURE",
        [RpcServerUnavailable] = "RPC_S_SERVER_UNAVAILABLE",
        [RpcDisconnected] = "RPC_E_DISCONNECTED",
        [ObjectNotConnected] = "CO_E_OBJNOTCONNECTED",
        [MonikerUnavailable] = "MK_E_UNAVAILABLE",
        [MalformedXml] = "hrMalformedXML",
        [InvalidXml] = "hrInvalidXML",
        [CreatingSection] = "hrCreatingSection",
        [OpeningSection] = "hrOpeningSection",
        [SectionDoesNotExist] = "hrSectionDoesNotExist",
        [PageDoesNotExist] = "hrPageDoesNotExist",
        [FileDoesNotExist] = "hrFileDoesNotExist",
        [InsertingImage] = "hrInsertingImage",
        [InsertingInk] = "hrInsertingInk",
        [InsertingHtml] = "hrInsertingHtml",
        [NavigatingToPage] = "hrNavigatingToPage",
        [SectionReadOnly] = "hrSectionReadOnly",
        [PageReadOnly] = "hrPageReadOnly",
        [InsertingOutlineText] = "hrInsertingOutlineText",
        [PageObjectDoesNotExist] = "hrPageObjectDoesNotExist",
        [BinaryObjectDoesNotExist] = "hrBinaryObjectDoesNotExist",
        [LastModifiedDateDidNotMatch] = "hrLastModifiedDateDidNotMatch",
        [GroupDoesNotExist] = "hrGroupDoesNotExist",
        [PageDoesNotExistInGroup] = "hrPageDoesNotExistInGroup",
        [NoActiveSelection] = "hrNoActiveSelection",
        [NoHyperlinkToObject] = "hrNoHyperlinkToObject",
        [ObjectDoesNotExist] = "hrObjectDoesNotExist",
        [NotebookDoesNotExist] = "hrNotebookDoesNotExist",
        [FileAlreadyExists] = "hrFileAlreadyExists",
        [SectionEncryptedAndLocked] = "hrSectionEncryptedAndLocked",
        [DisabledByPolicy] = "hrDisabledByPolicy",
        [NotYetSynchronized] = "hrNotYetSynchronized",
        [LegacySection] = "hrLegacySection",
        [MergeFailed] = "hrMergeFailed",
    };

    /// <summary>True for the two results that mean "OneNote is busy, call again": the wrapper retries once after a delay.</summary>
    public static bool IsBusy(int hresult) => hresult is RpcCallRejected or RpcServerCallRetryLater;

    /// <summary>True when the failure means OneNote desktop is not installed, not registered or not running.</summary>
    public static bool IsUnavailable(int hresult) =>
        hresult is ClassNotRegistered or ServerExecFailed or RpcServerUnavailable or RpcDisconnected or ObjectNotConnected or MonikerUnavailable;

    /// <summary>True when OneNote rejected the XML or could not insert the content it described.</summary>
    public static bool IsXmlRejected(int hresult) =>
        hresult is MalformedXml or InvalidXml or InsertingImage or InsertingInk or InsertingHtml or InsertingOutlineText;

    /// <summary>"0x80042001".</summary>
    public static string Hex(int hresult) => "0x" + hresult.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>"hrInvalidXML (0x80042001)" or just the hex when the value is not in the table.</summary>
    public static string Describe(int hresult) =>
        Names.TryGetValue(hresult, out string? name) ? name + " (" + Hex(hresult) + ")" : Hex(hresult);

    /// <summary>
    /// Maps a COM failure of <paramref name="call"/> to the typed exception the orchestrator handles. The busy results
    /// are not mapped here (the caller retries them and raises <see cref="OneNoteBusyException"/> itself).
    /// </summary>
    public static OneNoteException Map(string call, COMException error, string? xml)
    {
        int hr = error.HResult;
        string detail = string.IsNullOrWhiteSpace(error.Message) ? "" : ": " + error.Message.Trim();
        if (IsUnavailable(hr))
        {
            return new OneNoteUnavailableException(
                call,
                hr,
                string.Format(CultureInfo.InvariantCulture, "{0} {1} failed with {2}{3}", OneNoteUnavailableException.UserMessage, call, Describe(hr), detail),
                error);
        }

        if (IsXmlRejected(hr))
        {
            return new OneNoteXmlRejectedException(
                call,
                hr,
                string.Format(CultureInfo.InvariantCulture, "OneNote rejected the XML passed to {0}: {1}{2}", call, Describe(hr), detail),
                xml,
                error);
        }

        return new OneNoteException(
            call,
            hr,
            string.Format(CultureInfo.InvariantCulture, "OneNote call {0} failed: {1}{2}", call, Describe(hr), detail),
            error);
    }
}
