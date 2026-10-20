using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Render.Colors;
using Sfp.Render.Fonts;
using Sfp.Render.Overlay;
using Sfp.Render.Pages;
using SkiaSharp;

namespace Sfp.Render;

/// <summary>
/// The production <see cref="IPageRenderer"/>. For a page with PDF art it takes the rasterized page from the
/// rasterizer (a copy it owns), for a generated page a blank beige sheet, opens a <see cref="SheetCanvas"/> at the
/// render DPI, applies the overlays of the page type (dates, strip, counter, calendar numbers, time blocks, generated
/// art), erases the printed art under every link slot and collects the anchor placements, then encodes the sheet as an
/// opaque RGB PNG. Rendering is deterministic: no clock, fixed font settings, so two renders of one page are
/// byte-identical. Contents (a native page of links) and the blank Objective Details parent have no image: they get an
/// empty PNG (zero bytes, 0 x 0) and no anchors. Safe to call from several threads at once; the rasterizer serializes
/// PDFium itself.
/// </summary>
public sealed class PageRenderer : IPageRenderer
{
    private readonly Manifest manifest;
    private readonly IRasterizer rasterizer;
    private readonly IFontService fonts;
    private readonly IDateEngine dates;
    private readonly SKColor background;
    private readonly SKColor ink;

    public PageRenderer(Manifest manifest, IRasterizer rasterizer, IFontService fonts)
        : this(manifest, rasterizer, fonts, new DateEngine())
    {
    }

    /// <param name="manifest">The merged manifest; every coordinate, size and color comes from it.</param>
    /// <param name="rasterizer">Renders the PDF pages; shared across pages so its cache stays warm.</param>
    /// <param name="fonts">The template typeface (or the bundled fallback).</param>
    /// <param name="dates">The date engine for calendar grids and week lookups (the production engine unless a test injects one).</param>
    public PageRenderer(Manifest manifest, IRasterizer rasterizer, IFontService fonts, IDateEngine dates)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(dates);
        this.manifest = manifest;
        this.rasterizer = rasterizer;
        this.fonts = fonts;
        this.dates = dates;
        background = ColorParser.Parse(manifest.Colors.Background);
        ink = ColorParser.Parse(manifest.Colors.Ink);
    }

    /// <summary>True for the page types that have no sheet image (Contents and the blank parent page).</summary>
    public static bool HasNoImage(PageType type) => type is PageType.Contents or PageType.Blank;

    /// <inheritdoc />
    public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(o);
        if (HasNoImage(page.Type))
        {
            return new RenderedPage(page.Key, Array.Empty<byte>(), 0, 0, Array.Empty<AnchorPlacement>());
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(o.Dpi, 1);
        SKBitmap? rasterized = null;
        SheetCanvas? sheet = null;
        try
        {
            if (page.PdfPage is int pdfPage)
            {
                if (string.IsNullOrWhiteSpace(o.PdfPath))
                {
                    throw new ArgumentException($"Page {page.Key} needs PDF page {pdfPage} but RenderOptions.PdfPath is empty.", nameof(o));
                }

                rasterized = rasterizer.Render(o.PdfPath, pdfPage, o.Dpi);
                sheet = new SheetCanvas(rasterized, o.Dpi);
            }
            else
            {
                sheet = SheetCanvas.Blank(manifest.PageWidthPt, manifest.PageHeightPt, o.Dpi, background);
            }

            using RenderContext context = new(manifest, fonts, dates, page, plan, o, sheet, background, ink);
            Paint(context);
            AnchorLayout.PlaceFixedAnchors(context);
            sheet.Flush();
            SKBitmap bitmap = sheet.Bitmap;
            byte[] png = PngEncoder.Encode(bitmap);
            return new RenderedPage(page.Key, png, bitmap.Width, bitmap.Height, context.Anchors.ToList());
        }
        finally
        {
            sheet?.Dispose();
            rasterized?.Dispose();
        }
    }

    private static void Paint(RenderContext c)
    {
        switch (c.Page.Type)
        {
            case PageType.Cover:
                TemplatePages.Cover(c);
                break;
            case PageType.Setup:
                GeneratedPages.Setup(c);
                break;
            case PageType.Annual:
                TemplatePages.Annual(c);
                break;
            case PageType.Objective:
                TemplatePages.Objective(c);
                break;
            case PageType.Calendar:
                CalendarPage.Draw(c);
                break;
            case PageType.WeeklyOverview:
                TemplatePages.Overview(c);
                break;
            case PageType.Day:
                TemplatePages.Day(c);
                break;
            case PageType.WeeklyReflection:
                TemplatePages.Reflection(c);
                break;
            case PageType.Scorecard:
                GeneratedPages.Scorecard(c);
                break;
            case PageType.MonthlyReview:
                GeneratedPages.Review(c);
                break;
            case PageType.QuarterlyReview:
                GeneratedPages.Review(c);
                AnchorLayout.ReserveNextQuarter(c);
                break;
            case PageType.Notes:
                GeneratedPages.Notes(c);
                break;
            default:
                throw new InvalidOperationException($"Page type {c.Page.Type} of {c.Page.Key} has no renderer.");
        }
    }
}
