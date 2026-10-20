using System.Xml.Linq;
using Serilog;
using Sfp.OneNote.Com;
using Xunit.Abstractions;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Talks to the real OneNote desktop. Runs only on a Windows PC where the OneNote.Application ProgID resolves;
/// skips with a message everywhere else. Select with: dotnet test --filter "Category=COM".
/// </summary>
[Trait("Category", "COM")]
public class OneNoteComIntegrationTests
{
    private const string Namespace2013 = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    private readonly ITestOutputHelper _output;

    public OneNoteComIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void Opens_OneNote_prints_its_version_and_lists_the_notebooks()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new SkipException("The OneNote COM API is only reachable on Windows.");
        }

        Skip.If(Type.GetTypeFromProgID(ProgIdActivator.ProgId, throwOnError: false) is null, "OneNote desktop is not installed: the OneNote.Application ProgID does not resolve.");

        ILogger log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(new TestOutputSink(_output)).CreateLogger();
        using OneNoteApp app = new(log);

        string version = app.Version;
        _output.WriteLine("OneNote version: " + version);
        version.Should().NotBeNullOrWhiteSpace();

        string xml = app.GetHierarchy(null, HierarchyScope.Notebooks);
        XDocument doc = XDocument.Parse(xml);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.Namespace.NamespaceName.Should().Be(Namespace2013, "the 2013 schema namespace must be accepted");
        List<string> names = doc.Root.Elements(doc.Root.Name.Namespace + "Notebook").Select(n => (string?)n.Attribute("name") ?? "").ToList();
        _output.WriteLine("Open notebooks: " + (names.Count == 0 ? "(none, sign in to OneNote once)" : string.Join(", ", names)));
    }

    private sealed class TestOutputSink : Serilog.Core.ILogEventSink
    {
        private readonly ITestOutputHelper _output;

        public TestOutputSink(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Emit(Serilog.Events.LogEvent logEvent) => _output.WriteLine(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
}
