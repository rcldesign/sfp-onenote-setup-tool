using Sfp.App.ViewModels.Generate;
using Sfp.Core.Dates;
using Sfp.Core.Options;
using Sfp.Core.Plan;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>M6.3: three thumbnails on a background task, cancelled and debounced on change, errors as a line.</summary>
public class PreviewStripViewModelTests
{
    private readonly Harness _h = new();

    private static GenerateOptions Options(int month = 1) => new() { Start = new QuarterSpec(2027, month), PdfPath = Harness.Pdf };

    [UiFact]
    public async Task Request_renders_the_first_daily_page_the_first_calendar_month_and_the_cover()
    {
        using PreviewStripViewModel strip = _h.NewPreviews();

        strip.Request(Options());
        strip.Day.IsRendering.Should().BeTrue();
        strip.Day.Status.Should().Be("Rendering...");
        await _h.Settle(strip.Pending);

        strip.Day.Title.Should().Be("Fri Jan 1");
        strip.CalendarMonth.Title.Should().Be("January 2027");
        strip.Cover.Title.Should().Be("Cover");
        strip.Thumbnails.Should().AllSatisfy(t =>
        {
            t.HasImage.Should().BeTrue();
            t.Png.Should().Equal(FakePageRenderer.PngStub);
            t.WidthPx.Should().Be(1112);
            t.HeightPx.Should().Be(770);
            t.Status.Should().BeNull();
            t.IsRendering.Should().BeFalse();
        });
        strip.Status.Should().BeNull();
        strip.RenderCount.Should().Be(1);
        _h.Renderer.Rendered.Should().Equal("Fri Jan 1", "January 2027", "Cover");
        _h.Plans.Calls.Should().Be(1);
    }

    [UiFact]
    public void Pick_skips_daily_pages_outside_the_quarter()
    {
        Quarter q = _h.Dates.Build(new QuarterSpec(2027, 1));
        NotebookPlan plan = _h.Plans.Build(_h.Manifest, q, Options());

        (PageSpec? day, PageSpec? calendar, PageSpec? cover) = PreviewStripViewModel.Pick(plan);

        day!.Title.Should().Be("Fri Jan 1");
        calendar!.Type.Should().Be(PageType.Calendar);
        cover!.Type.Should().Be(PageType.Cover);
    }

    [UiFact]
    public async Task A_missing_page_in_the_plan_is_reported_in_its_slot_only()
    {
        using PreviewStripViewModel strip = _h.NewPreviews();
        Quarter q = _h.Dates.Build(new QuarterSpec(2027, 1));
        NotebookPlan full = _h.Plans.Build(_h.Manifest, q, Options());
        NotebookPlan noCover = full with { Sections = full.Sections.Where(s => s.Name != "Quarter").ToList() };

        (PageSpec? day, PageSpec? calendar, PageSpec? cover) = PreviewStripViewModel.Pick(noCover);
        day.Should().NotBeNull();
        calendar.Should().NotBeNull();
        cover.Should().BeNull();
        await _h.Settle(strip.Pending);
    }

    [UiFact]
    public async Task Renderer_errors_become_a_status_line_and_never_throw()
    {
        _h.Renderer.Throw = new InvalidOperationException("PDFium failed on page 17");
        using PreviewStripViewModel strip = _h.NewPreviews();

        strip.Request(Options());
        await _h.Settle(strip.Pending);

        strip.Status.Should().Be("Preview failed: PDFium failed on page 17");
        strip.Thumbnails.Should().AllSatisfy(t =>
        {
            t.HasImage.Should().BeFalse();
            t.Status.Should().Be("Preview failed: PDFium failed on page 17");
            t.IsRendering.Should().BeFalse();
        });
    }

    [UiFact]
    public async Task Plan_builder_errors_name_the_missing_module()
    {
        _h.Plans.Throw = new NotSupportedException("The plan builder (M1.6) is not wired into the app yet.");
        using PreviewStripViewModel strip = _h.NewPreviews();

        strip.Request(Options());
        await _h.Settle(strip.Pending);

        strip.Status.Should().Contain("M1.6");
    }

    [UiFact]
    public async Task A_newer_request_cancels_the_render_in_flight_and_only_its_result_shows()
    {
        using ManualResetEventSlim gate = new(false);
        _h.Renderer.Gate = gate;
        using PreviewStripViewModel strip = _h.NewPreviews();

        strip.Request(Options(month: 1));
        Task first = strip.Pending;
        strip.Request(Options(month: 4));
        Task second = strip.Pending;
        gate.Set();
        await _h.Settle(first, second);

        strip.Day.Title.Should().Be("Thu Apr 1");
        strip.CalendarMonth.Title.Should().Be("April 2027");
        strip.RenderCount.Should().Be(2);
    }

    [UiFact]
    public async Task Requests_inside_the_debounce_window_collapse_into_one_render()
    {
        using PreviewStripViewModel strip = _h.NewPreviews(TimeSpan.FromMilliseconds(80));

        strip.Request(Options(month: 1));
        strip.Request(Options(month: 4));
        strip.Request(Options(month: 7));
        await _h.Settle(strip.Pending);

        strip.RenderCount.Should().Be(1);
        _h.Plans.Calls.Should().Be(1);
        strip.Day.Title.Should().Be("Thu Jul 1");
    }

    [UiFact]
    public async Task Clear_cancels_and_shows_the_placeholder_in_every_slot()
    {
        using PreviewStripViewModel strip = _h.NewPreviews();
        strip.Request(Options());
        await _h.Settle(strip.Pending);

        strip.Clear("Choose a template PDF to see previews.");

        strip.Thumbnails.Should().AllSatisfy(t =>
        {
            t.HasImage.Should().BeFalse();
            t.Status.Should().Be("Choose a template PDF to see previews.");
            t.Title.Should().Be("");
        });
        await _h.Settle(strip.Pending);
    }

    [UiFact]
    public async Task Previews_render_at_the_preview_dpi_not_the_notebook_dpi()
    {
        using PreviewStripViewModel strip = _h.NewPreviews();

        strip.Request(Options() with { Dpi = 300 });
        await _h.Settle(strip.Pending);

        strip.Day.WidthPx.Should().Be(1112, "72 DPI gives one pixel per point");
        PreviewStripViewModel.DefaultPreviewDpi.Should().Be(72);
        PreviewStripViewModel.DefaultDebounce.Should().Be(TimeSpan.FromMilliseconds(300));
    }

    [UiFact]
    public void Thumbnail_keeps_its_image_while_re_rendering()
    {
        PreviewThumbnail thumbnail = new("Cover");
        thumbnail.ShowImage("Cover", FakePageRenderer.PngStub, 10, 10);

        thumbnail.BeginRendering();

        thumbnail.HasImage.Should().BeTrue();
        thumbnail.Status.Should().BeNull();
        thumbnail.IsRendering.Should().BeTrue();
        thumbnail.Caption.Should().Be("Cover");
    }
}
