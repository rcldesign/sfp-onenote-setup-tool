using Sfp.Core.Plan;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.10: the parallel render-only runner writes one PNG per page with an image plus pages.json, with progress and cancellation.</summary>
[Collection(RenderCollection.Name)]
public sealed class RenderOnlyRunnerTests
{
    private readonly RenderFixture f;

    public RenderOnlyRunnerTests(RenderFixture fixture)
    {
        f = fixture;
    }

    [Fact]
    public void Writes_a_png_per_page_with_an_image_and_the_index()
    {
        string dir = TestPaths.NewTempDir("runner");
        try
        {
            RenderOnlyRunner runner = new(f.Renderer);
            List<RenderProgress> progress = new();
            RenderOnlyResult result = runner.Run(f.Q1Plan, f.Options(f.Q1Options, dpi: 72), dir, new SynchronousProgress(progress));

            result.PagesRendered.Should().Be(144);
            result.PngFiles.Should().Be(142, "Contents and the blank Objective Details parent have no image");
            Directory.GetFiles(dir, "*.png").Should().HaveCount(142);
            File.Exists(Path.Combine(dir, "day-2027-01-04.png")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "week-02-overview.png")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "contents.png")).Should().BeFalse();
            result.IndexPath.Should().Be(Path.Combine(dir, "pages.json"));
            result.Elapsed.Should().BePositive();

            progress.Should().HaveCount(144);
            progress.Select(p => p.Done).OrderBy(d => d).Should().Equal(Enumerable.Range(1, 144));
            progress.Should().OnlyContain(p => p.Total == 144);

            RenderIndex index = RenderIndex.Parse(File.ReadAllText(result.IndexPath));
            index.Name.Should().Be("SFP Q1 2027");
            index.Quarter.Should().Be("Q1 2027");
            index.Dpi.Should().Be(72);
            index.Pages.Should().HaveCount(144);
            index.Pages.Select(p => p.Key).Should().Equal(f.Q1Plan.Pages.Select(p => p.Key.Value), "pages are listed in notebook order");

            RenderIndexPage jan4 = index.Pages.Single(p => p.Key == "day:2027-01-04");
            jan4.Title.Should().Be("Mon Jan 4");
            jan4.Section.Should().Be("Week 02 (Jan 4 to Jan 10)");
            jan4.Level.Should().Be(2);
            jan4.PdfPage.Should().Be(26);
            jan4.Png.Should().Be("day-2027-01-04.png");
            jan4.Width.Should().Be(1112);
            jan4.Height.Should().Be(770);
            jan4.Anchors.Should().Contain(a => a.AnchorId == "dailyDate" && a.Kind == AnchorKind.Link && a.Text == "01 / 04 / 27");
            jan4.Anchors.Should().Contain(a => a.AnchorId == "standingGoal:1" && a.Kind == AnchorKind.Propagated && a.Opacity == 0.6f);
            jan4.Anchors.Single(a => a.AnchorId == "dailyDate").ToPlacement().Should().Be(f.Render(PageKey.Day(new DateOnly(2027, 1, 4)), dpi: 72).Anchors.Single(a => a.AnchorId == "dailyDate"));

            RenderIndexPage contents = index.Pages.Single(p => p.Key == "contents");
            contents.Png.Should().BeNull();
            contents.Width.Should().Be(0);
            contents.Anchors.Should().BeEmpty();

            using SKBitmap png = SKBitmap.Decode(Path.Combine(dir, "day-2027-01-04.png"));
            png.Width.Should().Be(1112);
            File.ReadAllText(result.IndexPath).Should().Contain("\"anchorId\": \"dailyDate\"").And.Contain("\"kind\": \"link\"").And.Contain("\"align\": \"right\"");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_names_replace_colons_with_dashes()
    {
        RenderOnlyRunner.FileNameFor(PageKey.WeekOverview(2)).Should().Be("week-02-overview.png");
        RenderOnlyRunner.FileNameFor(PageKey.Calendar(2027, 1)).Should().Be("calendar-2027-01.png");
        RenderOnlyRunner.FileNameFor(PageKey.Cover).Should().Be("cover.png");
    }

    [Fact]
    public void A_cancelled_run_stops_without_an_index()
    {
        string dir = TestPaths.NewTempDir("runner-cancel");
        try
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();
            RenderOnlyRunner runner = new(f.Renderer, maxDegreeOfParallelism: 2);
            Action act = () => runner.Run(f.Q1Plan, f.Options(f.Q1Options, dpi: 72), dir, null, cts.Token);

            act.Should().Throw<OperationCanceledException>();
            File.Exists(Path.Combine(dir, "pages.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_render_failure_surfaces_as_the_inner_exception()
    {
        string dir = TestPaths.NewTempDir("runner-fail");
        try
        {
            RenderOnlyRunner runner = new(f.Renderer, maxDegreeOfParallelism: 1);
            Action act = () => runner.Run(f.Q1Plan, new RenderOptions(Path.Combine(dir, "missing.pdf"), 72, f.Q1Options), dir);
            act.Should().Throw<FileNotFoundException>().WithMessage("*missing.pdf*");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("day:2027-01-04", "Rendering Day page 'Mon Jan 4' (PDF page 26) failed: PDFium could not decode the page")]
    [InlineData("scorecard", "Rendering Scorecard page 'Quarter scorecard' (PDF page none) failed: PDFium could not decode the page")]
    public void With_page_failures_named_a_render_failure_names_the_page_in_the_generate_sentence(string key, string sentence)
    {
        string dir = TestPaths.NewTempDir("runner-named");
        try
        {
            InvalidOperationException cause = new("PDFium could not decode the page");
            PageKey failing = new(key);
            RenderOnlyRunner runner = new(new FailingRenderer(failing, cause), maxDegreeOfParallelism: 1) { NamePageFailures = true };
            Action act = () => runner.Run(f.Q1Plan, f.Options(f.Q1Options, dpi: 72), dir);

            PageRenderException ex = act.Should().Throw<PageRenderException>().Which;
            ex.Message.Should().Be(sentence);
            ex.Page.Should().BeSameAs(f.Q1Plan.Page(failing));
            ex.InnerException.Should().BeSameAs(cause, "the renderer's exception is kept for the log");
            File.Exists(Path.Combine(dir, RenderOnlyRunner.IndexFileName)).Should().BeFalse("a failed run writes no pages.json");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void With_page_failures_named_a_cancellation_from_the_renderer_is_not_wrapped()
    {
        string dir = TestPaths.NewTempDir("runner-named-cancel");
        try
        {
            RenderOnlyRunner runner = new(new FailingRenderer(PageKey.Cover, new OperationCanceledException()), maxDegreeOfParallelism: 1) { NamePageFailures = true };
            Action act = () => runner.Run(f.Q1Plan, f.Options(f.Q1Options, dpi: 72), dir);

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parallelism_must_be_positive()
    {
        Action act = () => _ = new RenderOnlyRunner(f.Renderer, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(64, 4)]
    [InlineData(0, 1)]
    public void Default_parallelism_is_the_processor_count_capped_at_four(int processors, int expected)
    {
        RenderParallelism.For(processors).Should().Be(expected);
        RenderParallelism.Default.Should().Be(RenderParallelism.For(Environment.ProcessorCount));
        RenderParallelism.MaxDefault.Should().Be(4);
    }

    [Fact]
    public void The_runner_defaults_to_the_shared_parallelism_and_never_exceeds_it()
    {
        CountingRenderer counting = new();
        RenderOnlyRunner runner = new(counting);
        runner.MaxDegreeOfParallelism.Should().Be(RenderParallelism.Default);
        new RenderOnlyRunner(counting, 2).MaxDegreeOfParallelism.Should().Be(2);

        string dir = TestPaths.NewTempDir("runner-degree");
        try
        {
            runner.Run(f.Q1Plan, f.Options(f.Q1Options, dpi: 72), dir);
            counting.Calls.Should().Be(144);
            counting.MaxConcurrent.Should().BeInRange(1, RenderParallelism.Default);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A renderer that returns imageless pages after a short pause and records how many calls overlapped.</summary>
    private sealed class CountingRenderer : IPageRenderer
    {
        private int current;
        private int max;
        private int calls;

        public int MaxConcurrent => Volatile.Read(ref max);

        public int Calls => Volatile.Read(ref calls);

        public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o)
        {
            int now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, now, seen) != seen)
            {
            }

            Thread.Sleep(2);
            Interlocked.Decrement(ref current);
            Interlocked.Increment(ref calls);
            return new RenderedPage(page.Key, Array.Empty<byte>(), 0, 0, Array.Empty<AnchorPlacement>());
        }
    }

    /// <summary>A renderer that returns imageless pages and throws the given exception for one page.</summary>
    private sealed class FailingRenderer : IPageRenderer
    {
        private readonly PageKey failing;
        private readonly Exception error;

        public FailingRenderer(PageKey failing, Exception error)
        {
            this.failing = failing;
            this.error = error;
        }

        public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o) =>
            page.Key == failing ? throw error : new RenderedPage(page.Key, Array.Empty<byte>(), 0, 0, Array.Empty<AnchorPlacement>());
    }

    /// <summary>Progress that records on the calling thread (Progress&lt;T&gt; would post to a context the test cannot wait on).</summary>
    private sealed class SynchronousProgress : IProgress<RenderProgress>
    {
        private readonly List<RenderProgress> reports;

        public SynchronousProgress(List<RenderProgress> reports)
        {
            this.reports = reports;
        }

        public void Report(RenderProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }
}
