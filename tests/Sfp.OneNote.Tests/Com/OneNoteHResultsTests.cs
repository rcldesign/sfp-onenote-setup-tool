using System.Runtime.InteropServices;
using Sfp.OneNote.Com;

namespace Sfp.OneNote.Tests.Com;

/// <summary>The HRESULT table and the mapping rule on their own.</summary>
public class OneNoteHResultsTests
{
    [Theory]
    [InlineData(unchecked((int)0x80010001), true)]
    [InlineData(unchecked((int)0x8001010A), true)]
    [InlineData(unchecked((int)0x80042001), false)]
    [InlineData(unchecked((int)0x80040154), false)]
    public void Busy_results_are_exactly_call_rejected_and_retry_later(int hresult, bool busy)
    {
        OneNoteHResults.IsBusy(hresult).Should().Be(busy);
    }

    [Theory]
    [InlineData(unchecked((int)0x80040154), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x80080005), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x800706BA), typeof(OneNoteUnavailableException))]
    [InlineData(unchecked((int)0x80010108), typeof(OneNoteUnavailableException))]
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
    [InlineData(OneNoteHResults.ActivateCall, unchecked((int)0x800706BA))]
    [InlineData(OneNoteHResults.ActivateCall, unchecked((int)0x80010108))]
    [InlineData(OneNoteHResults.ActivateCall, unchecked((int)0x80040154))]
    [InlineData(OneNoteHResults.ActivateCall, unchecked((int)0x80080005))]
    [InlineData(OneNoteHResults.ActivateCall, unchecked((int)0x800401E3))]
    [InlineData("GetHierarchy", unchecked((int)0x80040154))]
    [InlineData("GetHierarchy", unchecked((int)0x80080005))]
    public void Activation_and_registration_failures_carry_the_install_message(string call, int hresult)
    {
        OneNoteException mapped = OneNoteHResults.Map(call, new COMException("boom", hresult), null);

        mapped.Should().BeOfType<OneNoteUnavailableException>();
        mapped.Message.Should().StartWith(OneNoteUnavailableException.UserMessage + " " + call + " failed with " + OneNoteHResults.Describe(hresult));
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
        OneNoteHResults.IsDisconnected(OneNoteHResults.ClassNotRegistered).Should().BeFalse();
        OneNoteHResults.IsDisconnected(OneNoteHResults.ServerExecFailed).Should().BeFalse();
        OneNoteHResults.IsDisconnected(OneNoteHResults.InvalidXml).Should().BeFalse();
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
