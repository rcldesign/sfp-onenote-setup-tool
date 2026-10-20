using Sfp.Core;
using Sfp.Core.Plan;
using Sfp.OneNote.Tests.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// Stands in for the M2 page renderer: returns a 1 x 1 PNG for every page with one link placement per LinkSpec on the
/// page (positions spread so they are distinguishable) and, on daily pages, a propagated legend slot. Records what it
/// rendered, on which threads, and how many renders overlapped; can fail or stall on chosen pages.
/// </summary>
public sealed class FakePageRenderer : IPageRenderer
{
    private readonly List<PageKey> _rendered = new();
    private readonly object _gate = new();
    private int _inFlight;

    /// <summary>Exception to throw for a page, or null.</summary>
    public Func<PageSpec, Exception?>? Failure { get; set; }

    /// <summary>Delay applied to every render (to make overlap observable).</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Called before a page is rendered (used to trigger cancellation at a chosen point).</summary>
    public Action<PageSpec>? BeforeRender { get; set; }

    public IReadOnlyList<PageKey> Rendered
    {
        get
        {
            lock (_gate)
            {
                return _rendered.ToList();
            }
        }
    }

    public int MaxConcurrent { get; private set; }

    public HashSet<int> Threads { get; } = new();

    public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
    {
        BeforeRender?.Invoke(page);
        lock (_gate)
        {
            _inFlight++;
            MaxConcurrent = Math.Max(MaxConcurrent, _inFlight);
            Threads.Add(Environment.CurrentManagedThreadId);
        }

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                Thread.Sleep(Delay);
            }

            Exception? failure = Failure?.Invoke(page);
            if (failure is not null)
            {
                throw failure;
            }

            List<AnchorPlacement> anchors = new();
            int i = 0;
            foreach (LinkSpec link in plan.LinksFrom(page.Key))
            {
                anchors.Add(new AnchorPlacement(link.AnchorId, 100 + (120 * i), 20, 110, 20, link.Text, TextAlign.Left, 10) { Kind = AnchorKind.Link });
                i++;
            }

            if (page.Type == PageType.Day)
            {
                anchors.Add(new AnchorPlacement("objectiveLegend", 230, 48, 640, 12, "", TextAlign.Left, 8) { Kind = AnchorKind.Propagated });
            }

            lock (_gate)
            {
                _rendered.Add(page.Key);
            }

            (int w, int h) = (Rasterizer.PixelsFor(1112, o.Dpi), Rasterizer.PixelsFor(770, o.Dpi));
            return new RenderedPage(page.Key, MonJan4.Png, w, h, anchors);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }
}
