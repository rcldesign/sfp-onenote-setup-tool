using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render.Pages;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.7: the Time Blocks column rebuilt for the configured window.</summary>
[Collection(RenderCollection.Name)]
public sealed class TimeBlockColumnTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private readonly RenderFixture f;

    public TimeBlockColumnTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private TimeBlocksSlot Slot => f.Manifest.Slots.TimeBlocks;

    [SkippableFact]
    public void Window_6_to_22_labels_all_17_rules_6_through_10_in_order()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        TimeBlocksSlot t = Slot;
        double pitch = t.PitchFor(16);

        // 16 rows have 17 rules, and every rule carries its hour as the printed planner labels all 25 of its rules:
        // the closing rule reads 10 (22:00), the end of the window.
        string[] expected = ["6", "7", "8", "9", "10", "11", "12", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];

        // Each row crop stops short of the rules at columnX and is 16 pt tall so the OCR helper hands it to Tesseract
        // unscaled (a lone 9 pt digit is read reliably in line mode only at its native pixels; word mode turns "11"
        // into "ll"). The whole label column as one block is read too.
        List<string> read = new();
        for (int row = 0; row <= 16; row++)
        {
            double y = t.Top + (row * pitch);
            RectPt label = new(t.LabelRightX - 16, y - 8, 17, 16);
            string line = RenderFixture.Digits(RenderFixture.Read(sheet, label, paddingPt: 0, layout: OcrLayout.SingleLine));
            read.Add(line == expected[row] ? line : RenderFixture.Digits(RenderFixture.Read(sheet, label, paddingPt: 0, layout: OcrLayout.SingleWord)));
        }

        read.Should().Equal(expected);

        RectPt column = new(t.LabelRightX - 16, t.Top - 6, 18, t.Bottom - t.Top + 12);
        RenderFixture.Read(sheet, column, paddingPt: 0, layout: OcrLayout.Block).Should().Be(string.Join(' ', expected));
    }

    [Fact]
    public void Column_is_erased_and_rebuilt_with_rules_at_the_row_tops_and_a_closing_rule()
    {
        using SKBitmap sheet = RenderFixture.Decode(f.Render(MonJan4));
        TimeBlocksSlot t = Slot;
        double pitch = t.PitchFor(16);
        double probeX = t.ColumnX + 50;

        for (int row = 0; row <= 16; row++)
        {
            double y = t.Top + (row * pitch);
            f.HasInk(sheet, new RectPt(probeX, y - 0.6, 2, 1.2)).Should().BeTrue($"rule {row} at y {y:0.0}");
            if (row < 16)
            {
                f.HasInk(sheet, new RectPt(probeX, y + 3, 2, pitch - 6)).Should().BeFalse($"nothing between rule {row} and the next (the printed 22.6 pt rules are gone)");
            }
        }

        Pixels.Mean(sheet, new RectPt(t.Erase.X + 1, t.Erase.Y + 1, t.Erase.W - 2, t.Top - t.Erase.Y - 6), RenderFixture.Dpi).Should().Be(f.Background, "above the first rule is bare sheet");
        f.HasInk(sheet, new RectPt(t.LabelRightX - 14, t.Top - 5, 14, 10)).Should().BeTrue("the first label straddles its rule");
        for (int row = 1; row <= 16; row++)
        {
            double y = t.Top + (row * pitch);
            f.HasInk(sheet, new RectPt(t.LabelRightX - 14, y - 5, 14, 10)).Should().BeTrue($"rule {row} carries its hour label, the closing rule included");
            f.HasInk(sheet, new RectPt(t.LabelRightX - 14, y - (pitch / 2) - 3, 14, 6)).Should().BeFalse($"nothing beside the middle of row {row}");
        }

        Pixels.Mean(sheet, new RectPt(t.Erase.X + 1, t.Bottom + 6, t.Erase.W - 2, t.Erase.Bottom - t.Bottom - 7), RenderFixture.Dpi).Should().Be(f.Background, "below the closing label is bare sheet");
        f.HasInk(sheet, new RectPt(t.LabelRightX + 1, t.Top - 5, t.ColumnX - t.LabelRightX - 2, 10)).Should().BeFalse("labels end at the label right edge, before the rules start");
        (t.Bottom - t.Top).Should().BeApproximately(16 * pitch, 0.001);
    }

    [Fact]
    public void One_event_slot_per_row_plus_the_all_day_band()
    {
        RenderedPage page = f.Render(MonJan4);
        TimeBlocksSlot t = Slot;
        double pitch = t.PitchFor(16);

        IReadOnlyList<AnchorPlacement> rows = page.Anchors.Where(a => a.AnchorId.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).ToList();
        rows.Select(a => a.AnchorId).Should().Equal(Enumerable.Range(6, 16).Select(h => "timeBlockEvent:" + h));
        rows.Should().OnlyContain(a => a.Kind == AnchorKind.Propagated && a.X == (float)t.EventTextX && a.SizePt == (float)t.EventSizePt);
        rows[0].Y.Should().BeApproximately((float)t.Top, 0.01f);
        rows[1].Y.Should().BeApproximately((float)(t.Top + pitch), 0.01f);
        rows[0].H.Should().BeApproximately((float)pitch, 0.01f);
        rows[0].W.Should().BeApproximately((float)(t.ColumnRight - t.EventTextX), 0.01f);
        rows[0].Opacity.Should().Be((float)f.Manifest.Colors.EventInkOpacity);

        AnchorPlacement band = page.Anchors.Single(a => a.AnchorId == "allDayBand");
        band.Kind.Should().Be(AnchorKind.Propagated);
        band.X.Should().Be((float)f.Manifest.Slots.AllDayBand.Rect.X);
    }

    [Fact]
    public void A_full_day_window_gives_24_rows_at_the_printed_pitch()
    {
        RenderedPage page = f.Render(MonJan4, o => o with { TimeBlocks = new TimeBlockWindow(0, 24) });
        using SKBitmap sheet = RenderFixture.Decode(page);
        TimeBlocksSlot t = Slot;

        page.Anchors.Count(a => a.AnchorId.StartsWith("timeBlockEvent:", StringComparison.Ordinal)).Should().Be(24);
        page.Anchors.Should().Contain(a => a.AnchorId == "timeBlockEvent:0").And.Contain(a => a.AnchorId == "timeBlockEvent:23");
        double pitch = t.PitchFor(24);
        f.HasInk(sheet, new RectPt(t.ColumnX + 50, t.Top + pitch - 0.6, 2, 1.2)).Should().BeTrue();
        f.HasInk(sheet, new RectPt(t.LabelRightX - 14, t.Bottom - 4, 14, 8)).Should().BeTrue("25 labels for 24 rows, as printed: the closing rule reads 12");
    }

    [Theory]
    [InlineData(0, "12")]
    [InlineData(6, "6")]
    [InlineData(11, "11")]
    [InlineData(12, "12")]
    [InlineData(13, "1")]
    [InlineData(21, "9")]
    [InlineData(22, "10")]
    [InlineData(23, "11")]
    [InlineData(24, "12")]
    public void Hour_labels_follow_the_printed_12_hour_clock(int hour, string label)
    {
        TimeBlockColumn.HourLabel(hour).Should().Be(label);
    }

    [Fact]
    public void An_invalid_window_is_refused()
    {
        Action act = () => f.Render(MonJan4, o => o with { TimeBlocks = new TimeBlockWindow(9, 9) });
        act.Should().Throw<InvalidOperationException>().WithMessage("*window*");
    }
}
