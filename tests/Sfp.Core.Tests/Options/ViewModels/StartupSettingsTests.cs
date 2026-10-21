using Sfp.App.ViewModels;
using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>
/// Fix round 2 app-4: settings with null members start the window with their defaults; a failed start never saves; the
/// startup notice names a corrupt or newer file and the copy kept before the first save replaces it.
/// </summary>
public class StartupSettingsTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly string _dir = TestPaths.NewTempDir("startup-settings");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string FilePath => Path.Combine(_dir, "settings.json");

    [Fact]
    public async Task A_store_that_hands_back_null_members_still_starts_with_their_defaults()
    {
        _h.Settings.Stored = new AppSettings { PdfPath = Harness.Pdf, Dpi = 144, KnownOutputFolders = null!, TimeBlocks = null! };
        using MainViewModel main = _h.NewMain();

        main.Invoking(m => m.Initialize()).Should().NotThrow();
        await main.Generate.PendingPdfCheck;
        await main.Generate.Previews.Pending;
        await main.Refresh.PendingReload;

        main.Generate.PdfPath.Should().Be(Harness.Pdf);
        main.Generate.Dpi.Should().Be(144);
        main.Generate.TimeBlockStart.Should().Be(6);
        main.Generate.TimeBlockEnd.Should().Be(22);
        main.KnownOutputFolders.Should().BeEmpty();
        main.SaveSettings().Should().BeTrue();
        _h.Settings.Stored.TimeBlocks.Should().Be(TimeBlockWindow.Default);
    }

    [Fact]
    public async Task A_settings_file_with_null_members_starts_and_the_exit_save_keeps_the_other_values()
    {
        File.WriteAllText(FilePath, """{ "pdfPath": "C:\\planner\\SFP_No_Dates.pdf", "knownOutputFolders": null, "timeBlocks": null }""");
        using MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), new SettingsStore(FilePath));

        main.Initialize();
        await main.Generate.PendingPdfCheck;
        await main.Generate.Previews.Pending;

        main.Notice.Should().BeNull();
        main.Generate.PdfPath.Should().Be(Harness.Pdf);
        main.SaveSettings().Should().BeTrue();
        AppSettings saved = new SettingsStore(FilePath).Load(out string? problem);
        problem.Should().BeNull();
        saved.PdfPath.Should().Be(Harness.Pdf, "the exit save keeps the path the file held");
        saved.TimeBlocks.Should().Be(TimeBlockWindow.Default);
        saved.KnownOutputFolders.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_start_never_saves_over_the_file()
    {
        const string original = """{ "pdfPath": "C:\\planner\\SFP_No_Dates.pdf", "dpi": 300 }""";
        File.WriteAllText(FilePath, original);
        bool clockFails = false;
        _h.Env = _h.Env with { Clock = () => clockFails ? throw new InvalidOperationException("The clock is not available.") : _h.Now };
        using MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), new SettingsStore(FilePath));
        clockFails = true;

        main.Invoking(m => m.Initialize()).Should().Throw<InvalidOperationException>();

        main.SaveSettings().Should().BeFalse("App.OnExit saves after a failed start too, and that save must not happen");
        File.ReadAllText(FilePath).Should().Be(original);
    }

    [Fact]
    public async Task A_corrupt_file_is_named_in_the_notice_with_the_copy_kept_before_the_first_save()
    {
        const string corrupt = """{ "dpi": 144, oops""";
        File.WriteAllText(FilePath, corrupt);
        using MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), new SettingsStore(FilePath));

        main.Initialize();
        await main.Generate.PendingPdfCheck;

        main.Notice.Should().Contain(FilePath).And.Contain("defaults are used").And.Contain(FilePath + ".bak");
        main.SaveSettings().Should().BeTrue();
        File.ReadAllText(FilePath + ".bak").Should().Be(corrupt);
    }

    [Fact]
    public async Task A_newer_versions_file_is_used_named_in_the_notice_and_copied_before_the_first_save()
    {
        const string newer = """{ "schemaVersion": 2, "dpi": 144, "cloudSync": true }""";
        File.WriteAllText(FilePath, newer);
        using MainViewModel main = new(_h.NewGenerate(), _h.NewRefresh(), new SettingsStore(FilePath));

        main.Initialize();
        await main.Generate.PendingPdfCheck;

        main.Generate.Dpi.Should().Be(144, "the values this version knows are used");
        main.Notice.Should().Contain("newer version").And.Contain(FilePath + ".bak");
        main.SaveSettings().Should().BeTrue();
        File.ReadAllText(FilePath + ".bak").Should().Be(newer);
        File.ReadAllText(FilePath).Should().Contain("\"schemaVersion\": 1");
    }
}
