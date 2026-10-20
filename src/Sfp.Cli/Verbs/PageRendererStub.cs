using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Render.Fonts;

// Temporary stand-in for Sfp.Render.PageRenderer (cards M2.4 to M2.10), which another engineer is writing in parallel.
// The integrator deletes this file once the real class is merged: while both exist the compiler reports CS0436
// (a type in this assembly conflicts with the imported type), which under warnings-as-errors fails the build and
// is the signal to remove the stub. GenerateCommand constructs it as new PageRenderer(manifest, rasterizer, fonts,
// dateEngine); adjust that one line if the real constructor differs.
namespace Sfp.Render;

internal sealed class PageRenderer : IPageRenderer
{
    public PageRenderer(Manifest manifest, IRasterizer rasterizer, IFontService fonts, IDateEngine dates)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(dates);
    }

    public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o) =>
        throw new NotImplementedException("The page renderer (cards M2.4 to M2.10) has not been merged into this build yet.");
}
