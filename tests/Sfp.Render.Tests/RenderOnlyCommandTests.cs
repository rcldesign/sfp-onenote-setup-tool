using System.CommandLine;
using System.Globalization;
using Sfp.Cli;
using Sfp.Cli.Verbs;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Render.Colors;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.10: the render-only verb through TextWriters on the synthetic template, and its option validation.</summary>
public sealed class RenderOnlyCommandTests
{
    /// <summary>Set SFP_RENDER_OUT to keep the verb's output folder (the reviewer screenshot set) instead of a deleted temp folder.</summary>
    public const string KeepEnvVar = "SFP_RENDER_OUT";

    private static (int Code, string Out, string Err) Run(RenderOnlyRequest request)
    {
        using StringWriter stdout = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using StringWriter stderr = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        int code = RenderOnlyCommand.Run(request, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static RenderOnlyRequest Request(string outDir, string logDir) =>
        new(SyntheticTemplate.Build(), "2027-01", outDir, Dpi: 144, TemplatesDir: TestPaths.TemplatesDir, LogDirectory: logDir);

    [Fact]
    public void Renders_the_synthetic_quarter_to_pngs_and_an_index()
    {
        string? keep = Environment.GetEnvironmentVariable(KeepEnvVar);
        string dir = string.IsNullOrWhiteSpace(keep) ? TestPaths.NewTempDir("render-only") : keep;
        string logs = TestPaths.NewTempDir("render-only-logs");
        try
        {
            (int code, string output, string err) = Run(Request(dir, logs));

            err.Should().BeEmpty();
            code.Should().Be(ExitCodes.Success);
            output.Should().Contain("Rendered 144 pages of SFP Q1 2027: 142 PNG files and pages.json written to ");
            output.Should().Contain("Warning: Font 'Bookman Old Style' is not installed", "the build box has no Bookman");
            output.Should().Contain("Log: ");
            Directory.GetFiles(dir, "*.png").Should().HaveCount(142);
            RenderIndex index = RenderIndex.Parse(File.ReadAllText(Path.Combine(dir, "pages.json")));
            index.Pages.Should().HaveCount(144);
            index.Dpi.Should().Be(144);
            index.Pages.Single(p => p.Key == "day:2027-01-04").Width.Should().Be(2224);
            string log = Directory.GetFiles(logs, "*render-only.log").Should().ContainSingle().Subject;
            File.ReadAllText(log).Should().Contain("Rendered 144/144");
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(keep))
            {
                Directory.Delete(dir, recursive: true);
            }

            Directory.Delete(logs, recursive: true);
        }
    }

    [Fact]
    public void Quiet_prints_nothing_on_success()
    {
        string dir = TestPaths.NewTempDir("render-only-quiet");
        string logs = TestPaths.NewTempDir("render-only-quiet-logs");
        try
        {
            (int code, string output, string err) = Run(Request(dir, logs) with { Quiet = true, NoLinks = true, TimeBlocks = "8-18", DateFormat = "d/m/yy", WeeksRemaining = "as-printed", IncludeOutOfQuarterDays = true });

            code.Should().Be(ExitCodes.Success);
            output.Should().BeEmpty();
            err.Should().BeEmpty();
            RenderIndex index = RenderIndex.Parse(File.ReadAllText(Path.Combine(dir, "pages.json")));
            index.Pages.Should().HaveCount(144 + 8, "out-of-quarter days were included");
            RenderIndexPage jan4 = index.Pages.Single(p => p.Key == "day:2027-01-04");
            jan4.Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link);
            jan4.Anchors.Count(a => a.AnchorId.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).Should().Be(10);

            // --no-links bakes the labels that would otherwise be links, so pages printed from the same art still differ.
            File.ReadAllBytes(Path.Combine(dir, "annual-1.png")).Should().NotEqual(File.ReadAllBytes(Path.Combine(dir, "annual-2.png")), "the block labels name objectives 1 to 4 and 5 to 8");
            File.ReadAllBytes(Path.Combine(dir, "objective-1.png")).Should().NotEqual(File.ReadAllBytes(Path.Combine(dir, "objective-2.png")), "each detail page names its objective");
            using SKBitmap scorecard = SKBitmap.Decode(Path.Combine(dir, "scorecard.png"));
            ScorecardPageSpec s = TestManifest.Shipped.Generated.Scorecard;
            SKColor background = ColorParser.Parse(TestManifest.Shipped.Colors.Background);
            for (int week = 0; week < 14; week++)
            {
                RectPt cell = s.WeekHeader(week);
                Pixels.HasInk(scorecard, new RectPt(cell.X + 2, cell.Y + 1, cell.W - 4, cell.H - 2), 144, background).Should().BeTrue($"week {week + 1:00} is baked in the scorecard header");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(logs, recursive: true);
        }
    }

    [Fact]
    public void A_pdf_that_does_not_match_the_manifest_exits_2_listing_the_mismatches()
    {
        string dir = TestPaths.NewTempDir("render-only-bad");
        try
        {
            string shortPdf = Path.Combine(dir, "short.pdf");
            File.WriteAllBytes(shortPdf, SyntheticTemplate.BuildBytes(TestManifest.Shipped with { PageCount = 24 }));
            (int code, string output, string err) = Run(Request(dir, dir) with { Pdf = shortPdf });

            code.Should().Be(ExitCodes.ValidationError);
            output.Should().BeEmpty();
            err.Should().StartWith("The PDF does not match the manifest sfp-quarterly-2023:\n  Expected 132 pages, found 24\n");
            File.Exists(Path.Combine(dir, "pages.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_page_that_fails_to_render_exits_4_with_the_error_and_writes_no_index()
    {
        // The PDF validates and rasterizes; the user manifest leaves the strip too narrow for 14 numbers, so the first
        // daily page fails while its overlays are drawn (a render error on a page, exit 4 in the PRD's error table).
        string dir = TestPaths.NewTempDir("render-only-fail");
        try
        {
            string manifest = Path.Combine(dir, "narrow-strip.json");
            File.WriteAllText(manifest, """{ "slots": { "weeksRemaining": { "numberSpan": { "from": 537, "to": 560 } } } }""");
            string outDir = Path.Combine(dir, "out");

            (int code, string output, string err) = Run(Request(outDir, dir) with { ManifestPath = manifest });

            code.Should().Be(ExitCodes.RenderError);
            err.Should().StartWith("Render failed: slots.weeksRemaining.numberSpan (537 to 560, 23 pt) is too narrow for 14 numbers at 10 pt");
            err.Should().Contain("\nLog: ");
            output.Should().NotContain("Rendered ", "there is no success summary");
            File.Exists(Path.Combine(outDir, RenderOnlyRunner.IndexFileName)).Should().BeFalse("a failed run writes no pages.json");
            string log = Directory.GetFiles(dir, "*render-only.log").Should().ContainSingle().Subject;
            File.ReadAllText(log).Should().Contain("Render failed").And.Contain("too narrow for 14 numbers");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--dpi", "--dpi must be one of 144, 220, 300; got 200.")]
    [InlineData("--date-format", "--date-format must be one of mm/dd/yy, dd/mm/yy, m/d/yy, d/m/yy; got 'yy-mm-dd'.")]
    [InlineData("--weeks-remaining", "--weeks-remaining must be corrected or as-printed; got 'reversed'.")]
    [InlineData("--time-blocks", "--time-blocks must be START-END in 24-hour hours, for example 6-22; got '6 to 22'.")]
    [InlineData("--time-blocks-order", "--time-blocks window must start at 0 to 23 and end at 1 to 24, after the start; got '22-6'.")]
    [InlineData("--start", "Start month must be 01 to 12; got 13 in '2027-13'.")]
    [InlineData("--pdf", "PDF not found: ")]
    public void A_bad_option_exits_2_with_a_message(string option, string message)
    {
        string dir = TestPaths.NewTempDir("render-only-opts");
        try
        {
            RenderOnlyRequest request = Request(dir, dir);
            request = option switch
            {
                "--dpi" => request with { Dpi = 200 },
                "--date-format" => request with { DateFormat = "yy-mm-dd" },
                "--weeks-remaining" => request with { WeeksRemaining = "reversed" },
                "--time-blocks" => request with { TimeBlocks = "6 to 22" },
                "--time-blocks-order" => request with { TimeBlocks = "22-6" },
                "--start" => request with { Start = "2027-13" },
                _ => request with { Pdf = Path.Combine(dir, "nope.pdf") },
            };
            (int code, string output, string err) = Run(request);

            code.Should().Be(ExitCodes.ValidationError);
            output.Should().BeEmpty();
            err.Should().StartWith(message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Time_block_windows_parse_as_hours()
    {
        RenderOnlyCommand.ParseTimeBlocks("6-22", out string? error).Should().Be(new TimeBlockWindow(6, 22));
        error.Should().BeNull();
        RenderOnlyCommand.ParseTimeBlocks("0-24", out _).Should().Be(new TimeBlockWindow(0, 24));
        RenderOnlyCommand.ParseTimeBlocks("9", out error).Should().BeNull();
        error.Should().Contain("START-END");
        RenderOnlyCommand.ParseTimeBlocks("6-25", out error).Should().BeNull();
        error.Should().Contain("end at 1 to 24");
    }

    [Fact]
    public void A_templates_folder_without_the_manifest_exits_2()
    {
        string dir = TestPaths.NewTempDir("render-only-templates");
        try
        {
            (int code, _, string err) = Run(Request(dir, dir) with { TemplatesDir = dir });
            code.Should().Be(ExitCodes.ValidationError);
            err.Should().Contain("sfp-quarterly.json").And.Contain(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_defines_the_verb_and_its_options()
    {
        Command command = RenderOnlyCommand.Build();

        command.Name.Should().Be("render-only");
        command.Options.Select(o => o.Name).Should().BeEquivalentTo(
            "--pdf", "--start", "--out", "--dpi", "--date-format", "--weeks-remaining", "--no-links", "--time-blocks", "--include-out-of-quarter-days", "--manifest", "--templates", "--quiet");
        command.Options.Where(o => o.Required).Select(o => o.Name).Should().BeEquivalentTo("--pdf", "--start", "--out");
        command.Parse(new[] { "--pdf", "x.pdf", "--start", "2027-01", "--out", "out", "--dpi", "300", "--no-links", "--time-blocks", "7-19" }).Errors.Should().BeEmpty();
        command.Parse(new[] { "--pdf", "x.pdf", "--start", "2027-01" }).Errors.Should().NotBeEmpty("--out is required");
    }
}
