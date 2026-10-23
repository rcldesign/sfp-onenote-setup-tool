using System.Globalization;
using System.Text.RegularExpressions;

namespace Sfp.OneNote.Com;

/// <summary>
/// HRESULT values the wrapper understands and the rule that maps a COM failure to a typed exception.
/// The 0x8004xxxx values are Microsoft's published table, "Error codes (OneNote)" in the OneNote developer reference
/// (learn.microsoft.com/office/client-developer/onenote/error-codes-onenote), every row with its published name; the
/// others are standard COM.
/// </summary>
public static partial class OneNoteHResults
{
    // Standard COM: the two "try again" results and the "no server" family.
    public const int RpcCallRejected = unchecked((int)0x80010001);
    public const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
    public const int ClassNotRegistered = unchecked((int)0x80040154);
    public const int ServerExecFailed = unchecked((int)0x80080005);
    public const int RpcServerUnavailable = unchecked((int)0x800706BA);
    public const int RpcCallFailed = unchecked((int)0x800706BE);
    public const int RpcDisconnected = unchecked((int)0x80010108);
    public const int ObjectNotConnected = unchecked((int)0x800401FD);
    public const int MonikerUnavailable = unchecked((int)0x800401E3);

    /// <summary>E_OUTOFMEMORY, which the runtime raises as OutOfMemoryException; from activation it is the server's (<see cref="IsActivationFailure"/>).</summary>
    public const int OutOfMemory = unchecked((int)0x8007000E);

    /// <summary>
    /// E_FAIL, an unspecified failure. The owner's OneNote answered its first call with it; a type library it cannot load
    /// is the suspected cause (<see cref="OneNoteTypeLibrary"/>). <see cref="Map"/> leaves it a plain
    /// <see cref="OneNoteException"/>; the Generate preflight words it for the run's first call, after OneNote started.
    /// </summary>
    public const int Fail = unchecked((int)0x80004005);

    /// <summary>IDispatch: the object does not know the name (from GetIDsOfNames). See <see cref="IsNoAutomation"/>.</summary>
    public const int DispUnknownName = unchecked((int)0x80020006);

    /// <summary>IDispatch: the member does not exist (from Invoke). See <see cref="IsNoAutomation"/>.</summary>
    public const int DispMemberNotFound = unchecked((int)0x80020003);

    /// <summary>TYPE_E_LIBNOTREGISTERED: the type library is not registered (<see cref="IsTypeLibraryFailure"/>). Raised as a COMException.</summary>
    public const int TypeLibNotRegistered = unchecked((int)0x8002801D);

    /// <summary>
    /// TYPE_E_CANTLOADLIBRARY: the type library, or the file said to hold it, could not be loaded
    /// (<see cref="IsTypeLibraryFailure"/>). Raised as a COMException.
    /// </summary>
    public const int CantLoadTypeLibrary = unchecked((int)0x80029C4A);

    // OneNote: Microsoft's "Error codes (OneNote)" table, in its order (there is no 0x80042021).
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
    public const int ObjectDoesNotExist = unchecked((int)0x80042014);
    public const int NotebookDoesNotExist = unchecked((int)0x80042015);
    public const int InsertingFile = unchecked((int)0x80042016);
    public const int InvalidName = unchecked((int)0x80042017);
    public const int FolderDoesNotExist = unchecked((int)0x80042018);
    public const int InvalidQuery = unchecked((int)0x80042019);
    public const int FileAlreadyExists = unchecked((int)0x8004201A);
    public const int SectionEncryptedAndLocked = unchecked((int)0x8004201B);
    public const int DisabledByPolicy = unchecked((int)0x8004201C);
    public const int NotYetSynchronized = unchecked((int)0x8004201D);
    public const int LegacySection = unchecked((int)0x8004201E);
    public const int MergeFailed = unchecked((int)0x8004201F);
    public const int InvalidXmlSchema = unchecked((int)0x80042020);
    public const int FutureContentLoss = unchecked((int)0x80042022);
    public const int TimeOut = unchecked((int)0x80042023);
    public const int RecordingInProgress = unchecked((int)0x80042024);
    public const int UnknownLinkedNoteState = unchecked((int)0x80042025);
    public const int NoShortNameForLinkedNote = unchecked((int)0x80042026);
    public const int NoFriendlyNameForLinkedNote = unchecked((int)0x80042027);
    public const int InvalidLinkedNoteUri = unchecked((int)0x80042028);
    public const int InvalidLinkedNoteThumbnail = unchecked((int)0x80042029);
    public const int ImportLntThumbnailFailed = unchecked((int)0x8004202A);
    public const int UnreadDisabledForNotebook = unchecked((int)0x8004202B);
    public const int InvalidSelection = unchecked((int)0x8004202C);
    public const int ConvertFailed = unchecked((int)0x8004202D);
    public const int RecycleBinEditFailed = unchecked((int)0x8004202E);
    public const int IMConversationTypeInvalid = unchecked((int)0x8004202F);

    /// <summary>"A modal dialog is blocking the app": retried like busy (<see cref="IsBusy"/>), then reported as a dialog to close.</summary>
    public const int AppInModalUI = unchecked((int)0x80042030);
    public const int PublishFormatUnsupportedForLabels = unchecked((int)0x80042031);

    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [RpcCallRejected] = "RPC_E_CALL_REJECTED",
        [RpcServerCallRetryLater] = "RPC_E_SERVERCALL_RETRYLATER",
        [ClassNotRegistered] = "REGDB_E_CLASSNOTREG",
        [ServerExecFailed] = "CO_E_SERVER_EXEC_FAILURE",
        [RpcServerUnavailable] = "RPC_S_SERVER_UNAVAILABLE",
        [RpcCallFailed] = "RPC_S_CALL_FAILED",
        [RpcDisconnected] = "RPC_E_DISCONNECTED",
        [ObjectNotConnected] = "CO_E_OBJNOTCONNECTED",
        [MonikerUnavailable] = "MK_E_UNAVAILABLE",
        [OutOfMemory] = "E_OUTOFMEMORY",
        [Fail] = "E_FAIL",
        [DispUnknownName] = "DISP_E_UNKNOWNNAME",
        [DispMemberNotFound] = "DISP_E_MEMBERNOTFOUND",
        [TypeLibNotRegistered] = "TYPE_E_LIBNOTREGISTERED",
        [CantLoadTypeLibrary] = "TYPE_E_CANTLOADLIBRARY",
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
        [ObjectDoesNotExist] = "hrObjectDoesNotExist",
        [NotebookDoesNotExist] = "hrNotebookDoesNotExist",
        [InsertingFile] = "hrInsertingFile",
        [InvalidName] = "hrInvalidName",
        [FolderDoesNotExist] = "hrFolderDoesNotExist",
        [InvalidQuery] = "hrInvalidQuery",
        [FileAlreadyExists] = "hrFileAlreadyExists",
        [SectionEncryptedAndLocked] = "hrSectionEncryptedAndLocked",
        [DisabledByPolicy] = "hrDisabledByPolicy",
        [NotYetSynchronized] = "hrNotYetSynchronized",
        [LegacySection] = "hrLegacySection",
        [MergeFailed] = "hrMergeFailed",
        [InvalidXmlSchema] = "hrInvalidXMLSchema",
        [FutureContentLoss] = "hrFutureContentLoss",
        [TimeOut] = "hrTimeOut",
        [RecordingInProgress] = "hrRecordingInProgress",
        [UnknownLinkedNoteState] = "hrUnknownLinkedNoteState",
        [NoShortNameForLinkedNote] = "hrNoShortNameForLinkedNote",
        [NoFriendlyNameForLinkedNote] = "hrNoFriendlyNameForLinkedNote",
        [InvalidLinkedNoteUri] = "hrInvalidLinkedNoteUri",
        [InvalidLinkedNoteThumbnail] = "hrInvalidLinkedNoteThumbnail",
        [ImportLntThumbnailFailed] = "hrImportLNTThumbnailFailed",
        [UnreadDisabledForNotebook] = "hrUnreadDisabledForNotebook",
        [InvalidSelection] = "hrInvalidSelection",
        [ConvertFailed] = "hrConvertFailed",
        [RecycleBinEditFailed] = "hrRecycleBinEditFailed",
        [IMConversationTypeInvalid] = "hrIMConversationTypeInvalid",
        [AppInModalUI] = "hrAppInModalUI",
        [PublishFormatUnsupportedForLabels] = "hrPublishFormatUnsupportedForLabels",
    };

    /// <summary>The call name of creating the OneNote.Application object, as the log and the exceptions carry it.</summary>
    public const string ActivateCall = "activate";

    /// <summary>
    /// True for the results that mean "OneNote cannot take the call now, call again": the two COM busy results and
    /// hrAppInModalUI (a dialog in OneNote blocks it). The wrapper retries once after a delay.
    /// </summary>
    public static bool IsBusy(int hresult) => hresult is RpcCallRejected or RpcServerCallRetryLater or AppInModalUI;

    /// <summary>
    /// The message of a call still refused after the retry: a dialog to close for hrAppInModalUI, else "busy". Used by
    /// <see cref="OneNoteBusyException"/>.
    /// </summary>
    public static string BusyMessage(string call, int hresult) => hresult == AppInModalUI
        ? string.Format(CultureInfo.InvariantCulture, "OneNote is showing a dialog and refused {0} twice ({1}). Close the dialog OneNote is showing, then retry.", call, Describe(hresult))
        : string.Format(CultureInfo.InvariantCulture, "OneNote is busy and rejected {0} twice ({1}).", call, Describe(hresult));

    /// <summary>True when the failure means OneNote desktop is not installed, not registered or not running.</summary>
    public static bool IsUnavailable(int hresult) =>
        hresult is ClassNotRegistered or ServerExecFailed or RpcServerUnavailable or RpcCallFailed or RpcDisconnected or ObjectNotConnected or MonikerUnavailable;

    /// <summary>
    /// True for the unavailable results that mean the COM server went away after the object existed (the RPC server is
    /// gone, the call failed because OneNote crashed while it was in flight, or the object disconnected from its
    /// clients), rather than that it could not be created.
    /// </summary>
    public static bool IsDisconnected(int hresult) => hresult is RpcServerUnavailable or RpcCallFailed or RpcDisconnected or ObjectNotConnected;

    /// <summary>
    /// True when the IDispatch server has no such member: DISP_E_UNKNOWNNAME (the name is unknown) or
    /// DISP_E_MEMBERNOTFOUND (the member does not exist). OneNote desktop's OneNote.Application object has every member
    /// the wrapper calls, so the object is not OneNote desktop's or its installation is damaged
    /// (<see cref="OneNoteUnavailableException.NoAutomationMessage"/>).
    /// </summary>
    public static bool IsNoAutomation(int hresult) => hresult is DispUnknownName or DispMemberNotFound;

    /// <summary>
    /// True when the type library could not be found or loaded: TYPE_E_LIBNOTREGISTERED or TYPE_E_CANTLOADLIBRARY.
    /// <see cref="Map"/> leaves them plain <see cref="OneNoteException"/>s; the Generate preflight words them, as it words
    /// E_FAIL, when the run's first call fails after OneNote started (<see cref="OneNoteTypeLibrary"/>).
    /// </summary>
    public static bool IsTypeLibraryFailure(int hresult) => hresult is TypeLibNotRegistered or CantLoadTypeLibrary;

    /// <summary>True when OneNote rejected the XML or could not insert the content it described.</summary>
    public static bool IsXmlRejected(int hresult) =>
        hresult is MalformedXml or InvalidXml or InsertingImage or InsertingInk or InsertingHtml or InsertingOutlineText;

    /// <summary>"0x80042001".</summary>
    public static string Hex(int hresult) => "0x" + hresult.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>"hrInvalidXML (0x80042001)" or just the hex when the value is not in the table.</summary>
    public static string Describe(int hresult) =>
        Names.TryGetValue(hresult, out string? name) ? name + " (" + Hex(hresult) + ")" : Hex(hresult);

    /// <summary>
    /// True when an exception thrown while creating the OneNote.Application object is an activation failure, which
    /// <see cref="MapActivation"/> reports as OneNote not starting: anything but one the activator already mapped
    /// (<see cref="OneNoteException"/>) or a fatal runtime failure. The runtime throws most activation HRESULTs as
    /// COMException, but some as the .NET exception it maps them to, carrying the HRESULT: E_ACCESSDENIED (0x80070005, a
    /// DCOM launch permission) as UnauthorizedAccessException, ERROR_FILE_NOT_FOUND (0x80070002, a registration whose
    /// server is gone) as FileNotFoundException, and E_OUTOFMEMORY (0x8007000E, the server could not be started, as on an
    /// exhausted desktop heap) as OutOfMemoryException. An OutOfMemoryException that carries E_OUTOFMEMORY is therefore an
    /// activation failure (a real managed out-of-memory from that one call is implausible); any other one
    /// (InsufficientMemoryException, for one) stays fatal and is not caught. A reflection wrapper is judged by what it wraps.
    /// </summary>
    public static bool IsActivationFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Unwrap(error) switch
        {
            OneNoteException => false,
            OutOfMemoryException outOfMemory => outOfMemory.HResult == OutOfMemory,
            _ => true,
        };
    }

    /// <summary>
    /// Maps an activation failure (<see cref="IsActivationFailure"/>), whatever exception type the runtime raised it as,
    /// to the not-started exception with its HRESULT, as <see cref="Map"/> does for <see cref="ActivateCall"/>; a
    /// reflection wrapper is unwrapped first.
    /// </summary>
    public static OneNoteException MapActivation(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Map(ActivateCall, Unwrap(error), null);
    }

    /// <summary>
    /// Maps a failure of <paramref name="call"/> (a COMException; for activation, whatever exception the runtime raised
    /// for the HRESULT) to the typed exception the orchestrator handles, by the exception's HResult. The busy results
    /// are not mapped here (the caller retries them and raises <see cref="OneNoteBusyException"/> itself). Any failure of
    /// activation (<see cref="ActivateCall"/>) opens with <see cref="OneNoteUnavailableException.NotStartedMessage"/>: the
    /// activator resolves the ProgID first and keeps the install message for that lookup, so OneNote is installed here.
    /// A later DISP_E_UNKNOWNNAME or DISP_E_MEMBERNOTFOUND (<see cref="IsNoAutomation"/>) opens with
    /// <see cref="OneNoteUnavailableException.NoAutomationMessage"/>. A later unavailable result opens with
    /// <see cref="OneNoteUnavailableException.DisconnectedMessage"/> when OneNote went away (<see cref="IsDisconnected"/>),
    /// else with the install message (a missing registration).
    /// </summary>
    public static OneNoteException Map(string call, Exception error, string? xml)
    {
        ArgumentNullException.ThrowIfNull(error);
        return MapResult(call, error.HResult, error, xml);
    }

    /// <summary>
    /// Maps the binder's refusal of the late-bound call <paramref name="call"/>, which reaches the session as a binder
    /// exception instead of a COMException, keeping <paramref name="error"/> as the inner exception. The runtime binder's
    /// RuntimeBinderException, raised when IDispatch's GetIDsOfNames answers DISP_E_UNKNOWNNAME ("'System.__ComObject'
    /// does not contain a definition for ..."), is the no-automation exception under DISP_E_UNKNOWNNAME. The COM binder's
    /// MissingMemberException is read by its message: "Could not get dispatch ID for {name} (error: 0x80004005)." names
    /// the HRESULT GetIDsOfNames answered (<see cref="DispatchIdResult"/>), which is mapped as <see cref="Map"/> maps that
    /// HRESULT from the call itself (E_FAIL stays a plain <see cref="OneNoteException"/>, a disconnect reads as one, and
    /// <paramref name="xml"/> is carried as there); "Error while invoking {name}." (DISP_E_MEMBERNOTFOUND from Invoke)
    /// names none and is the no-automation exception under DISP_E_MEMBERNOTFOUND. The busy results are the caller's to
    /// retry, as for <see cref="Map"/>.
    /// </summary>
    public static OneNoteException MapMissingMember(string call, Exception error, string? xml = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error is MissingMemberException)
        {
            return DispatchIdResult(error) is int hresult
                ? MapResult(call, hresult, error, xml)
                : NoAutomation(call, DispMemberNotFound, error);
        }

        return NoAutomation(call, DispUnknownName, error);
    }

    /// <summary>
    /// The HRESULT the COM binder names in its MissingMemberException for a failed GetIDsOfNames ("Could not get dispatch
    /// ID for {name} (error: 0x80004005)."): the last "(error: 0x" plus eight hex digits plus ")" in the message, read
    /// without regard to case or culture. Null when the message has none, as "Error while invoking {name}." has not.
    /// </summary>
    public static int? DispatchIdResult(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Match? last = DispatchIdError().Matches(error.Message).LastOrDefault();
        return last is null ? null : int.Parse(last.Groups["hr"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    /// <summary><see cref="Map"/> for <paramref name="hr"/>, which a binder exception names instead of carrying it as its HResult.</summary>
    private static OneNoteException MapResult(string call, int hr, Exception error, string? xml)
    {
        if (string.Equals(call, ActivateCall, StringComparison.Ordinal))
        {
            return new OneNoteUnavailableException(
                call,
                hr,
                string.Format(CultureInfo.InvariantCulture, "{0} {1} failed with {2}{3}", OneNoteUnavailableException.NotStartedMessage, call, Describe(hr), Detail(error)),
                error);
        }

        if (IsNoAutomation(hr))
        {
            return NoAutomation(call, hr, error);
        }

        string detail = Detail(error);
        if (IsUnavailable(hr))
        {
            string lead = IsDisconnected(hr) ? OneNoteUnavailableException.DisconnectedMessage : OneNoteUnavailableException.UserMessage;
            return new OneNoteUnavailableException(
                call,
                hr,
                string.Format(CultureInfo.InvariantCulture, "{0} {1} failed with {2}{3}", lead, call, Describe(hr), detail),
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

    /// <summary>The no-automation exception for <paramref name="call"/>, with the HRESULT and the failure's own message.</summary>
    private static OneNoteUnavailableException NoAutomation(string call, int hresult, Exception error) =>
        new(
            call,
            hresult,
            string.Format(CultureInfo.InvariantCulture, "{0} {1} failed with {2}{3}", OneNoteUnavailableException.NoAutomationMessage, call, Describe(hresult), Detail(error)),
            error);

    /// <summary>": " and the exception's trimmed message, or nothing when it has none.</summary>
    private static string Detail(Exception error) =>
        string.IsNullOrWhiteSpace(error.Message) ? "" : ": " + error.Message.Trim();

    /// <summary>The exception a reflection wrapper (TargetInvocationException) carries, else the exception itself.</summary>
    private static Exception Unwrap(Exception error) =>
        error is System.Reflection.TargetInvocationException { InnerException: Exception inner } ? inner : error;

    [GeneratedRegex(@"\(error: 0x(?<hr>[0-9A-F]{8})\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DispatchIdError();
}
