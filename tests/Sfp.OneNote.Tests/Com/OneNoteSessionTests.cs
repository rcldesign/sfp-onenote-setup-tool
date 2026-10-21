using System.Runtime.InteropServices;
using Serilog.Events;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;

namespace Sfp.OneNote.Tests.Com;

/// <summary>The scheduler, retry and mapping rules of the COM wrapper, driven with a fake target (no OneNote needed).</summary>
public class OneNoteSessionTests
{
    private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(20);

    private static (OneNoteSession Session, FakeOneNoteTarget Fake, FakeActivator Activator, CapturingSink Log) Create()
    {
        FakeOneNoteTarget fake = new();
        FakeActivator activator = new(fake);
        CapturingSink sink = new();
        OneNoteSession session = new(activator, sink.CreateLogger(), FastRetry);
        return (session, fake, activator, sink);
    }

    [Fact]
    public void A_call_rejected_once_as_busy_is_retried_and_succeeds()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, CapturingSink log) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.RpcCallRejected);

            string xml = session.GetHierarchy(null, HierarchyScope.Notebooks);

            xml.Should().Contain("SFP Q1 2027");
            fake.CallCount("GetHierarchy").Should().Be(2, "the first attempt was rejected and the second succeeded");
            log.Events.Should().Contain(e => e.Level == LogEventLevel.Warning && e.RenderMessage(null).Contains("retrying once", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Retry_later_is_treated_like_call_rejected()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.RpcServerCallRetryLater);

            session.SyncHierarchy("{nb}");

            fake.CallCount("SyncHierarchy").Should().Be(2);
        }
    }

    [Fact]
    public void A_call_rejected_twice_as_busy_raises_the_busy_exception_with_the_hresult()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.RpcCallRejected, OneNoteHResults.RpcCallRejected);

            Action act = () => session.CreateNewPage("{s}", NewPageStyle.BlankPageWithTitle);

            OneNoteBusyException ex = act.Should().Throw<OneNoteBusyException>().Which;
            ex.HResult.Should().Be(OneNoteHResults.RpcCallRejected);
            ex.Call.Should().Be("CreateNewPage");
            ex.InnerException.Should().BeOfType<COMException>();
            fake.CallCount("CreateNewPage").Should().Be(2, "exactly one retry is made");
        }
    }

    [Fact]
    public void A_modal_dialog_is_retried_once_and_then_reported_as_a_dialog_to_close()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.AppInModalUI);
            session.SyncHierarchy("{nb}");
            fake.CallCount("SyncHierarchy").Should().Be(2, "a dialog the user closes in time lets the retry through");

            fake.FailNext(OneNoteHResults.AppInModalUI, OneNoteHResults.AppInModalUI);
            Action act = () => session.GetHierarchy(null, HierarchyScope.Notebooks);

            OneNoteBusyException ex = act.Should().Throw<OneNoteBusyException>().Which;
            ex.HResult.Should().Be(OneNoteHResults.AppInModalUI);
            ex.Message.Should().Contain("hrAppInModalUI (0x80042030)").And.Contain("Close the dialog OneNote is showing, then retry.");
            fake.CallCount("GetHierarchy").Should().Be(2, "exactly one retry is made");
        }
    }

    [Fact]
    public void The_retry_waits_the_configured_delay()
    {
        FakeOneNoteTarget fake = new();
        CapturingSink sink = new();
        TimeSpan delay = TimeSpan.FromMilliseconds(150);
        using OneNoteSession session = new(new FakeActivator(fake), sink.CreateLogger(), delay);
        fake.FailNext(OneNoteHResults.RpcCallRejected);

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        session.NavigateTo("{p}");
        clock.Stop();

        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(120));
    }

    [Fact]
    public void Invalid_xml_maps_to_the_xml_rejected_exception_carrying_the_xml()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            const string pageXml = "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\"{p}\" bogus=\"1\"/>";
            fake.FailNext(OneNoteHResults.InvalidXml);

            Action act = () => session.UpdatePageContent(pageXml, DateTime.MinValue);

            OneNoteXmlRejectedException ex = act.Should().Throw<OneNoteXmlRejectedException>().Which;
            ex.HResult.Should().Be(OneNoteHResults.InvalidXml);
            ex.HResultHex.Should().Be("0x80042001");
            ex.Xml.Should().Be(pageXml);
            ex.Call.Should().Be("UpdatePageContent");
            ex.Message.Should().Contain("hrInvalidXML");
            fake.CallCount("UpdatePageContent").Should().Be(1, "XML rejections are not retried");
        }
    }

    [Fact]
    public void Malformed_xml_in_update_hierarchy_is_also_an_xml_rejection()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.MalformedXml);

            Action act = () => session.UpdateHierarchy("<one:Section/>");

            act.Should().Throw<OneNoteXmlRejectedException>().Which.Xml.Should().Be("<one:Section/>");
        }
    }

    [Fact]
    public void Class_not_registered_maps_to_the_unavailable_exception()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.ClassNotRegistered);

            Action act = () => session.GetHierarchy("", HierarchyScope.Sections);

            OneNoteUnavailableException ex = act.Should().Throw<OneNoteUnavailableException>().Which;
            ex.HResult.Should().Be(OneNoteHResults.ClassNotRegistered);
            ex.Message.Should().Contain(OneNoteUnavailableException.UserMessage);
        }
    }

    [Fact]
    public void A_disconnect_after_activation_is_reported_as_onenote_stopping_during_the_run()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            session.Version.Should().Be("16.0.fake", "OneNote was running when the run reached it");
            fake.FailNext(OneNoteHResults.RpcDisconnected);

            Action act = () => session.UpdatePageContent("<one:Page/>", DateTime.MinValue);

            OneNoteUnavailableException ex = act.Should().Throw<OneNoteUnavailableException>().Which;
            ex.HResult.Should().Be(OneNoteHResults.RpcDisconnected);
            ex.Call.Should().Be("UpdatePageContent");
            ex.Message.Should().StartWith(OneNoteUnavailableException.DisconnectedMessage);
            ex.Message.Should().NotContain(OneNoteUnavailableException.UserMessage);
        }
    }

    [Fact]
    public void OneNote_crashing_while_a_call_is_in_flight_is_reported_as_onenote_stopping_during_the_run()
    {
        const int callFailed = unchecked((int)0x800706BE);
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            session.Version.Should().Be("16.0.fake", "OneNote was running when the run reached it");
            fake.FailNext(callFailed);

            Action act = () => session.UpdatePageContent("<one:Page/>", DateTime.MinValue);

            OneNoteUnavailableException ex = act.Should().Throw<OneNoteUnavailableException>().Which;
            ex.HResult.Should().Be(callFailed);
            ex.Call.Should().Be("UpdatePageContent");
            ex.Message.Should().StartWith(OneNoteUnavailableException.DisconnectedMessage + " UpdatePageContent failed with RPC_S_CALL_FAILED (0x800706BE)");
            ex.Message.Should().NotContain(OneNoteUnavailableException.UserMessage);
            fake.CallCount("UpdatePageContent").Should().Be(1, "a crashed OneNote is not retried");
        }
    }

    [Fact]
    public void An_activation_that_cannot_reach_the_com_server_carries_the_install_message()
    {
        (OneNoteSession session, _, FakeActivator activator, _) = Create();
        using (session)
        {
            activator.Failure = new COMException("The RPC server is unavailable.", OneNoteHResults.RpcServerUnavailable);

            Action act = () => session.GetHierarchy(null, HierarchyScope.Notebooks);

            OneNoteUnavailableException ex = act.Should().Throw<OneNoteUnavailableException>().Which;
            ex.Call.Should().Be(OneNoteHResults.ActivateCall);
            ex.Message.Should().StartWith(OneNoteUnavailableException.UserMessage).And.Contain(OneNoteUnavailableException.InstallUrl);
            ex.Message.Should().NotContain(OneNoteUnavailableException.DisconnectedMessage);
        }
    }

    [Fact]
    public void Activation_failure_maps_to_the_unavailable_exception_and_is_retried_on_the_next_call()
    {
        (OneNoteSession session, _, FakeActivator activator, _) = Create();
        using (session)
        {
            activator.Failure = new COMException("no server", OneNoteHResults.ServerExecFailed);

            Action act = () => session.GetHierarchy(null, HierarchyScope.Notebooks);

            act.Should().Throw<OneNoteUnavailableException>().Which.HResult.Should().Be(OneNoteHResults.ServerExecFailed);
            activator.Failure = null;
            session.Version.Should().Be("16.0.fake");
            activator.ActivationCount.Should().Be(2);
        }
    }

    [Fact]
    public void Any_other_com_failure_maps_to_the_base_exception_with_the_hresult_and_name()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            fake.FailNext(OneNoteHResults.PageDoesNotExist);

            Action act = () => session.GetPageContent("{missing}", PageInfo.Basic);

            OneNoteException ex = act.Should().Throw<OneNoteException>().Which;
            ex.Should().BeOfType<OneNoteException>();
            ex.HResult.Should().Be(OneNoteHResults.PageDoesNotExist);
            ex.Message.Should().Contain("hrPageDoesNotExist").And.Contain("0x80042005").And.Contain("GetPageContent");
        }
    }

    [Fact]
    public void Calls_from_several_threads_all_run_on_the_one_worker_thread()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, FakeActivator activator, _) = Create();
        using (session)
        {
            int[] callerThreads = new int[12];
            Parallel.For(0, callerThreads.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                callerThreads[i] = Environment.CurrentManagedThreadId;
                session.SyncHierarchy("{" + i + "}");
            });

            IReadOnlyList<(string Call, int ThreadId)> calls = fake.Calls;
            calls.Should().HaveCount(callerThreads.Length);
            calls.Select(c => c.ThreadId).Distinct().Should().ContainSingle().Which.Should().Be(session.WorkerThreadId);
            callerThreads.Should().NotContain(session.WorkerThreadId, "callers never run the COM call on their own thread");
            activator.ActivatedOnThread.Should().Be(session.WorkerThreadId, "the COM object is created on the thread that uses it");
        }
    }

    [Fact]
    public void Out_parameters_and_enum_arguments_cross_the_dynamic_call_intact()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        using (session)
        {
            session.OpenHierarchy(@"C:\nb\SFP Q1 2027", null, CreateFileType.Notebook).Should().Be(@"{id:C:\nb\SFP Q1 2027}");
            session.CreateNewPage("{s}", NewPageStyle.BlankPageNoTitle).Should().Be("{page:{s}:2}");
            session.GetHyperlinkToObject("{p}", null).Should().Contain("page-id={p}");
            session.GetPageContent("{p}", PageInfo.All).Should().StartWith("<one:Page");
            DateTime expected = new(2027, 1, 4, 6, 0, 0, DateTimeKind.Local);
            session.UpdatePageContent("<one:Page/>", expected);
            fake.LastExpectedLastModified.Should().Be(expected);
            fake.LastUpdatedPageXml.Should().Be("<one:Page/>");
        }
    }

    [Fact]
    public void Log_lines_carry_the_call_name_and_duration_but_never_image_data()
    {
        (OneNoteSession session, _, _, CapturingSink log) = Create();
        using (session)
        {
            string base64 = Convert.ToBase64String(Enumerable.Range(0, 6000).Select(i => (byte)(i % 251)).ToArray());
            string xml = "<one:Page xmlns:one=\"x\" ID=\"{p}\"><one:Outline><one:Image format=\"png\" backgroundImage=\"true\"><one:Data>" + base64 + "</one:Data></one:Image></one:Outline></one:Page>";

            session.UpdatePageContent(xml, DateTime.MinValue);

            IReadOnlyList<string> messages = log.Messages;
            messages.Should().Contain(m => m.StartsWith("OneNote \"UpdatePageContent\" completed in", StringComparison.Ordinal) && m.EndsWith(" ms", StringComparison.Ordinal));
            messages.Should().OnlyContain(m => !m.Contains(base64.Substring(0, 64), StringComparison.Ordinal), "image bytes never reach the log");
            messages.Should().OnlyContain(m => m.Length < 400, "XML bodies are cut to 200 characters");
            log.Events.Should().Contain(e => e.Properties.ContainsKey("ElapsedMs") && e.Properties["Call"].ToString().Contains("UpdatePageContent", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Dispose_releases_the_target_on_the_worker_thread_and_rejects_later_calls()
    {
        (OneNoteSession session, FakeOneNoteTarget fake, _, _) = Create();
        session.Version.Should().Be("16.0.fake");
        int worker = session.WorkerThreadId;

        session.Dispose();

        fake.DisposedOnThread.Should().Be(worker);
        Action act = () => session.SyncHierarchy("{nb}");
        act.Should().Throw<ObjectDisposedException>();
        session.Dispose(); // idempotent
    }
}
