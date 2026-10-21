using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Sfp.OneNote.Com;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Com;

/// <summary>The HRESULT table and the mapping rule on their own.</summary>
public class OneNoteHResultsTests
{
    /// <summary>
    /// Microsoft's "Error codes (OneNote)" table (learn.microsoft.com/office/client-developer/onenote/error-codes-onenote),
    /// every row: value and published name. There is no 0x80042021.
    /// </summary>
    private static readonly (uint Value, string Name)[] Published =
    {
        (0x80042000, "hrMalformedXML"),
        (0x80042001, "hrInvalidXML"),
        (0x80042002, "hrCreatingSection"),
        (0x80042003, "hrOpeningSection"),
        (0x80042004, "hrSectionDoesNotExist"),
        (0x80042005, "hrPageDoesNotExist"),
        (0x80042006, "hrFileDoesNotExist"),
        (0x80042007, "hrInsertingImage"),
        (0x80042008, "hrInsertingInk"),
        (0x80042009, "hrInsertingHtml"),
        (0x8004200A, "hrNavigatingToPage"),
        (0x8004200B, "hrSectionReadOnly"),
        (0x8004200C, "hrPageReadOnly"),
        (0x8004200D, "hrInsertingOutlineText"),
        (0x8004200E, "hrPageObjectDoesNotExist"),
        (0x8004200F, "hrBinaryObjectDoesNotExist"),
        (0x80042010, "hrLastModifiedDateDidNotMatch"),
        (0x80042011, "hrGroupDoesNotExist"),
        (0x80042012, "hrPageDoesNotExistInGroup"),
        (0x80042013, "hrNoActiveSelection"),
        (0x80042014, "hrObjectDoesNotExist"),
        (0x80042015, "hrNotebookDoesNotExist"),
        (0x80042016, "hrInsertingFile"),
        (0x80042017, "hrInvalidName"),
        (0x80042018, "hrFolderDoesNotExist"),
        (0x80042019, "hrInvalidQuery"),
        (0x8004201A, "hrFileAlreadyExists"),
        (0x8004201B, "hrSectionEncryptedAndLocked"),
        (0x8004201C, "hrDisabledByPolicy"),
        (0x8004201D, "hrNotYetSynchronized"),
        (0x8004201E, "hrLegacySection"),
        (0x8004201F, "hrMergeFailed"),
        (0x80042020, "hrInvalidXMLSchema"),
        (0x80042022, "hrFutureContentLoss"),
        (0x80042023, "hrTimeOut"),
        (0x80042024, "hrRecordingInProgress"),
        (0x80042025, "hrUnknownLinkedNoteState"),
        (0x80042026, "hrNoShortNameForLinkedNote"),
        (0x80042027, "hrNoFriendlyNameForLinkedNote"),
        (0x80042028, "hrInvalidLinkedNoteUri"),
        (0x80042029, "hrInvalidLinkedNoteThumbnail"),
        (0x8004202A, "hrImportLNTThumbnailFailed"),
        (0x8004202B, "hrUnreadDisabledForNotebook"),
        (0x8004202C, "hrInvalidSelection"),
        (0x8004202D, "hrConvertFailed"),
        (0x8004202E, "hrRecycleBinEditFailed"),
        (0x8004202F, "hrIMConversationTypeInvalid"),
        (0x80042030, "hrAppInModalUI"),
        (0x80042031, "hrPublishFormatUnsupportedForLabels"),
    };

    [Fact]
    public void Every_onenote_error_code_is_pinned_to_microsofts_published_table()
    {
        foreach ((uint value, string name) in Published)
        {
            OneNoteHResults.Describe(unchecked((int)value)).Should().Be(name + " (0x" + value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        // Every OneNote constant of the class is a published row, named as published without the "hr" prefix (any casing).
        List<(uint Value, string Name)> constants = typeof(OneNoteHResults)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (Value: unchecked((uint)(int)f.GetRawConstantValue()!), Name: ("hr" + f.Name).ToUpperInvariant()))
            .Where(c => c.Value is >= 0x80042000 and <= 0x800420FF)
            .ToList();
        constants.Should().BeEquivalentTo(Published.Select(p => (p.Value, Name: p.Name.ToUpperInvariant())));

        OneNoteHResults.ObjectDoesNotExist.Should().Be(unchecked((int)0x80042014));
        OneNoteHResults.NotebookDoesNotExist.Should().Be(unchecked((int)0x80042015));
        OneNoteHResults.InsertingFile.Should().Be(unchecked((int)0x80042016));
        OneNoteHResults.TimeOut.Should().Be(unchecked((int)0x80042023));
        OneNoteHResults.AppInModalUI.Should().Be(unchecked((int)0x80042030));
    }

    /// <summary>
    /// onenote-3: the M0 spike's Python copy of the table (tools/spike/spike_common.py, the scripts the owner runs on the
    /// PC) is checked against the same published rows, so the two copies cannot drift again: every OneNote row of its
    /// ONENOTE_ERRORS in the published order and name, every other row the wrapper also names named alike, the DATE
    /// fallback on hrLastModifiedDateDidNotMatch, and the retry set equal to the wrapper's busy results.
    /// </summary>
    [Fact]
    public void The_spike_scripts_copy_of_the_table_matches_the_published_rows_and_the_wrapper()
    {
        string source = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tools", "spike", "spike_common.py"));
        Dictionary<string, uint> constants = Regex.Matches(source, @"^(?<name>[A-Z][A-Z0-9_]*) = 0x(?<hex>[0-9A-Fa-f]{8})\s*(#.*)?$", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups["name"].Value, m => uint.Parse(m.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), StringComparer.Ordinal);
        uint Value(string key) => key.StartsWith("0x", StringComparison.Ordinal)
            ? uint.Parse(key[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : constants.Should().ContainKey(key, "a symbolic key of the table is a hex constant of the script").WhoseValue;

        Match table = Regex.Match(source, @"^ONENOTE_ERRORS = \{\r?\n(?<body>.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        table.Success.Should().BeTrue("spike_common.py defines ONENOTE_ERRORS");
        List<(uint Value, string Name)> rows = table.Groups["body"].Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line =>
            {
                Match row = Regex.Match(line, @"^(?<key>0x[0-9A-Fa-f]{8}|[A-Z][A-Z0-9_]*): ""(?<name>[A-Za-z0-9_]+)"",$");
                row.Success.Should().BeTrue("every line of the table is a 'key: \"name\",' row, which '" + line + "' is not");
                return (Value(row.Groups["key"].Value), row.Groups["name"].Value);
            })
            .ToList();

        rows.Where(r => r.Value is >= 0x80042000 and <= 0x800420FF).Should().Equal(Published, "the spike's OneNote rows are Microsoft's table, every row, in its order");
        foreach ((uint value, string name) in rows)
        {
            string hex = "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
            string described = OneNoteHResults.Describe(unchecked((int)value));
            if (!string.Equals(described, hex, StringComparison.Ordinal))
            {
                described.Should().Be(name + " (" + hex + ")", "a row the wrapper also names carries the wrapper's name");
            }
        }

        constants.Should().ContainKey("HR_LAST_MODIFIED_DATE_DID_NOT_MATCH")
            .WhoseValue.Should().Be(unchecked((uint)OneNoteHResults.LastModifiedDateDidNotMatch), "the DATE fallback fires on hrLastModifiedDateDidNotMatch");
        Match retry = Regex.Match(source, @"^RETRY_CODES = \{(?<members>[^}]*)\}", RegexOptions.Multiline);
        retry.Success.Should().BeTrue("spike_common.py defines RETRY_CODES");
        List<uint> retried = retry.Groups["members"].Value.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).Select(Value).ToList();
        retried.Should().BeEquivalentTo(
            new[] { OneNoteHResults.RpcCallRejected, OneNoteHResults.RpcServerCallRetryLater, OneNoteHResults.AppInModalUI }.Select(hr => unchecked((uint)hr)),
            "the spike retries once what the wrapper retries once");
        retried.Should().OnlyContain(hr => OneNoteHResults.IsBusy(unchecked((int)hr)));
    }

    [Theory]
    [InlineData(unchecked((int)0x80010001), true)]
    [InlineData(unchecked((int)0x8001010A), true)]
    [InlineData(unchecked((int)0x80042030), true)]
    [InlineData(unchecked((int)0x80042001), false)]
    [InlineData(unchecked((int)0x80042023), false)]
    [InlineData(unchecked((int)0x80040154), false)]
    public void Busy_results_are_call_rejected_retry_later_and_a_modal_dialog(int hresult, bool busy)
    {
        OneNoteHResults.IsBusy(hresult).Should().Be(busy);
    }

    [Fact]
    public void A_modal_dialog_that_persists_asks_for_the_dialog_to_be_closed()
    {
        OneNoteBusyException ex = new("UpdatePageContent", OneNoteHResults.AppInModalUI, null);

        ex.Message.Should().Be("OneNote is showing a dialog and refused UpdatePageContent twice (hrAppInModalUI (0x80042030)). Close the dialog OneNote is showing, then retry.");
        ex.HResult.Should().Be(OneNoteHResults.AppInModalUI);
        OneNoteHResults.IsUnavailable(OneNoteHResults.AppInModalUI).Should().BeFalse("OneNote is running; it is waiting for the user");
    }

    [Theory]
    [InlineData(unchecked((int)0x80040154), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x80080005), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x800706BA), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x80010108), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x800706BE), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x80042000), typeof(OneNoteXmlRejectedException))]
    [InlineData(unchecked((int)0x80042001), typeof(OneNoteXmlRejectedException))]
    [InlineData(unchecked((int)0x80042007), typeof(OneNoteXmlRejectedException))]
    [InlineData(unchecked((int)0x8004200D), typeof(OneNoteXmlRejectedException))]
    [InlineData(unchecked((int)0x80042005), typeof(OneNoteException))]
    [InlineData(unchecked((int)0x80042010), typeof(OneNoteException))]
    [InlineData(unchecked((int)0x80004005), typeof(OneNoteException))]
    public void Com_failures_map_to_the_typed_exception_for_their_hresult(int hresult, Type expected)
    {
        OneNoteException mapped = OneNoteHResults.Map("UpdatePageContent", new COMException("boom", hresult), "<xml/>");

        mapped.Should().BeOfType(expected);
        mapped.HResult.Should().Be(hresult);
        mapped.Call.Should().Be("UpdatePageContent");
        mapped.InnerException.Should().BeOfType<COMException>();
        mapped.Message.Should().Contain(OneNoteHResults.Hex(hresult));
        if (mapped is OneNoteXmlRejectedException rejected)
        {
            rejected.Xml.Should().Be("<xml/>");
        }
    }

    [Theory]
    [InlineData("UpdatePageContent", unchecked((int)0x800706BA))]
    [InlineData("UpdatePageContent", unchecked((int)0x80010108))]
    [InlineData("GetHierarchy", unchecked((int)0x800401FD))]
    [InlineData("OpenHierarchy", unchecked((int)0x80010108))]
    [InlineData("UpdatePageContent", unchecked((int)0x800706BE))]
    [InlineData("GetPageContent", unchecked((int)0x800706BE))]
    public void A_disconnect_after_activation_says_onenote_stopped_or_was_closed_not_that_it_is_missing(string call, int hresult)
    {
        OneNoteException mapped = OneNoteHResults.Map(call, new COMException("The object invoked has disconnected from its clients.", hresult), null);

        mapped.Should().BeOfType<OneNoteUnavailableException>("an unavailable OneNote still exits with 3");
        mapped.Message.Should().StartWith(OneNoteUnavailableException.DisconnectedMessage + " " + call + " failed with ");
        mapped.Message.Should().Contain(OneNoteHResults.Hex(hresult)).And.Contain("disconnected from its clients");
        mapped.Message.Should().NotContain("is required").And.NotContain(OneNoteUnavailableException.InstallUrl);
        OneNoteHResults.IsDisconnected(hresult).Should().BeTrue();
    }

    [Theory]
    [InlineData(unchecked((int)0x80080005))]
    [InlineData(unchecked((int)0x800706BA))]
    [InlineData(unchecked((int)0x80010108))]
    [InlineData(unchecked((int)0x800706BE))]
    [InlineData(unchecked((int)0x80040154))]
    [InlineData(unchecked((int)0x800401E3))]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x80004005))]
    public void An_activation_failure_says_onenote_is_installed_but_could_not_be_started_with_the_hresult(int hresult)
    {
        OneNoteException mapped = OneNoteHResults.Map(OneNoteHResults.ActivateCall, new COMException("boom", hresult), null);

        mapped.Should().BeOfType<OneNoteUnavailableException>("an OneNote that does not start still exits with 3");
        mapped.HResult.Should().Be(hresult);
        mapped.Message.Should().StartWith(OneNoteUnavailableException.NotStartedMessage + " activate failed with " + OneNoteHResults.Describe(hresult));
        mapped.Message.Should().NotContain(OneNoteUnavailableException.InstallUrl, "the ProgID resolved, so OneNote is installed");
        mapped.Message.Should().NotContain(OneNoteUnavailableException.DisconnectedMessage);
    }

    [Theory]
    [InlineData(unchecked((int)0x80040154))]
    [InlineData(unchecked((int)0x80080005))]
    public void Registration_failures_of_a_later_call_carry_the_install_message(int hresult)
    {
        OneNoteException mapped = OneNoteHResults.Map("GetHierarchy", new COMException("boom", hresult), null);

        mapped.Should().BeOfType<OneNoteUnavailableException>();
        mapped.Message.Should().StartWith(OneNoteUnavailableException.UserMessage + " GetHierarchy failed with " + OneNoteHResults.Describe(hresult));
        mapped.Message.Should().NotContain(OneNoteUnavailableException.DisconnectedMessage);
    }

    [Fact]
    public void The_install_message_is_the_prd_text_with_the_install_link()
    {
        OneNoteUnavailableException.UserMessage.Should().Be(
            "OneNote desktop (Office 2016 or later) is required. OneNote for Windows 10 is not supported. Install OneNote desktop from https://www.onenote.com/download.");
        OneNoteUnavailableException.InstallUrl.Should().Be("https://www.onenote.com/download");
        new OneNoteUnavailableException().Message.Should().Be(OneNoteUnavailableException.UserMessage);
        OneNoteUnavailableException.DisconnectedMessage.Should().Be("OneNote stopped responding or was closed during the run.");
        OneNoteUnavailableException.NotStartedMessage.Should().Be(
            "OneNote is installed but could not be started. Close OneNote, run this tool and OneNote at the same elevation (both as administrator or neither), then retry.");
        OneNoteHResults.IsDisconnected(OneNoteHResults.ClassNotRegistered).Should().BeFalse();
        OneNoteHResults.IsDisconnected(OneNoteHResults.ServerExecFailed).Should().BeFalse();
        OneNoteHResults.IsDisconnected(OneNoteHResults.InvalidXml).Should().BeFalse();
    }

    [Fact]
    public void Rpc_call_failed_is_named_and_joins_the_disconnected_family_not_the_busy_one()
    {
        const int callFailed = unchecked((int)0x800706BE);

        OneNoteHResults.RpcCallFailed.Should().Be(callFailed);
        OneNoteHResults.Describe(callFailed).Should().Be("RPC_S_CALL_FAILED (0x800706BE)");
        OneNoteHResults.IsDisconnected(callFailed).Should().BeTrue("OneNote crashing while a call is in flight is a mid-run disconnect");
        OneNoteHResults.IsUnavailable(callFailed).Should().BeTrue("an unavailable OneNote exits with 3, not the generic 5");
        OneNoteHResults.IsBusy(callFailed).Should().BeFalse("a crashed OneNote is not retried");
        OneNoteHResults.IsXmlRejected(callFailed).Should().BeFalse();
    }

    [Fact]
    public void Describe_names_known_values_and_falls_back_to_hex()
    {
        OneNoteHResults.Describe(OneNoteHResults.InvalidXml).Should().Be("hrInvalidXML (0x80042001)");
        OneNoteHResults.Describe(OneNoteHResults.RpcCallRejected).Should().Be("RPC_E_CALL_REJECTED (0x80010001)");
        OneNoteHResults.Describe(unchecked((int)0x80004005)).Should().Be("0x80004005");
    }

    [Fact]
    public void Busy_exception_message_names_the_call_and_the_result()
    {
        OneNoteBusyException ex = new("SyncHierarchy", OneNoteHResults.RpcServerCallRetryLater, null);

        ex.Message.Should().Be("OneNote is busy and rejected SyncHierarchy twice (RPC_E_SERVERCALL_RETRYLATER (0x8001010A)).");
        ex.HResult.Should().Be(OneNoteHResults.RpcServerCallRetryLater);
    }
}
