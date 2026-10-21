using System.Collections.Concurrent;
using System.Diagnostics;
using Sfp.TestSupport;

namespace Sfp.Render.Tests;

/// <summary>
/// M2.10: the golden suite. Every page of Q1 2027 is rendered at 220 DPI from the synthetic template (and from the real
/// planner when SFP_PDF_PATH is set), hashed, and compared with golden/&lt;fixture&gt;.&lt;rid&gt;.sha256; a mismatch lists
/// the differing keys. The full render is timed, and a second full render must give the same hashes.
/// </summary>
[Collection(RenderCollection.Name)]
public sealed class GoldenSuiteTests
{
    /// <summary>Wall time a full synthetic render may take on the 4-core build box (the card asks for under 60 s).</summary>
    public const double BudgetSeconds = 60;

    private readonly RenderFixture f;

    public GoldenSuiteTests(RenderFixture fixture)
    {
        f = fixture;
    }

    [SkippableFact]
    public void Every_synthetic_page_matches_its_golden_hash_and_the_render_fits_the_budget()
    {
        (IReadOnlyDictionary<string, string> hashes, TimeSpan elapsed) = RenderAll(f.Pdf);
        hashes.Should().HaveCount(144);
        hashes.Should().ContainKey("contents").WhoseValue.Should().Be(Goldens.Sha256(Array.Empty<byte>()), "a page without an image hashes its empty PNG");

        CompareOrRecord(Goldens.SyntheticFixture, hashes);
        elapsed.TotalSeconds.Should().BeLessThan(BudgetSeconds, "144 pages at 220 DPI on four cores; measured {0:0.0} s", elapsed.TotalSeconds);
    }

    [Fact]
    public void A_second_full_render_gives_identical_hashes()
    {
        (IReadOnlyDictionary<string, string> first, _) = RenderAll(f.Pdf);
        (IReadOnlyDictionary<string, string> second, _) = RenderAll(f.Pdf);
        Goldens.Differences(first, second).Should().BeEmpty();
    }

    [SkippableFact]
    public void Every_real_planner_page_matches_its_golden_hash()
    {
        string pdf = SfpPdf.RequireOrSkip();
        (IReadOnlyDictionary<string, string> hashes, _) = RenderAll(pdf);
        hashes.Should().HaveCount(144);
        CompareOrRecord(Goldens.SfpFixture, hashes);
    }

    [Fact]
    public void Golden_files_round_trip_and_report_differences_by_key()
    {
        string dir = TestPaths.NewTempDir("golden");
        try
        {
            string path = Path.Combine(dir, "x.sha256");
            Dictionary<string, string> hashes = new() { ["day:2027-01-04"] = new string('a', 64), ["cover"] = new string('b', 64) };
            Goldens.Write(path, hashes);
            File.ReadAllText(path).Should().Be(new string('b', 64) + "  cover\n" + new string('a', 64) + "  day:2027-01-04\n");
            Goldens.Read(path).Should().Equal(hashes);

            Dictionary<string, string> changed = new() { ["day:2027-01-04"] = new string('c', 64), ["setup"] = new string('d', 64) };
            Goldens.Differences(hashes, changed).Should().Equal(
                "cover: expected bbbbbbbbbbbbbbbb, got (missing)",
                "day:2027-01-04: expected aaaaaaaaaaaaaaaa, got cccccccccccccccc",
                "setup: expected (missing), got dddddddddddddddd");
            Goldens.Rid.Should().MatchRegex("^(win|linux|osx)-[a-z0-9]+$");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void CompareOrRecord(string fixture, IReadOnlyDictionary<string, string> hashes)
    {
        string path = Goldens.PathFor(fixture);
        if (Goldens.Recording)
        {
            Goldens.Write(path, hashes);
            return;
        }

        Skip.If(!File.Exists(path), $"No golden file {Path.GetFileName(path)} for this platform; record one with {Goldens.RecordEnvVar}=1.");
        IReadOnlyList<string> differences = Goldens.Differences(Goldens.Read(path), hashes);
        differences.Should().BeEmpty("every page must match " + Path.GetFileName(path) + "; differing keys:\n" + string.Join("\n", differences));
    }

    /// <summary>
    /// Renders every page of Q1 2027 at 220 DPI with the product's default worker count (at most four pages in flight,
    /// as render-only and generate run) and hashes the PNG bytes. The shared rasterizer's page cache is cleared
    /// afterwards: the last pages of a full render are of no use to the tests that follow.
    /// </summary>
    private (IReadOnlyDictionary<string, string> Hashes, TimeSpan Elapsed) RenderAll(string pdf)
    {
        RenderOptions options = new(pdf, RenderFixture.Dpi, f.Q1Options);
        ConcurrentDictionary<string, string> hashes = new(StringComparer.Ordinal);
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            Parallel.ForEach(f.Q1Plan.Pages, new ParallelOptions { MaxDegreeOfParallelism = RenderParallelism.Default }, page =>
            {
                RenderedPage art = f.Renderer.Render(page, f.Q1Plan, options);
                hashes[page.Key.Value] = Goldens.Sha256(art.Png);
            });
            return (hashes, clock.Elapsed);
        }
        finally
        {
            f.Rasterizer.ClearCache();
        }
    }
}
