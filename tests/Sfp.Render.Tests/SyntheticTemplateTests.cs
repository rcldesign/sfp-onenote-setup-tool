using Sfp.Core.Manifests;
using Sfp.TestSupport;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Sfp.Render.Tests;

/// <summary>The shared synthetic planner fixture must look like the real PDF to the manifest validator: page count, page size, probes.</summary>
public class SyntheticTemplateTests
{
    private static readonly Manifest M = TestManifest.Shipped;

    [Fact]
    public void Has_the_manifest_page_count_and_page_size()
    {
        using PdfDocument doc = PdfDocument.Open(SyntheticTemplate.Build());
        doc.NumberOfPages.Should().Be(M.PageCount);
        foreach (int number in new[] { 1, 13, 17, 132 })
        {
            Page page = doc.GetPage(number);
            page.Width.Should().BeApproximately(M.PageWidthPt, 0.5);
            page.Height.Should().BeApproximately(M.PageHeightPt, 0.5);
        }
    }

    [Theory]
    [InlineData(1, "Structured Focus Planner")]
    [InlineData(2, "Contents")]
    [InlineData(3, "Annual Objectives")]
    [InlineData(4, "Annual Objectives")]
    [InlineData(5, "Objective Details")]
    [InlineData(12, "Objective Details")]
    [InlineData(13, "Monthly Calendar")]
    [InlineData(15, "Monthly Calendar")]
    [InlineData(16, "Weekly Overview")]
    [InlineData(17, "Monday")]
    [InlineData(23, "Sunday")]
    [InlineData(24, "Weekly Reflection")]
    [InlineData(124, "Weekly Overview")]
    [InlineData(131, "Sunday")]
    [InlineData(132, "Weekly Reflection")]
    public void Each_page_type_carries_its_probe_word(int pdfPage, string probe)
    {
        using PdfDocument doc = PdfDocument.Open(SyntheticTemplate.Build());
        doc.GetPage(pdfPage).Text.Should().Contain(probe);
    }

    [Fact]
    public void Every_week_run_has_its_nine_pages_in_manifest_order()
    {
        WeeklyPagesSpec weekly = M.PageTypes.Weekly;
        using PdfDocument doc = PdfDocument.Open(SyntheticTemplate.Build());
        for (int run = 1; run <= weekly.Count; run++)
        {
            foreach (string name in weekly.Pages)
            {
                int pdfPage = weekly.PdfPageFor(run, name);
                doc.GetPage(pdfPage).Text.Should().Contain(weekly.Probes[name], $"page {pdfPage} is {name} of run {run}");
            }
        }
    }

    [Fact]
    public void Daily_pages_carry_the_strip_label_and_the_thirteen_numbers()
    {
        using PdfDocument doc = PdfDocument.Open(SyntheticTemplate.Build());
        string text = doc.GetPage(17).Text;
        text.Should().Contain("Weeks Remaining In Quarter");
        text.Should().Contain("Daily Goals").And.Contain("Plan").And.Contain("Time Blocks");
        IEnumerable<string> words = doc.GetPage(17).GetWords().Select(w => w.Text);
        words.Should().Contain(Enumerable.Range(1, 13).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Build_is_cached_per_process_at_a_temp_path()
    {
        string first = SyntheticTemplate.Build();
        string second = SyntheticTemplate.Build();
        second.Should().Be(first);
        File.Exists(first).Should().BeTrue();
        first.Should().StartWith(Path.GetTempPath());
    }

    [Fact]
    public void Bytes_can_be_built_for_a_manifest_variant()
    {
        Manifest shorter = M with { PageCount = 24 };
        byte[] pdf = SyntheticTemplate.BuildBytes(shorter);
        using PdfDocument doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.Should().Be(24);
    }
}
