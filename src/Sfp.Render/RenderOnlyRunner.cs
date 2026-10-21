using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Sfp.Core.Plan;

namespace Sfp.Render;

/// <summary>Progress of a render-only run: pages finished so far, the total, and the page that just finished.</summary>
public sealed record RenderProgress(int Done, int Total, PageKey Key, string Title);

/// <summary>What a render-only run produced.</summary>
/// <param name="PagesRendered">Pages handled (every page of the plan).</param>
/// <param name="PngFiles">PNG files written (pages without an image write none).</param>
/// <param name="IndexPath">Full path of pages.json.</param>
/// <param name="Elapsed">Wall time of the run.</param>
public sealed record RenderOnlyResult(int PagesRendered, int PngFiles, string IndexPath, TimeSpan Elapsed);

/// <summary>
/// Renders every page of a plan to {outDir}/{key with ':' as '-'}.png plus pages.json, in parallel
/// (<see cref="RenderParallelism.Default"/> workers by default: the processor count, at most four; PDFium is serialized
/// by the rasterizer, overlays and PNG encoding run side by side). Each page's PNG is written and released before the
/// next page on that worker, so memory stays at a few pages. Progress is reported per finished page; cancellation
/// stops after the pages in flight and writes no pages.json. A page that fails to render stops the run the same way;
/// with <see cref="NamePageFailures"/> its exception is wrapped in a <see cref="PageRenderException"/> naming the page.
/// </summary>
public sealed class RenderOnlyRunner
{
    public const string IndexFileName = "pages.json";

    private readonly IPageRenderer renderer;

    /// <param name="renderer">The page renderer; it must be safe to call from several threads.</param>
    /// <param name="maxDegreeOfParallelism">Workers; null means <see cref="RenderParallelism.Default"/>.</param>
    public RenderOnlyRunner(IPageRenderer renderer, int? maxDegreeOfParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (maxDegreeOfParallelism is int n)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        }

        this.renderer = renderer;
        MaxDegreeOfParallelism = maxDegreeOfParallelism ?? RenderParallelism.Default;
    }

    /// <summary>The most pages this runner renders at once.</summary>
    public int MaxDegreeOfParallelism { get; }

    /// <summary>
    /// When true, an exception from the renderer (cancellation aside) surfaces as a <see cref="PageRenderException"/>
    /// whose message names the page in the PRD's render-error sentence; the render-only verb sets it. When false (the
    /// default) the renderer's own exception surfaces unchanged, for a caller whose renderer already names the page
    /// that failed (GenerateOrchestrator's render-only mode wraps its renderer in a RenderFailedException).
    /// </summary>
    public bool NamePageFailures { get; init; }

    /// <summary>The PNG file name of a page: its key with every ':' replaced by '-', plus ".png".</summary>
    public static string FileNameFor(PageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Value.Replace(':', '-') + ".png";
    }

    /// <summary>Renders the whole plan into <paramref name="outDir"/> (created if missing).</summary>
    public RenderOnlyResult Run(NotebookPlan plan, RenderOptions options, string outDir, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outDir);
        Stopwatch clock = Stopwatch.StartNew();
        Directory.CreateDirectory(outDir);

        List<(PageSpec Page, string Section)> pages = plan.Sections.SelectMany(s => s.Pages.Select(p => (p, s.Name))).ToList();
        RenderIndexPage?[] entries = new RenderIndexPage?[pages.Count];
        int done = 0;
        int files = 0;
        ParallelOptions parallel = new() { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = cancellationToken };
        try
        {
            Parallel.For(0, pages.Count, parallel, i =>
            {
                (PageSpec page, string section) = pages[i];
                RenderedPage art = Render(page, plan, options);
                string? file = null;
                if (art.Png.Length > 0)
                {
                    file = FileNameFor(page.Key);
                    File.WriteAllBytes(Path.Combine(outDir, file), art.Png);
                    Interlocked.Increment(ref files);
                }

                entries[i] = RenderIndexPage.From(page, section, art, file);
                int finished = Interlocked.Increment(ref done);
                progress?.Report(new RenderProgress(finished, pages.Count, page.Key, page.Title));
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }

        RenderIndex index = new(plan.Name, plan.Quarter.Label, options.Dpi, entries.Select(e => e!).ToList());
        string indexPath = Path.Combine(outDir, IndexFileName);
        File.WriteAllText(indexPath, index.Serialize() + "\n");
        return new RenderOnlyResult(pages.Count, files, indexPath, clock.Elapsed);
    }

    /// <summary>Renders one page, naming it in a <see cref="PageRenderException"/> when it fails and <see cref="NamePageFailures"/> is set.</summary>
    private RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions options)
    {
        try
        {
            return renderer.Render(page, plan, options);
        }
        catch (Exception ex) when (NamePageFailures && ex is not OperationCanceledException and not OutOfMemoryException)
        {
            throw new PageRenderException(page, ex);
        }
    }
}
