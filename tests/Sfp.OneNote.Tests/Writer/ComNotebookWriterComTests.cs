using System.Xml.Linq;
using Serilog;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Xunit.Abstractions;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// M3.3 to M3.5 against the real OneNote desktop: a scratch notebook with three sections in order, a week section with
/// Overview, seven indented days and Reflection, hyperlinks that resolve, then the notebook is deleted along with its
/// folder. Runs only on a Windows PC with OneNote; skips everywhere else. Select with: dotnet test --filter "Category=COM".
/// </summary>
[Trait("Category", "COM")]
public class ComNotebookWriterComTests
{
    private static readonly Manifest M = TestManifest.Load();
    private readonly ITestOutputHelper _output;

    public ComNotebookWriterComTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void Scratch_notebook_gets_three_sections_in_order_a_week_hierarchy_and_resolving_links_then_is_deleted()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "The OneNote COM API is only reachable on Windows.");
        Skip.If(!IsInstalled(), "OneNote desktop is not installed: the OneNote.Application ProgID does not resolve.");

        ILogger log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(new OutputSink(_output)).CreateLogger();
        string folder = ScratchFolder();
        string name = "sfp2onenote scratch " + Guid.NewGuid().ToString("N")[..8];
        using OneNoteLocator locator = new(log);
        IOneNoteApp app = locator.Open();
        ComNotebookWriter writer = new(app, MonJan4.Options(M), log);
        NotebookHandle? nb = null;
        try
        {
            nb = writer.CreateNotebook(folder, name);
            nb.Id.Should().NotBeNullOrWhiteSpace();
            Directory.Exists(nb.Path).Should().BeTrue("OpenHierarchy cftNotebook creates the folder");

            // M3.3: three sections in plan order.
            SectionHandle alpha = writer.CreateSection(nb, new SectionSpec("Alpha", "#F0E6D8", Array.Empty<PageSpec>()));
            SectionHandle beta = writer.CreateSection(nb, new SectionSpec("Beta", null, Array.Empty<PageSpec>()));
            SectionHandle gamma = writer.CreateSection(nb, new SectionSpec("Gamma", null, Array.Empty<PageSpec>()));
            XElement notebookXml = XElement.Parse(app.GetHierarchy(nb.Id, HierarchyScope.Sections));
            XNamespace one = notebookXml.Name.Namespace;
            notebookXml.Elements(one + "Section").Select(s => (string?)s.Attribute("name")).Should().Equal("Alpha", "Beta", "Gamma");
            notebookXml.Elements(one + "Section").Select(s => (string?)s.Attribute("ID")).Should().Equal(alpha.Id, beta.Id, gamma.Id);

            // M3.4: Overview, seven days at level 2, Reflection.
            Week week = new DateEngine().Weeks(TestPlans.Q1)[1];
            List<PageSpec> weekPages = new() { Spec(PageKey.WeekOverview(2), PageType.WeeklyOverview, "Weekly Overview", 1, week.MondayStart) };
            weekPages.AddRange(week.QuarterDays.Select(d => Spec(PageKey.Day(d), PageType.Day, PlannerText.DayTitle(d), 2, d)));
            weekPages.Add(Spec(PageKey.WeekReflection(2), PageType.WeeklyReflection, "Weekly Reflection", 1, week.SundayEnd));
            List<(PageHandle Handle, PageSpec Spec)> created = weekPages.Select(p => (writer.CreatePage(beta, p), p)).ToList();
            writer.ApplyHierarchy(beta, created);
            XElement sectionXml = XElement.Parse(app.GetHierarchy(beta.Id, HierarchyScope.Pages));
            List<XElement> pages = sectionXml.Elements(one + "Page").ToList();
            pages.Select(p => (string?)p.Attribute("name")).Should().Equal(weekPages.Select(p => p.Title));
            pages.Select(p => (string?)p.Attribute("pageLevel")).Should().Equal(weekPages.Select(p => p.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            pages.Should().HaveCount(9);

            // M3.5: hyperlinks resolve to the page and section ids, and navigation works.
            PageHandle monday = created[1].Handle;
            OneNoteHyperlink link = HyperlinkParser.Parse(writer.HyperlinkTo(monday));
            link.PageId.Should().Be(monday.Id);
            link.SectionId.Should().Be(beta.Id);
            HyperlinkParser.Parse(writer.HyperlinkTo(gamma)).SectionId.Should().Be(gamma.Id);
            writer.NavigateTo(monday);

            // M3.6: a page with anchors is written and read back with object ids for every anchor.
            IReadOnlyList<ResolvedAnchor> anchors = new[] { MonJan4.DateAnchor(M) with { Href = writer.HyperlinkTo(created[0].Handle) } };
            writer.WritePage(monday, created[1].Spec, MonJan4.Art(anchors), anchors);
            writer.ReadAnchorIds(monday, anchors).Should().ContainKey("dailyDate");
            _output.WriteLine("Writer warnings: " + string.Join(" | ", writer.Warnings));
        }
        finally
        {
            if (nb is not null)
            {
                try
                {
                    app.DeleteHierarchy(nb.Id, deletePermanently: true);
                }
                catch (OneNoteException ex)
                {
                    _output.WriteLine("DeleteHierarchy failed: " + ex.Message + "; closing instead");
                    app.CloseNotebook(nb.Id);
                }

                if (Directory.Exists(nb.Path))
                {
                    Directory.Delete(nb.Path, recursive: true);
                }
            }
        }
    }

    private static bool IsInstalled()
    {
        using OneNoteLocator locator = new(Serilog.Core.Logger.None);
        return locator.IsInstalled;
    }

    private static string ScratchFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sfp2onenote-com-tests");
        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root) && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The temp folder is under OneDrive, where OneNote refuses local notebooks; set TMP elsewhere.");
            }
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static PageSpec Spec(PageKey key, PageType type, string title, int level, DateOnly date) =>
        new(key, type, title, level, null, date, new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed class OutputSink : Serilog.Core.ILogEventSink
    {
        private readonly ITestOutputHelper _output;

        public OutputSink(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Emit(Serilog.Events.LogEvent logEvent) => _output.WriteLine(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
}
