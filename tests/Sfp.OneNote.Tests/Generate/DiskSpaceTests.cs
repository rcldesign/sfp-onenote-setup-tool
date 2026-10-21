using Sfp.Core.Options;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>The PRD's "Disk full" row: recognizing a full disk by its code and estimating the space a run needs.</summary>
public class DiskSpaceTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80070027))]
    [InlineData(28)]
    public void An_io_exception_carrying_a_disk_full_code_is_recognized_also_when_wrapped(int hresult)
    {
        IOException io = new("No space left on device", hresult);

        DiskSpace.IsDiskFull(io).Should().BeTrue();
        DiskSpace.IsDiskFull(new RenderFailedException("Rendering page 'Cover' failed", io)).Should().BeTrue();
        DiskSpace.IsDiskFull(new AggregateException(new InvalidOperationException("another worker failed"), io)).Should().BeTrue();
    }

    [Fact]
    public void A_onenote_call_failing_with_the_win32_disk_full_hresult_is_recognized()
    {
        DiskSpace.IsDiskFull(new OneNoteException("UpdatePageContent", DiskSpace.ErrorDiskFull, "OneNote call UpdatePageContent failed: 0x80070070", null)).Should().BeTrue();
        DiskSpace.IsDiskFull(new OneNoteException("UpdatePageContent", DiskSpace.ErrorHandleDiskFull, "OneNote call UpdatePageContent failed: 0x80070027", null)).Should().BeTrue();
    }

    [Fact]
    public void Other_failures_are_not_a_full_disk()
    {
        DiskSpace.IsDiskFull(null).Should().BeFalse();
        DiskSpace.IsDiskFull(new IOException("There is not enough space on the disk.")).Should().BeFalse("the code decides, never the localized text");
        DiskSpace.IsDiskFull(new IOException("The process cannot access the file.", unchecked((int)0x80070020))).Should().BeFalse();
        DiskSpace.IsDiskFull(new UnauthorizedAccessException("Access is denied.")).Should().BeFalse();
        DiskSpace.IsDiskFull(new OneNoteException("UpdatePageContent", 28, "not an HRESULT", null)).Should().BeFalse("errno 28 only counts on an IOException");
        DiskSpace.IsDiskFull(new OneNoteException("UpdatePageContent", OneNoteHResults.InvalidXml, "rejected", null)).Should().BeFalse();
        DiskSpace.IsDiskFull(new RenderFailedException("Rendering page 'Cover' failed", new InvalidOperationException("PDFium"))).Should().BeFalse();
    }

    [Theory]
    [InlineData(144, 220, 44236800L, 42L)]
    [InlineData(144, 300, 76677120L, 73L)]
    [InlineData(144, 144, 20643840L, 20L)]
    [InlineData(7, 220, 2150400L, 2L)]
    [InlineData(1, 220, 307200L, 1L)]
    [InlineData(0, 220, 0L, 1L)]
    public void The_estimate_is_140_300_or_520_kb_per_page_at_144_220_or_300_dpi(int pages, int dpi, long bytes, long megabytes)
    {
        DiskSpace.EstimatedBytes(pages, dpi).Should().Be(bytes);
        DiskSpace.EstimatedMegabytes(pages, dpi).Should().Be(megabytes, "whole megabytes, rounded, never 0");
    }

    [Fact]
    public void The_estimate_agrees_with_the_app_size_line_at_every_allowed_dpi()
    {
        NotebookSizeEstimate.BytesPerPage.Keys.Should().BeEquivalentTo(GenerateOptions.AllowedDpis, "every DPI the CLI and the app accept has its figure in the one table");
        foreach (int dpi in GenerateOptions.AllowedDpis)
        {
            DiskSpace.BytesPerPage(dpi).Should().Be(NotebookSizeEstimate.BytesPerPage[dpi], "at {0} DPI", dpi);
            foreach (int pages in new[] { 0, 1, 7, 132, 144, 152 })
            {
                DiskSpace.EstimatedBytes(pages, dpi).Should().Be(NotebookSizeEstimate.Bytes(dpi, pages), "{0} pages at {1} DPI", pages, dpi);
            }

            foreach (int pages in new[] { 132, 144, 152 })
            {
                long megabytes = DiskSpace.EstimatedMegabytes(pages, dpi);
                DiskSpace.Message("/x", pages, dpi).Should().Contain(" about " + megabytes + " MB is needed for " + pages + " pages at " + dpi + " DPI.");
                NotebookSizeEstimate.Describe(dpi, pages).Should().StartWith("About " + megabytes + " MB for " + pages + " pages at " + dpi + " DPI", "the disk-full message and the size line name the same size");
            }
        }
    }

    [Theory]
    [InlineData(72, 35840L)]
    [InlineData(150, 155556L)]
    [InlineData(182, 229006L)]
    [InlineData(200, 253885L)]
    [InlineData(260, 429065L)]
    [InlineData(400, 946632L)]
    public void A_dpi_outside_the_table_scales_the_nearest_entry_by_the_dpi_squared(int dpi, long bytes)
    {
        // 72 is a quarter of 140 KB (144 DPI); 182 and 260 sit halfway between two entries and take the lower one;
        // 400 scales 520 KB (300 DPI) by 16/9.
        DiskSpace.BytesPerPage(dpi).Should().Be(bytes);
        DiskSpace.EstimatedBytes(3, dpi).Should().Be(3 * bytes);
    }

    [Fact]
    public void The_message_names_the_folder_the_space_the_pages_and_the_dpi()
    {
        DiskSpace.Message(@"D:\Notebooks", 144, 220).Should().Be(@"Not enough disk space in D:\Notebooks: about 42 MB is needed for 144 pages at 220 DPI.");
        DiskSpace.Message("/png", 144, 300).Should().Be("Not enough disk space in /png: about 73 MB is needed for 144 pages at 300 DPI.");
        DiskSpace.Message("/png", 144, 144).Should().Be("Not enough disk space in /png: about 20 MB is needed for 144 pages at 144 DPI.");
    }

    [Fact]
    public void Negative_pages_or_a_dpi_below_one_are_refused()
    {
        Action pages = () => DiskSpace.EstimatedBytes(-1, 220);
        Action dpi = () => DiskSpace.EstimatedBytes(144, 0);
        Action perPage = () => DiskSpace.BytesPerPage(-220);

        pages.Should().Throw<ArgumentOutOfRangeException>();
        dpi.Should().Throw<ArgumentOutOfRangeException>();
        perPage.Should().Throw<ArgumentOutOfRangeException>();
    }
}
