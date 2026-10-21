using System.Collections.Concurrent;
using Serilog.Events;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Tests.Writer;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>
/// A default Q1 2027 generate composed the way the generate verb composes it (shipped manifest, real preflight, date
/// engine, plan builder, page renderer over the planner PDF, COM writer, Next quarter link writer), against the
/// in-memory OneNote instead of COM. Only the DPI is lowered, to keep the run fast; it plays no part in warnings.
/// </summary>
public class GenerateEndToEndTests
{
    /// <summary>Records the anchor placements the real renderer returns for each page.</summary>
    private sealed class RecordingRenderer : IPageRenderer
    {
        private readonly IPageRenderer _inner;

        public RecordingRenderer(IPageRenderer inner)
        {
            _inner = inner;
        }

        public ConcurrentDictionary<PageKey, IReadOnlyList<string>> Placements { get; } = new();

        public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
        {
            RenderedPage art = _inner.Render(page, plan, o);
            Placements[page.Key] = art.Anchors.Select(a => a.AnchorId).ToList();
            return art;
        }
    }

    [Fact]
    public void A_default_q1_2027_generate_against_the_in_memory_onenote_ends_with_no_warning_but_the_fallback_font()
    {
        string root = TestPaths.NewTempDir("generate-e2e");
        try
        {
            Manifest manifest = new ManifestLoader().LoadShipped(TestPaths.TemplatesDir);
            using FakeOneNoteApp app = new() { CreateFoldersOnDisk = true };
            using FontService fonts = FontService.Load(manifest.Font, TestPaths.TemplatesDir);
            using Rasterizer rasterizer = new(ColorParser.Parse(manifest.Colors.Background));
            CapturingSink log = new();
            DateEngine dates = new();
            SidecarStore sidecars = new();
            RecordingRenderer renderer = new(new PageRenderer(manifest, rasterizer, fonts, dates));
            GenerateOptions options = new()
            {
                Start = new QuarterSpec(2027, 1),
                PdfPath = SfpPdf.Path ?? SyntheticTemplate.Build(),
                OutputFolder = Path.Combine(root, "notebooks"),
                Dpi = 72,
            };
            PageXmlOptions xml = PageXmlOptions.From(manifest, options, fonts.FamilyInUse, MonJan4.FixedZone);
            GenerateOrchestrator orchestrator = new(
                new GeneratePreflight(new ManifestValidator(), new FakeOneNoteLocator(true, app), new PreflightFileSystem(), sidecars, dates, fonts.Warnings),
                dates,
                new PlanBuilder(dates),
                renderer,
                () => new ComNotebookWriter(app, xml, log.CreateLogger()),
                sidecars,
                new GenerateOrchestratorOptions { FontFamily = fonts.FamilyInUse, TimeZone = MonJan4.FixedZone, LogPath = Path.Combine(root, "logs", "generate.log") },
                log.CreateLogger(),
                () => new NextQuarterLinkWriter(app, sidecars, manifest, xml, log.CreateLogger()));

            GenerateResult result = orchestrator.Run(new GenerateRequest(options, manifest, TestPaths.TemplatesDir, false, null), null, CancellationToken.None);

            result.Success.Should().BeTrue(result.Error);
            result.Warnings.Should().Equal(fonts.Warnings, "a default run ends with no warning but the font fallback this machine needs");
            fonts.Warnings.Should().OnlyContain(w => w.Contains(manifest.Font.Family, StringComparison.Ordinal));
            log.Events.Where(e => e.Level >= LogEventLevel.Warning).Select(e => e.RenderMessage(null)).Should().BeEmpty();
            result.PagesWritten.Should().Be(144);
            app.WrittenPages.Should().HaveCount(144);

            renderer.Placements[PageKey.QuarterlyReview].Should().Contain(NextQuarterLinkWriter.AnchorId, "the renderer reserves the Next quarter slot, which Generate skips silently");
            Sidecar sidecar = sidecars.Load(result.SidecarPath!)!;
            sidecar.Pages[PageKey.QuarterlyReview.Value].Anchors.Should().NotContainKey(NextQuarterLinkWriter.AnchorId);
            sidecar.Pages[PageKey.Day(new DateOnly(2027, 1, 4)).Value].Anchors.Should().ContainKeys("dailyDate", "prev", "next");
            app.PageContent(sidecar.Pages[PageKey.ObjectiveDetails.Value].PageId).Should().NotContain("<one:Image", "the blank parent page is written without an image");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
