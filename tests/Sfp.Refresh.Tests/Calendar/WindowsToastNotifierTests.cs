using Serilog.Core;
using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

public class WindowsToastNotifierTests
{
    [Fact]
    public void Script_escapes_the_title_and_message_for_xml_and_powershell_quoting()
    {
        string script = WindowsToastNotifier.BuildScript("Sources <down> & out", "Feed 'Work' failed: a < b");

        script.Should().Contain("<text>Sources &lt;down&gt; &amp; out</text>");
        script.Should().Contain("<text>Feed &apos;Work&apos; failed: a &lt; b</text>", "the escaped apostrophe cannot break the single-quoted PowerShell literal");
        script.Split('\n').Single(l => l.Contains("LoadXml", StringComparison.Ordinal)).Count(c => c == '\'').Should().Be(2, "only the two delimiting quotes remain");
        script.Should().Contain("CreateToastNotifier('" + WindowsToastNotifier.PowerShellAppId + "')");
    }

    [Fact]
    public void Off_Windows_the_notifier_is_a_no_op_and_on_Windows_it_runs_the_script_hook()
    {
        List<string> ran = new();
        WindowsToastNotifier notifier = new(Logger.None, script =>
        {
            ran.Add(script);
            return true;
        });

        notifier.Notify("Title", "Message");

        if (OperatingSystem.IsWindows())
        {
            ran.Should().ContainSingle().Which.Should().Contain("<text>Title</text>");
        }
        else
        {
            ran.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_failing_script_never_throws_out_of_Notify()
    {
        WindowsToastNotifier notifier = new(Logger.None, _ => throw new InvalidOperationException("no WinRT here"));

        Action act = () => notifier.Notify("Title", "Message");

        act.Should().NotThrow();
    }
}
