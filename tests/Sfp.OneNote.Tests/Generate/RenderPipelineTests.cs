using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>Parallel rendering delivered in page order with a bounded number of pages in flight.</summary>
public class RenderPipelineTests
{
    private static IReadOnlyList<PageSpec> Pages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new PageSpec(new PageKey("page:" + i), PageType.Notes, "Page " + i, 1, i + 1, null, new Dictionary<string, string>(StringComparer.Ordinal)))
            .ToList();

    private static RenderedPage Render(PageSpec page) => new(page.Key, MonJan4.Png, 1, 1, Array.Empty<AnchorPlacement>());

    [Fact]
    public void Pages_arrive_in_order_whatever_the_render_timing()
    {
        IReadOnlyList<PageSpec> pages = Pages(24);
        List<int> rendered = new();
        object gate = new();
        using RenderPipeline pipeline = new(
            pages,
            page =>
            {
                int i = int.Parse(page.Key.Value.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
                Thread.Sleep((i * 7) % 5);
                lock (gate)
                {
                    rendered.Add(i);
                }

                return Render(page);
            },
            maxDegreeOfParallelism: 4,
            capacity: 4,
            CancellationToken.None);

        List<string> keys = pipeline.Pages().Select(p => p.Key.Value).ToList();

        keys.Should().Equal(pages.Select(p => p.Key.Value));
        rendered.Should().HaveCount(24);
        pipeline.RenderedCount.Should().Be(24);
    }

    [Fact]
    public void At_most_capacity_pages_are_in_flight_and_several_workers_run()
    {
        IReadOnlyList<PageSpec> pages = Pages(16);
        int inFlight = 0;
        int maxInFlight = 0;
        HashSet<int> threads = new();
        object gate = new();
        using RenderPipeline pipeline = new(
            pages,
            page =>
            {
                lock (gate)
                {
                    inFlight++;
                    maxInFlight = Math.Max(maxInFlight, inFlight);
                    threads.Add(Environment.CurrentManagedThreadId);
                }

                Thread.Sleep(5);
                lock (gate)
                {
                    inFlight--;
                }

                return Render(page);
            },
            maxDegreeOfParallelism: 4,
            capacity: 2,
            CancellationToken.None);

        int consumed = 0;
        foreach (RenderedPage _ in pipeline.Pages())
        {
            consumed++;
            Thread.Sleep(3);
        }

        consumed.Should().Be(16);
        maxInFlight.Should().BeLessThanOrEqualTo(2, "the capacity bounds the pages held, rendering ones included");
        maxInFlight.Should().BeGreaterThan(1, "two slots allow two renders at once");
        threads.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Rendered_callback_counts_pages_as_they_finish()
    {
        IReadOnlyList<PageSpec> pages = Pages(5);
        List<int> counts = new();
        using RenderPipeline pipeline = new(pages, Render, 2, 2, CancellationToken.None, (_, count) =>
        {
            lock (counts)
            {
                counts.Add(count);
            }
        });

        pipeline.Pages().Count().Should().Be(5);

        counts.OrderBy(c => c).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void A_render_failure_surfaces_to_the_consumer_naming_the_page_after_the_pages_before_it()
    {
        IReadOnlyList<PageSpec> pages = Pages(6);
        using RenderPipeline pipeline = new(
            pages,
            page => page.Key.Value == "page:3" ? throw new InvalidOperationException("PDFium gave up") : Render(page),
            maxDegreeOfParallelism: 1,
            capacity: 1,
            CancellationToken.None);

        List<string> delivered = new();
        Action act = () =>
        {
            foreach (RenderedPage page in pipeline.Pages())
            {
                delivered.Add(page.Key.Value);
            }
        };

        RenderFailedException ex = act.Should().Throw<RenderFailedException>().Which;
        ex.Page!.Key.Value.Should().Be("page:3");
        ex.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("PDFium gave up");
        ex.Message.Should().Contain("Page 3");
        delivered.Should().Equal("page:0", "page:1", "page:2");
    }

    [Fact]
    public void A_failure_on_a_later_page_stops_the_workers_even_when_earlier_pages_are_still_pending()
    {
        IReadOnlyList<PageSpec> pages = Pages(8);
        int renders = 0;
        using RenderPipeline pipeline = new(
            pages,
            page =>
            {
                Interlocked.Increment(ref renders);
                if (page.Key.Value == "page:1")
                {
                    throw new InvalidOperationException("boom");
                }

                Thread.Sleep(20);
                return Render(page);
            },
            maxDegreeOfParallelism: 4,
            capacity: 4,
            CancellationToken.None);

        Action act = () => pipeline.Pages().ToList();

        act.Should().Throw<RenderFailedException>().Which.Page!.Key.Value.Should().Be("page:1");
        renders.Should().BeLessThan(8, "the workers stop after the failure instead of rendering every page");
    }

    [Fact]
    public void Cancellation_stops_the_consumer_between_pages()
    {
        IReadOnlyList<PageSpec> pages = Pages(10);
        using CancellationTokenSource cts = new();
        using RenderPipeline pipeline = new(pages, page => { Thread.Sleep(5); return Render(page); }, 2, 2, cts.Token);

        List<string> delivered = new();
        Action act = () =>
        {
            foreach (RenderedPage page in pipeline.Pages())
            {
                delivered.Add(page.Key.Value);
                if (delivered.Count == 3)
                {
                    cts.Cancel();
                }
            }
        };

        act.Should().Throw<OperationCanceledException>();
        delivered.Should().HaveCountLessThanOrEqualTo(4);
    }

    [Fact]
    public void An_empty_page_list_yields_nothing_and_disposal_is_idempotent()
    {
        RenderPipeline pipeline = new(Pages(0), Render, 4, 4, CancellationToken.None);
        pipeline.Pages().Should().BeEmpty();
        pipeline.Dispose();
        pipeline.Dispose();
        Action act = () => pipeline.Pages().ToList();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Action degree = () => new RenderPipeline(Pages(1), Render, 0, 1, CancellationToken.None);
        Action capacity = () => new RenderPipeline(Pages(1), Render, 1, 0, CancellationToken.None);
        degree.Should().Throw<ArgumentOutOfRangeException>();
        capacity.Should().Throw<ArgumentOutOfRangeException>();
    }
}
