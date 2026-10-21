using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
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
/// Another run holds the output folder's lock file (<see cref="RenderOnlyRunner.LockFileName"/>). Two runs into one folder
/// would overwrite each other's PNGs under an index that describes only one of them, so the second stops before it
/// deletes or writes anything. The message names the folder; the inner exception is the refused open of the lock file.
/// </summary>
public sealed class RenderFolderInUseException : IOException
{
    public RenderFolderInUseException()
    {
    }

    public RenderFolderInUseException(string message)
        : base(message)
    {
    }

    public RenderFolderInUseException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Renders every page of a plan to {outDir}/{key with ':' as '-'}.png plus pages.json, in parallel
/// (<see cref="RenderParallelism.Default"/> workers by default: the processor count, at most four; PDFium is serialized
/// by the rasterizer, overlays and PNG encoding run side by side). Before any page renders, a pages.json an earlier run
/// left in the folder is read and deleted, then the PNG files it lists; no other file in the folder is touched. The run
/// holds the folder's <see cref="LockFileName"/> from start to finish, so a second run into the same folder stops with a
/// <see cref="RenderFolderInUseException"/> before it deletes or writes anything.
/// Each page's PNG is written and released before the next page on that worker, so memory stays at a few pages.
/// Progress is reported per finished page; cancellation stops after the pages in flight and writes no pages.json, so
/// the folder holds no index over a partial render. A page that fails to render stops the run the same way; with
/// <see cref="NamePageFailures"/> its exception is wrapped in a <see cref="PageRenderException"/> naming the page.
/// </summary>
public sealed class RenderOnlyRunner
{
    public const string IndexFileName = "pages.json";

    /// <summary>
    /// The lock file a run holds in its output folder from start to finish: opened with FileShare.None, so no second run
    /// can open it, and deleted when the run closes it. A run that crashes releases it with its process (outside Windows
    /// the empty file can stay behind, holding nothing), so a stale lock never blocks a later run. The cleanup never
    /// takes it for a page: it deletes only the .png names an index lists.
    /// </summary>
    public const string LockFileName = ".sfp-render.lock";

    /// <summary>ERROR_SHARING_VIOLATION (32) as an HRESULT: Windows refuses to open a file another handle holds with FileShare.None.</summary>
    private const int ErrorSharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// EWOULDBLOCK on Linux. Outside Windows .NET backs FileShare.None with an advisory lock and, when another handle holds
    /// it, throws an IOException whose HResult is this errno.
    /// </summary>
    private const int EwouldblockLinux = 11;

    /// <summary>EWOULDBLOCK on macOS and the BSDs.</summary>
    private const int EwouldblockBsd = 35;

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

    /// <summary>
    /// Renders the whole plan into <paramref name="outDir"/> (created if missing), first removing the previous run's
    /// index and the PNG files it lists. The run holds the folder's <see cref="LockFileName"/> until it returns or throws.
    /// </summary>
    /// <exception cref="RenderFolderInUseException">Another run holds the folder; nothing is deleted or written.</exception>
    /// <exception cref="InvalidDataException">The folder holds a pages.json this tool cannot read; nothing is deleted.</exception>
    public RenderOnlyResult Run(NotebookPlan plan, RenderOptions options, string outDir, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outDir);
        Stopwatch clock = Stopwatch.StartNew();
        Directory.CreateDirectory(outDir);
        using FileStream folderLock = LockFolder(outDir);
        RemovePreviousRun(outDir);

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

    /// <summary>
    /// Opens the folder's <see cref="LockFileName"/> for this run alone (created if missing, deleted on close), or throws a
    /// <see cref="RenderFolderInUseException"/> naming the folder when another run holds it. Any other failure to open it
    /// (a folder that cannot be written) propagates as it is.
    /// </summary>
    private static FileStream LockFolder(string outDir)
    {
        string path = Path.Combine(outDir, LockFileName);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex) when (ex.HResult == HeldElsewhereResult)
        {
            throw new RenderFolderInUseException($"Another render is using the folder {Path.GetFullPath(outDir)}: wait for it to finish, or render into another folder.", ex);
        }
    }

    /// <summary>The HResult of the IOException an open gets when another handle holds the file with FileShare.None, on this OS.</summary>
    private static int HeldElsewhereResult =>
        OperatingSystem.IsWindows() ? ErrorSharingViolation : OperatingSystem.IsLinux() ? EwouldblockLinux : EwouldblockBsd;

    /// <summary>
    /// Reads the folder's pages.json and deletes it before the PNG files it lists, so a PNG delete that fails (a read-only
    /// file, one open in another program) leaves no index over a partly deleted render, a run that fails or is cancelled
    /// later leaves no index over partly overwritten PNGs, and a run that succeeds leaves no PNG of a page its plan no
    /// longer has. Only a plain .png file name the index records is deleted (never a path into another folder), and no
    /// other file is touched. A PNG delete that fails stops the run with its exception; the listed PNGs it did not reach
    /// stay in the folder with no index naming them.
    /// </summary>
    private static void RemovePreviousRun(string outDir)
    {
        string indexPath = Path.Combine(outDir, IndexFileName);
        if (!File.Exists(indexPath))
        {
            return;
        }

        RenderIndex previous;
        try
        {
            previous = RenderIndex.Parse(File.ReadAllText(indexPath));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{indexPath} is not an index this tool can read, so the PNG files of the run that wrote it are unknown and nothing was deleted. Remove it or render into another folder. {ex.Message}", ex);
        }

        File.Delete(indexPath);
        IEnumerable<string?> files = (previous.Pages ?? Array.Empty<RenderIndexPage>()).Select(p => p?.Png);
        foreach (string png in files.OfType<string>())
        {
            if (Path.GetFileName(png) == png && png.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(outDir, png));
            }
        }
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
