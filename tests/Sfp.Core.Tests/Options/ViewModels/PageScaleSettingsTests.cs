using Sfp.App.ViewModels;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-3: a page scale that is not a finite number (NaN, Infinity, 1e999) shows the inline error, while the
/// options and the saved settings keep the last finite scale, so saving and the Browse buttons never throw.
/// </summary>
public class PageScaleSettingsTests
{
    private readonly Harness _h = new();

    [UiTheory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public async Task A_non_finite_page_scale_shows_the_error_and_settings_still_save_and_browse_works(string typed)
    {
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        await main.Generate.PendingPdfCheck;
        main.Generate.PageScaleText = "0.72";

        main.Generate.PageScaleText = typed;

        main.Generate.PageScaleError.Should().Be("Page scale must be a number between 0.25 and 2.");
        main.Generate.CanGenerate.Should().BeFalse();
        main.Generate.ToOptions().PageScale.Should().Be(0.72, "the last finite scale stands in");
        main.Notice.Should().BeNull("the save that followed the keystroke succeeded");
        main.SaveSettings().Should().BeTrue();
        _h.Settings.Stored.PageScale.Should().Be(0.72);

        _h.Dialogs.NextFile = Harness.Pdf;
        main.Generate.Invoking(g => g.BrowsePdfCommand.Execute(null)).Should().NotThrow();
        await main.Generate.PendingPdfCheck;
        await main.Generate.Previews.Pending;
        main.Generate.PdfPath.Should().Be(Harness.Pdf);
        _h.Settings.Stored.PdfPath.Should().Be(Harness.Pdf, "the browse saved the settings");
        main.Notice.Should().BeNull();
    }

    [UiFact]
    public async Task Text_that_is_no_number_also_keeps_the_last_finite_scale()
    {
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        await main.Generate.PendingPdfCheck;
        main.Generate.PageScaleText = "1.5";

        main.Generate.PageScaleText = "1.5x";

        main.Generate.PageScaleError.Should().NotBeNull();
        main.Current().PageScale.Should().Be(1.5);
    }

    [UiFact]
    public async Task Any_exception_from_the_store_becomes_the_notice()
    {
        _h.Settings.ThrowOnSave = new InvalidOperationException("the settings folder is a file");
        using MainViewModel main = _h.NewMain();
        main.Initialize();
        await main.Generate.PendingPdfCheck;

        main.Generate.Invoking(g => g.Dpi = 144).Should().NotThrow();
        main.SaveSettings().Should().BeFalse();

        main.Notice.Should().Be("Settings could not be saved to /fake/settings.json: the settings folder is a file");
    }
}
