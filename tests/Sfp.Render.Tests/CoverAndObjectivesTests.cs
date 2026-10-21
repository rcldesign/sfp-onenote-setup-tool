using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.6: the cover label lines, the Annual Objectives and Objective Details pass-through pages, and the two pages without an image.</summary>
[Collection(RenderCollection.Name)]
public sealed class CoverAndObjectivesTests
{
    private readonly RenderFixture f;

    public CoverAndObjectivesTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private Manifest M => f.Manifest;

    [SkippableFact]
    public void Cover_carries_the_quarter_label_and_the_date_span_centered()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.Cover));
        CoverLabelSlot slot = M.Slots.CoverLabel;

        string label = RenderFixture.Read(sheet, new RectPt(slot.X - 120, slot.Baselines[0] - 14, 240, 19));
        label.Should().Be("Q1 2027");
        string span = RenderFixture.Read(sheet, new RectPt(slot.X - 160, slot.Baselines[1] - 14, 320, 19));
        RenderFixture.Digits(span).Should().Be("12027312027", "OCR read '" + span + "' for 'Jan 1, 2027 to Mar 31, 2027'");

        double half = f.Fonts.TextWidth("Q1 2027", slot.SizePt) / 2;
        f.HasInk(sheet, new RectPt(slot.X - half, slot.Baselines[0] - 11, half, 12)).Should().BeTrue("ink left of center");
        f.HasInk(sheet, new RectPt(slot.X, slot.Baselines[0] - 11, half, 12)).Should().BeTrue("ink right of center");
        f.HasInk(sheet, new RectPt(slot.X - half - 30, slot.Baselines[0] - 11, 25, 12)).Should().BeFalse("nothing beyond the label");
    }

    [Fact]
    public void Cover_has_no_anchors()
    {
        f.Render(PageKey.Cover).Anchors.Should().BeEmpty();
    }

    [Fact]
    public void Annual_pages_carry_four_objective_links_and_four_title_cells_each()
    {
        RenderedPage first = f.Render(PageKey.Annual(1));
        RenderedPage second = f.Render(PageKey.Annual(2));
        AnnualObjectiveAnchor a = M.Links.AnnualObjective;

        first.Anchors.Where(x => x.Kind == AnchorKind.Link).Select(x => x.AnchorId).Should().Equal("annualObjective:1", "annualObjective:2", "annualObjective:3", "annualObjective:4");
        second.Anchors.Where(x => x.Kind == AnchorKind.Link).Select(x => x.AnchorId).Should().Equal("annualObjective:5", "annualObjective:6", "annualObjective:7", "annualObjective:8");
        first.Anchors.Where(x => x.Kind == AnchorKind.Propagated).Select(x => x.AnchorId).Should().Equal("annualRow:1", "annualRow:2", "annualRow:3", "annualRow:4");
        second.Anchors.Where(x => x.Kind == AnchorKind.Propagated).Select(x => x.AnchorId).Should().Equal("annualRow:5", "annualRow:6", "annualRow:7", "annualRow:8");

        AnchorPlacement link3 = first.Anchors.Single(x => x.AnchorId == "annualObjective:3");
        link3.Text.Should().Be("Objective 3");
        link3.X.Should().Be((float)a.X);
        link3.Y.Should().BeApproximately((float)(a.BlockTops[2] + a.YOffset), 0.01f);
        link3.SizePt.Should().Be((float)a.SizePt);

        AnchorPlacement row7 = second.Anchors.Single(x => x.AnchorId == "annualRow:7");
        AnnualTitleSlot title = M.Slots.AnnualTitle;
        row7.X.Should().Be((float)title.X);
        row7.Y.Should().BeApproximately((float)(a.BlockTops[2] + title.YOffset), 0.01f);
        row7.W.Should().Be((float)title.W);
        row7.Text.Should().BeEmpty();
    }

    [Fact]
    public void Annual_page_with_erase_on_clears_the_label_cells()
    {
        Manifest erasing = M with { Links = M.Links with { AnnualObjective = M.Links.AnnualObjective with { Erase = true } } };
        PageRenderer renderer = new(erasing, f.Rasterizer, f.Fonts, f.Engine);
        RenderedPage page = renderer.Render(f.Q1Plan.Page(PageKey.Annual(1)), f.Q1Plan, f.Options(f.Q1Options));
        using SKBitmap sheet = RenderFixture.Decode(page);

        RectPt cell = erasing.Links.AnnualObjective.Rect(0);
        Pixels.Mean(sheet, new RectPt(cell.X + 1, cell.Y + 1, cell.W - 2, cell.H - 2), RenderFixture.Dpi).Should().Be(f.Background);
        page.Anchors.Should().Contain(x => x.AnchorId == "annualObjective:1");
    }

    [Fact]
    public void Objective_page_carries_the_back_link_and_the_heading_cell()
    {
        RenderedPage page = f.Render(PageKey.Objective(3));

        page.Anchors.Should().HaveCount(2);
        AnchorPlacement back = page.Anchors.Single(x => x.AnchorId == "objectiveBack");
        back.Kind.Should().Be(AnchorKind.Link);
        back.Text.Should().Be("Objective 3");
        back.X.Should().Be((float)M.Links.ObjectiveBack.X);
        back.Y.Should().Be((float)M.Links.ObjectiveBack.Y);
        AnchorPlacement heading = page.Anchors.Single(x => x.AnchorId == "objectiveHeading");
        heading.Kind.Should().Be(AnchorKind.Propagated);
        heading.X.Should().Be((float)M.Slots.ObjectiveHeading.Rect.X);
        heading.SizePt.Should().Be((float)M.Slots.ObjectiveHeading.SizePt);
    }

    [Fact]
    public void Annual_pages_with_links_off_bake_each_block_label_so_the_two_pages_differ()
    {
        RenderedPage first = f.Render(PageKey.Annual(1), o => o with { Links = false });
        RenderedPage second = f.Render(PageKey.Annual(2), o => o with { Links = false });
        AnnualObjectiveAnchor a = M.Links.AnnualObjective;

        first.Anchors.Should().NotContain(x => x.Kind == AnchorKind.Link, "links are off");
        first.Png.Should().NotEqual(second.Png, "page 1 names objectives 1 to 4 and page 2 names 5 to 8");
        using SKBitmap sheet = RenderFixture.Decode(first);
        for (int block = 0; block < a.BlockTops.Count; block++)
        {
            RectPt label = a.Rect(block);
            f.HasInk(sheet, new RectPt(label.X, label.Y + 1, 12, label.H - 2)).Should().BeTrue($"'Objective {block + 1}' starts at the label's left edge");
            f.HasInk(sheet, label).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void Annual_page_with_links_off_reads_Objective_n_in_each_block()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.Annual(2), o => o with { Links = false }));
        AnnualObjectiveAnchor a = M.Links.AnnualObjective;

        RenderFixture.Read(sheet, a.Rect(0), paddingPt: 2).Should().Be("Objective 5");
        RenderFixture.Read(sheet, a.Rect(3), paddingPt: 2).Should().Be("Objective 8");
    }

    [Fact]
    public void Objective_pages_with_links_off_bake_their_number_beside_the_heading()
    {
        RenderedPage third = f.Render(PageKey.Objective(3), o => o with { Links = false });
        RenderedPage fourth = f.Render(PageKey.Objective(4), o => o with { Links = false });

        third.Anchors.Should().ContainSingle().Which.AnchorId.Should().Be("objectiveHeading", "links are off; the heading slot stays for Refresh");
        third.Png.Should().NotEqual(fourth.Png, "each detail page names its objective");
        using SKBitmap sheet = RenderFixture.Decode(third);
        RectPt back = M.Links.ObjectiveBack.Rect;
        f.HasInk(sheet, new RectPt(back.X, back.Y + 2, 14, back.H - 4)).Should().BeTrue("'Objective 3' starts at the back link's left edge");
    }

    [SkippableFact]
    public void Objective_page_with_links_off_reads_Objective_3()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.Objective(3), o => o with { Links = false }));

        RenderFixture.Read(sheet, M.Links.ObjectiveBack.Rect, paddingPt: 2).Should().Be("Objective 3");
    }

    [Fact]
    public void Objective_page_art_passes_through_untouched()
    {
        RenderedPage page = f.Render(PageKey.Objective(3));
        using SKBitmap rendered = RenderFixture.Decode(page);
        using SKBitmap raw = f.Rasterizer.Render(f.Pdf, f.Q1Plan.Page(PageKey.Objective(3)).PdfPage!.Value, RenderFixture.Dpi);
        using SKBitmap rawRgb = PngEncoder.ToRgb888x(raw);

        PngEncoder.Encode(rawRgb).Should().Equal(page.Png, "no overlay is drawn on an Objective Details page");
    }

    [Theory]
    [InlineData("contents")]
    [InlineData("objectives")]
    public void Contents_and_the_blank_parent_page_have_no_image_and_no_anchors(string key)
    {
        RenderedPage page = f.Render(new PageKey(key));

        page.Png.Should().BeEmpty();
        page.WidthPx.Should().Be(0);
        page.HeightPx.Should().Be(0);
        page.Anchors.Should().BeEmpty();
        PageRenderer.HasNoImage(f.Q1Plan.Page(new PageKey(key)).Type).Should().BeTrue();
    }
}
