using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>
/// Shared by every page-render test: the shipped manifest, the fallback font (Bookman is never installed here), one
/// rasterizer whose cache stays warm across tests, the date engine, the renderer and the Q1 2027 plan. Pages are
/// rendered from the synthetic template at 220 DPI unless a test asks otherwise.
/// </summary>
public sealed class RenderFixture : IDisposable
{
    public const int Dpi = 220;

    public RenderFixture()
    {
        Manifest = TestManifest.Shipped;
        Background = ColorParser.Parse(Manifest.Colors.Background);
        Ink = ColorParser.Parse(Manifest.Colors.Ink);
        Fonts = FontService.Load(Manifest.Font, TestPaths.TemplatesDir, Array.Empty<string>());
        Rasterizer = new Rasterizer(Background);
        Engine = new DateEngine();
        Renderer = new PageRenderer(Manifest, Rasterizer, Fonts, Engine);
        Pdf = SyntheticTemplate.Build();
        Q1Options = new GenerateOptions { Start = new QuarterSpec(2027, 1), PdfPath = Pdf };
        Q1Plan = Plan(Q1Options);
    }

    public Manifest Manifest { get; }

    public SKColor Background { get; }

    public SKColor Ink { get; }

    public FontService Fonts { get; }

    public Rasterizer Rasterizer { get; }

    public DateEngine Engine { get; }

    public PageRenderer Renderer { get; }

    /// <summary>Path of the synthetic template PDF.</summary>
    public string Pdf { get; }

    public GenerateOptions Q1Options { get; }

    public NotebookPlan Q1Plan { get; }

    public NotebookPlan Plan(GenerateOptions options) => new PlanBuilder(Engine).Build(Manifest, Engine.Build(options.Start), options);

    public RenderOptions Options(GenerateOptions options, int dpi = Dpi) => new(Pdf, dpi, options);

    /// <summary>Set to a folder to keep a copy of every page these tests render (named by key and DPI), for a look at the output.</summary>
    public const string DebugDirEnvVar = "SFP_RENDER_DEBUG_DIR";

    /// <summary>Renders a page of the Q1 2027 plan with default options.</summary>
    public RenderedPage Render(PageKey key, int dpi = Dpi) => Keep(Renderer.Render(Q1Plan.Page(key), Q1Plan, Options(Q1Options, dpi)), dpi, "");

    /// <summary>Renders a page of the plan built for adjusted options.</summary>
    public RenderedPage Render(PageKey key, Func<GenerateOptions, GenerateOptions> adjust, int dpi = Dpi, string tag = "variant")
    {
        GenerateOptions options = adjust(Q1Options);
        NotebookPlan plan = Plan(options);
        return Keep(Renderer.Render(plan.Page(key), plan, Options(options, dpi)), dpi, tag);
    }

    /// <summary>Decodes a rendered page's PNG.</summary>
    public static SKBitmap Decode(RenderedPage page) => SKBitmap.Decode(page.Png) ?? throw new InvalidDataException("PNG did not decode: " + page.Key);

    /// <summary>
    /// OCR of a region in points of a rendered sheet; empty when the engine fails. Keep the padding small enough that
    /// no neighboring rule enters the crop: Tesseract reads a rule end as punctuation and drops the digits beside it.
    /// </summary>
    public static string Read(SKBitmap sheet, RectPt region, int dpi = Dpi, double paddingPt = 6, OcrLayout layout = OcrLayout.SingleLine)
    {
        using SKBitmap crop = Ocr.Crop(sheet, region, dpi, paddingPt);
        string result = Ocr.TryRead(crop, out string text, layout) ? Ocr.Normalize(text) : "";
        KeepCrop(crop, region, layout, result);
        return result;
    }

    /// <summary>With <see cref="DebugDirEnvVar"/> set, saves every OCR crop under crops/ named by region, layout and what was read.</summary>
    private static void KeepCrop(SKBitmap crop, RectPt region, OcrLayout layout, string result)
    {
        string? dir = Environment.GetEnvironmentVariable(DebugDirEnvVar);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        string folder = Path.Combine(dir, "crops");
        Directory.CreateDirectory(folder);
        string name = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{region.X:0}-{region.Y:0}-{region.W:0}x{region.H:0}.{layout}.{(result.Length == 0 ? "empty" : new string(result.Where(char.IsLetterOrDigit).ToArray()))}.png");
        using SKData data = crop.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name), data.ToArray());
    }

    private static RenderedPage Keep(RenderedPage page, int dpi, string tag)
    {
        string? dir = Environment.GetEnvironmentVariable(DebugDirEnvVar);
        if (!string.IsNullOrWhiteSpace(dir) && page.Png.Length > 0)
        {
            Directory.CreateDirectory(dir);
            string name = page.Key.Value.Replace(':', '-') + "." + dpi.ToString(System.Globalization.CultureInfo.InvariantCulture) + (tag.Length > 0 ? "." + tag : "") + ".png";
            File.WriteAllBytes(Path.Combine(dir, name), page.Png);
        }

        return page;
    }

    /// <summary>The digits of a string, for comparing OCR output without its spacing.</summary>
    public static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    public bool HasInk(SKBitmap sheet, RectPt region, int dpi = Dpi, int minDarkening = 40) => Pixels.HasInk(sheet, region, dpi, Background, minDarkening);

    public void Dispose()
    {
        Rasterizer.Dispose();
        Fonts.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class RenderCollection : ICollectionFixture<RenderFixture>
{
    public const string Name = "Render";
}
