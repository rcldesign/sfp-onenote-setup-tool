using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.Render.Pages;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.8: Setup, scorecard, reviews and Notes pages drawn from manifest.generated on a blank sheet or the reflection art.</summary>
[Collection(RenderCollection.Name)]
public sealed class GeneratedPagesTests
{
    private readonly RenderFixture f;

    public GeneratedPagesTests(RenderFixture fixture)
    {
        f = fixture;
    }

    private Manifest M => f.Manifest;

    private RectPt HeadingRegion(double width)
    {
        HeadingSpec h = M.Generated.Heading;
        return new RectPt(h.X - 2, h.BaselineY - h.SizePt, width, h.SizePt * 1.3);
    }

    /// <summary>A box inset by 2 pt so its outline stays out of an OCR crop.</summary>
    private static RectPt Inside(RectPt box) => new(box.X + 2, box.Y + 2, box.W - 4, box.H - 4);

    [SkippableFact]
    public void Setup_page_shows_its_heading_labels_and_window_hints()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.Setup));
        SetupPageSpec s = M.Generated.Setup;

        RenderFixture.Read(sheet, HeadingRegion(140), paddingPt: 8).Should().BeEquivalentTo("Setup", "the engine sometimes reads the capital S as lower case");
        LabelSpec objectives = s.Labels.Single(l => l.Text == "Objectives");
        RenderFixture.Read(sheet, new RectPt(objectives.X - 2, objectives.BaselineY - 14, 120, 19)).Should().Be("Objectives");
        RenderFixture.Digits(RenderFixture.Read(sheet, Inside(s.TimeBlockStart), paddingPt: 0, layout: OcrLayout.SingleWord)).Should().Be("06");
        RenderFixture.Digits(RenderFixture.Read(sheet, Inside(s.TimeBlockEnd), paddingPt: 0, layout: OcrLayout.SingleWord)).Should().Be("22");
    }

    [Fact]
    public void Setup_page_rules_every_slot_numbers_them_and_has_no_anchors()
    {
        RenderedPage page = f.Render(PageKey.Setup);
        using SKBitmap sheet = RenderFixture.Decode(page);
        SetupPageSpec s = M.Generated.Setup;

        page.Anchors.Should().BeEmpty("the user fills the Setup page; Refresh reads it");
        page.WidthPx.Should().Be(3398);
        foreach (RectPt slot in s.Objectives.Concat(s.StandingGoals).Concat(s.MetricNames).Concat(s.Habits).Concat(s.CalendarSources))
        {
            f.HasInk(sheet, new RectPt(slot.CenterX - 1, slot.Bottom - 0.6, 2, 1.2)).Should().BeTrue($"a rule under the slot at {slot}");
            f.HasInk(sheet, new RectPt(slot.X - 12, slot.Y + 2, 11, slot.H - 4)).Should().BeTrue($"a number left of the slot at {slot}");
            f.HasInk(sheet, new RectPt(slot.X + 2, slot.Y + 2, slot.W - 4, slot.H - 5)).Should().BeFalse($"the slot at {slot} is empty");
        }

        SKColor hint = Pixels.Darkest(sheet, new RectPt(s.TimeBlockStart.X + 2, s.TimeBlockStart.Y + 2, s.TimeBlockStart.W - 4, s.TimeBlockStart.H - 4), RenderFixture.Dpi);
        ((int)hint.Red).Should().BeGreaterThan(f.Ink.Red + 60, "hint digits are at 40% ink");
        ((int)hint.Red).Should().BeLessThan(f.Background.Red - 40);
        f.HasInk(sheet, new RectPt(s.TimeBlockStart.X - 0.6, s.TimeBlockStart.CenterY, 1.2, 2)).Should().BeTrue("the window box is outlined");
    }

    [SkippableFact]
    public void Scorecard_has_the_heading_the_quarter_column_and_the_week_headers_reserved()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(PageKey.Scorecard);
        using SKBitmap sheet = RenderFixture.Decode(page);
        ScorecardPageSpec s = M.Generated.Scorecard;

        RenderFixture.Read(sheet, HeadingRegion(330), paddingPt: 8).Should().Be("Quarter scorecard");
        double quarterX = GeneratedPages.QuarterColumnX(s, 14);
        RenderFixture.Read(sheet, new RectPt(quarterX - 10, s.HeaderBaselineY - 10, s.FinalColumnW + 20, 14)).Should().Be("Quarter");

        IReadOnlyList<AnchorPlacement> weeks = page.Anchors.Where(a => a.Kind == AnchorKind.Link).ToList();
        weeks.Select(a => a.AnchorId).Should().Equal(Enumerable.Range(1, 14).Select(w => "scorecardWeek:" + w.ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
        weeks.Select(a => a.Text).Should().Equal(Enumerable.Range(1, 14).Select(w => w.ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
        weeks[0].X.Should().BeApproximately((float)s.WeekHeader(0).X, 0.01f);
        weeks[13].X.Should().BeApproximately((float)s.WeekHeader(13).X, 0.01f);
        weeks.Should().OnlyContain(a => a.Align == M.Links.ScorecardWeek.Align && a.SizePt == (float)M.Links.ScorecardWeek.SizePt);
        f.HasInk(sheet, new RectPt(s.WeekHeader(3).X + 2, s.WeekHeader(3).Y + 1, s.WeekHeader(3).W - 4, s.WeekHeader(3).H - 2)).Should().BeFalse("week headers are links, not baked");

        IReadOnlyList<AnchorPlacement> rows = page.Anchors.Where(a => a.Kind == AnchorKind.Propagated).ToList();
        rows.Select(a => a.AnchorId).Should().Equal(Enumerable.Range(1, 8).Select(r => "scorecardRow:" + r));
        rows[2].Y.Should().BeApproximately((float)s.RowLabel(2).Y, 0.01f);

        for (int row = 0; row <= s.RowCount; row++)
        {
            double y = s.FirstRowTop + (s.RowPitch * row);
            f.HasInk(sheet, new RectPt(s.ColumnX0 + 20, y - 0.6, 2, 1.2)).Should().BeTrue($"row rule {row}");
        }

        for (int column = 0; column <= 14; column++)
        {
            double x = s.ColumnX0 + (s.ColumnPitch * column);
            f.HasInk(sheet, new RectPt(x - 0.6, s.FirstRowTop + 20, 1.2, 2)).Should().BeTrue($"column rule {column}");
        }
    }

    [Fact]
    public void Scorecard_with_links_off_bakes_every_week_number_in_its_header_cell()
    {
        RenderedPage page = f.Render(PageKey.Scorecard, o => o with { Links = false });
        using SKBitmap sheet = RenderFixture.Decode(page);
        ScorecardPageSpec s = M.Generated.Scorecard;

        page.Anchors.Should().NotContain(a => a.Kind == AnchorKind.Link, "links are off");
        for (int week = 0; week < 14; week++)
        {
            RectPt cell = s.WeekHeader(week);
            f.HasInk(sheet, new RectPt(cell.X + 2, cell.Y + 1, cell.W - 4, cell.H - 2)).Should().BeTrue($"week {week + 1:00} is baked in its header cell");
            f.HasInk(sheet, new RectPt(cell.X + 2, cell.Y + 1, (cell.W / 2) - 9, cell.H - 2)).Should().BeFalse($"week {week + 1:00} is centered in its cell, not at its left edge");
        }
    }

    [SkippableFact]
    public void Scorecard_with_links_off_reads_the_week_numbers_in_the_header()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        using SKBitmap sheet = RenderFixture.Decode(f.Render(PageKey.Scorecard, o => o with { Links = false }));
        ScorecardPageSpec s = M.Generated.Scorecard;

        // Tesseract reads the fallback face's "0" as the letter O when it leads ("01" comes back as "Ol"), so the OCR
        // check reads the cells it reads reliably; the pixel test above covers all fourteen.
        foreach (int week in new[] { 2, 9, 10, 14 })
        {
            RectPt cell = s.WeekHeader(week - 1);
            string text = RenderFixture.Read(sheet, new RectPt(cell.X + 3, cell.Y, cell.W - 6, cell.H), paddingPt: 1);
            RenderFixture.Digits(text).Should().Be(week.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "OCR read '" + text + "'");
        }
    }

    [Fact]
    public void Q2_2024_scorecard_has_13_week_columns()
    {
        Core.Options.GenerateOptions q2 = f.Q1Options with { Start = new Core.Dates.QuarterSpec(2024, 4) };
        NotebookPlan plan = f.Plan(q2);
        RenderedPage page = f.Renderer.Render(plan.Page(PageKey.Scorecard), plan, f.Options(q2));
        page.Anchors.Count(a => a.Kind == AnchorKind.Link).Should().Be(13);
    }

    [Theory]
    [InlineData(2027, 1, 14)]
    [InlineData(2024, 4, 13)]
    public void The_quarter_column_follows_the_last_week_column_with_one_rule_between(int year, int month, int weeks)
    {
        Core.Options.GenerateOptions options = f.Q1Options with { Start = new Core.Dates.QuarterSpec(year, month) };
        NotebookPlan plan = f.Plan(options);
        plan.Quarter.WeekCount.Should().Be(weeks);
        using SKBitmap sheet = RenderFixture.Decode(f.Renderer.Render(plan.Page(PageKey.Scorecard), plan, f.Options(options)));
        ScorecardPageSpec s = M.Generated.Scorecard;
        double lastWeekRight = s.ColumnX0 + (s.ColumnPitch * weeks);
        double quarterRight = lastWeekRight + s.FinalColumnW;
        double bodyY = s.FirstRowTop + (s.RowPitch / 2);
        double ruleY = s.FirstRowTop + s.RowPitch;

        GeneratedPages.QuarterColumnX(s, weeks).Should().Be(lastWeekRight);
        f.HasInk(sheet, new RectPt(lastWeekRight - 0.6, bodyY, 1.2, 4)).Should().BeTrue("the rule after the last week column is the Quarter column's left edge");
        f.HasInk(sheet, new RectPt(lastWeekRight + 1, bodyY, s.FinalColumnW - 2, 4)).Should().BeFalse("one rule between week {0:00} and Quarter, and no second rule at finalColumnX", weeks);
        f.HasInk(sheet, new RectPt(quarterRight - 0.6, bodyY, 1.2, 4)).Should().BeTrue("the Quarter column is finalColumnW wide");
        f.HasInk(sheet, new RectPt(quarterRight + 1, s.FirstRowTop - 2, 1082 - quarterRight - 1, s.RowPitch * 2)).Should().BeFalse("no phantom column or rule stub right of Quarter");
        f.HasInk(sheet, new RectPt(quarterRight - 4, ruleY - 0.6, 3, 1.2)).Should().BeTrue("the row rules run to the Quarter column's right edge");

        double title = f.Fonts.TextWidth(GeneratedPages.QuarterColumnTitle, s.LabelSizePt);
        double center = lastWeekRight + (s.FinalColumnW / 2);
        f.HasInk(sheet, new RectPt(center - (title / 2), s.HeaderBaselineY - 8, title / 2, 9)).Should().BeTrue("'Quarter' is centered over its column");
        f.HasInk(sheet, new RectPt(center, s.HeaderBaselineY - 8, title / 2, 9)).Should().BeTrue();
        f.HasInk(sheet, new RectPt(center + (title / 2) + 1, s.HeaderBaselineY - 8, 6, 9)).Should().BeFalse();
    }

    [Fact]
    public void Week_columns_that_would_end_past_finalColumnX_are_refused()
    {
        ScorecardPageSpec s = M.Generated.Scorecard;
        Manifest wide = M with { Generated = M.Generated with { Scorecard = s with { ColumnPitch = 60 } } };
        PageRenderer renderer = new(wide, f.Rasterizer, f.Fonts, f.Engine);

        Action act = () => renderer.Render(f.Q1Plan.Page(PageKey.Scorecard), f.Q1Plan, f.Options(f.Q1Options, dpi: 72));

        act.Should().Throw<InvalidOperationException>().WithMessage("*generated.scorecard*14 week columns of 60 pt from x 255 end at 1095*finalColumnX 1013*");
        GeneratedPages.QuarterColumnX(s, 14).Should().Be(1011);
        GeneratedPages.QuarterColumnX(s, 13).Should().Be(957);
    }

    [SkippableFact]
    public void Monthly_review_reuses_the_reflection_art_with_a_new_heading()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(PageKey.MonthlyReview(2027, 1));
        using SKBitmap sheet = RenderFixture.Decode(page);

        f.Q1Plan.Page(PageKey.MonthlyReview(2027, 1)).PdfPage.Should().Be(M.PageTypes.Weekly.PdfPageFor(13, "reflection"));
        RenderFixture.Read(sheet, HeadingRegion(300), paddingPt: 8).Should().Be("January Review");
        RectPt slashes = M.Slots.Date.SlashErase;
        Pixels.Mean(sheet, new RectPt(slashes.X + 1, slashes.Y + 1, slashes.W - 2, slashes.H - 2), RenderFixture.Dpi).Should().Be(f.Background, "the empty date slot is erased");
        page.Anchors.Should().BeEmpty("a monthly review reserves nothing");
    }

    [SkippableFact]
    public void Quarterly_review_is_retitled_and_reserves_the_scorecard_and_next_quarter_links()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(PageKey.QuarterlyReview);
        using SKBitmap sheet = RenderFixture.Decode(page);

        RenderFixture.Read(sheet, HeadingRegion(330), paddingPt: 8).Should().Be("Q1 2027 Review");
        page.Anchors.Select(a => a.AnchorId).Should().Equal("nextQuarter", "quarterReviewScorecard");
        AnchorPlacement scorecard = page.Anchors.Single(a => a.AnchorId == "quarterReviewScorecard");
        scorecard.Text.Should().Be("Scorecard ›");
        scorecard.X.Should().Be((float)M.Links.QuarterReviewScorecard.X);
        AnchorPlacement next = page.Anchors.Single(a => a.AnchorId == "nextQuarter");
        next.Kind.Should().Be(AnchorKind.Link);
        next.Text.Should().BeEmpty("a later Generate with --previous fills it");
        next.X.Should().Be((float)M.Links.NextQuarter.X);
        RectPt erased = M.Links.QuarterReviewScorecard.Rect;
        Pixels.Mean(sheet, new RectPt(erased.X + 1, erased.Y + 1, erased.W - 2, erased.H - 2), RenderFixture.Dpi).Should().Be(f.Background, "the printed label under the scorecard link is erased");
    }

    [Fact]
    public void Quarterly_review_with_links_off_reserves_nothing()
    {
        f.Render(PageKey.QuarterlyReview, o => o with { Links = false }).Anchors.Should().BeEmpty();
    }

    [SkippableFact]
    public void Notes_page_is_a_ruled_sheet_titled_with_its_name()
    {
        Skip.If(!Ocr.IsAvailable, "No OCR engine on this machine.");
        RenderedPage page = f.Render(PageKey.Notes("Backlog"));
        using SKBitmap sheet = RenderFixture.Decode(page);
        NotesPageSpec n = M.Generated.Notes;

        RenderFixture.Read(sheet, HeadingRegion(180), paddingPt: 8).Should().Be("Backlog");
        int expected = GeneratedPages.NotesRuleCount(n);
        expected.Should().BeGreaterThan(20);
        int found = 0;
        for (double y = n.RulesTop; y <= n.RulesBottom + 0.001; y += n.RulePitch)
        {
            if (f.HasInk(sheet, new RectPt(n.RuleX + 200, y - 0.6, 2, 1.2)))
            {
                found++;
            }

            f.HasInk(sheet, new RectPt(n.RuleX + 200, y + 3, 2, n.RulePitch - 6)).Should().BeFalse("nothing between rules");
        }

        found.Should().Be(expected);
        f.HasInk(sheet, new RectPt(n.RuleX + 3, n.RulesTop - 0.6, 2, 1.2)).Should().BeTrue("rules start at ruleX");
        f.HasInk(sheet, new RectPt(n.RuleX - 6, n.RulesTop - 0.6, 4, 1.2)).Should().BeFalse();

        AnchorPlacement contents = page.Anchors.Should().ContainSingle().Subject;
        contents.AnchorId.Should().Be("notesContents");
        contents.Text.Should().Be("Contents ›");
        contents.X.Should().Be((float)M.Links.NotesContents.X);
    }

    [Fact]
    public void Generated_pages_are_blank_sheets_of_the_page_size()
    {
        foreach (PageKey key in new[] { PageKey.Setup, PageKey.Scorecard, PageKey.Notes("Ideas") })
        {
            RenderedPage page = f.Render(key, dpi: 72);
            page.WidthPx.Should().Be(1112);
            page.HeightPx.Should().Be(770);
            using SKBitmap sheet = RenderFixture.Decode(page);
            Pixels.Dominant(sheet).Should().Be(f.Background);
        }
    }
}
