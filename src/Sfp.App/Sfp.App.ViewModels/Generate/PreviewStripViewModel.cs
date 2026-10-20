using CommunityToolkit.Mvvm.ComponentModel;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render;

namespace Sfp.App.ViewModels.Generate;

/// <summary>
/// The three thumbnails under the fields: the first daily page of the quarter, the first calendar month and the cover,
/// rendered through <see cref="IPageRenderer"/> on a background task. Every <see cref="Request"/> cancels the previous
/// render, waits the debounce, builds the plan with <see cref="IPlanBuilder"/> and renders the three pages one by one.
/// Errors become a status line; nothing here throws on the UI thread.
/// </summary>
public sealed partial class PreviewStripViewModel : ObservableObject, IDisposable
{
    /// <summary>Thumbnails are rendered at this DPI (1112 x 770 px), enough for a strip and fast on any machine.</summary>
    public const int DefaultPreviewDpi = 72;

    /// <summary>The PRD's 300 ms pause after the last change before rendering.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IPlanBuilder _plans;
    private readonly IPageRenderer _renderer;
    private readonly Manifest _manifest;
    private readonly IDateEngine _dates;
    private readonly IUiThread _ui;
    private readonly TimeSpan _debounce;
    private readonly int _previewDpi;
    private CancellationTokenSource? _cts;
    private int _generation;

    [ObservableProperty]
    private string? _status;

    public PreviewStripViewModel(
        IPlanBuilder plans,
        IPageRenderer renderer,
        Manifest manifest,
        IDateEngine dates,
        IUiThread ui,
        TimeSpan? debounce = null,
        int previewDpi = DefaultPreviewDpi)
    {
        _plans = plans;
        _renderer = renderer;
        _manifest = manifest;
        _dates = dates;
        _ui = ui;
        _debounce = debounce ?? DefaultDebounce;
        _previewDpi = previewDpi;
        Thumbnails = new[] { Day, CalendarMonth, Cover };
        Pending = Task.CompletedTask;
    }

    public PreviewThumbnail Day { get; } = new("First daily page");

    public PreviewThumbnail CalendarMonth { get; } = new("First calendar month");

    public PreviewThumbnail Cover { get; } = new("Cover");

    public IReadOnlyList<PreviewThumbnail> Thumbnails { get; }

    /// <summary>The task of the latest request, for tests and for a clean shutdown.</summary>
    public Task Pending { get; private set; }

    /// <summary>Number of renders started so far (each request that reached the render stage).</summary>
    public int RenderCount { get; private set; }

    /// <summary>Re-renders the three thumbnails for the options after the debounce, cancelling any render in flight.</summary>
    public void Request(GenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        CancellationToken token = Restart();
        Status = null;
        foreach (PreviewThumbnail thumbnail in Thumbnails)
        {
            thumbnail.BeginRendering();
        }

        Pending = RenderAsync(options, token);
    }

    /// <summary>Cancels any render in flight and shows one placeholder line in every slot.</summary>
    public void Clear(string placeholder)
    {
        Restart();
        Status = null;
        foreach (PreviewThumbnail thumbnail in Thumbnails)
        {
            thumbnail.ShowPlaceholder(placeholder);
        }

        Pending = Task.CompletedTask;
    }

    /// <summary>The pages the strip shows, in slot order: first in-quarter daily page, first calendar month, cover (null when the plan lacks one).</summary>
    public static (PageSpec? Day, PageSpec? Calendar, PageSpec? Cover) Pick(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PageSpec? day = plan.Pages.FirstOrDefault(p => p.Type == PageType.Day && (p.Date is null || plan.Quarter.Contains(p.Date.Value)));
        PageSpec? calendar = plan.Pages.FirstOrDefault(p => p.Type == PageType.Calendar);
        PageSpec? cover = plan.Pages.FirstOrDefault(p => p.Type == PageType.Cover);
        return (day, calendar, cover);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private CancellationToken Restart()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _generation++;
        return _cts.Token;
    }

    private async Task RenderAsync(GenerateOptions options, CancellationToken token)
    {
        int generation = _generation;
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce, token);
            }

            RenderCount++;
            NotebookPlan plan = await Task.Run(() => _plans.Build(_manifest, _dates.Build(options.Start), options), token);
            (PageSpec? day, PageSpec? calendar, PageSpec? cover) = Pick(plan);
            RenderOptions renderOptions = new(options.PdfPath, _previewDpi, options);
            foreach ((PreviewThumbnail thumbnail, PageSpec? page) in new[] { (Day, day), (CalendarMonth, calendar), (Cover, cover) })
            {
                token.ThrowIfCancellationRequested();
                if (page is null)
                {
                    Post(generation, () => thumbnail.ShowError("The plan has no such page."));
                    continue;
                }

                RenderedPage rendered = await Task.Run(() => _renderer.Render(page, plan, renderOptions), token);
                token.ThrowIfCancellationRequested();
                Post(generation, () => thumbnail.ShowImage(page.Title, rendered.Png, rendered.WidthPx, rendered.HeightPx));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer request superseded this one; its own render reports the outcome.
        }
        catch (Exception ex)
        {
            // Whatever the plan builder or renderer threw is shown as a line; the strip never crashes the app.
            string message = "Preview failed: " + ex.Message;
            Post(generation, () =>
            {
                Status = message;
                foreach (PreviewThumbnail thumbnail in Thumbnails)
                {
                    thumbnail.ShowError(message);
                }
            });
        }
    }

    private void Post(int generation, Action action) => _ui.Post(() =>
    {
        if (generation == _generation)
        {
            action();
        }
    });
}
