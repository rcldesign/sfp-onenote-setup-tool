using Sfp.Core.Dates;
using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options;

/// <summary>M6.1: the settings store round-trips, tolerates a missing or corrupt file and ignores unknown keys.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("settings");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string FilePath => Path.Combine(_dir, "nested", "settings.json");

    [Fact]
    public void Save_then_Load_round_trips_every_member()
    {
        AppSettings saved = new()
        {
            PdfPath = @"C:\planner\SFP_No_Dates.pdf",
            Start = new QuarterSpec(2027, 1),
            NotebookName = "SFP Q1 2027",
            OutputFolder = @"D:\Notebooks",
            DateOrder = DateFieldOrder.DayMonthYear,
            PadDateFields = false,
            WeeksRemaining = WeeksRemainingMode.AsPrinted,
            Dpi = 300,
            GroupWeeks = true,
            Links = false,
            HideTitle = true,
            PageSize = PageSizeMode.Custom,
            PageScale = 0.72,
            SixthRow = SixthRowMode.Drop,
            OutOfMonthDays = OutOfMonthDayStyle.Blank,
            ReflectionDateIsWeekStart = true,
            IncludeOutOfQuarterDays = true,
            TimeBlocks = new TimeBlockWindow(7, 19),
            KnownOutputFolders = new[] { @"D:\Notebooks", @"C:\Users\me\Documents\OneNote Notebooks" },
        };
        SettingsStore store = new(FilePath);

        store.Save(saved);
        AppSettings loaded = new SettingsStore(FilePath).Load();

        loaded.Should().BeEquivalentTo(saved);
        loaded.KnownOutputFolders.Should().Equal(saved.KnownOutputFolders);
        File.Exists(FilePath + ".tmp").Should().BeFalse("the temporary file is moved into place");
    }

    [Fact]
    public void Missing_file_loads_the_defaults_without_a_problem()
    {
        SettingsStore store = new(FilePath);

        AppSettings loaded = store.Load(out string? problem);

        loaded.Should().BeEquivalentTo(new AppSettings());
        problem.Should().BeNull();
        File.Exists(FilePath).Should().BeFalse("loading never creates the file");
    }

    [Fact]
    public void Corrupt_file_loads_the_defaults_and_names_the_problem()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"pdfPath\": \"x\", oops");
        SettingsStore store = new(FilePath);

        AppSettings loaded = store.Load(out string? problem);

        loaded.Should().BeEquivalentTo(new AppSettings());
        problem.Should().Contain(FilePath).And.Contain("defaults");
        store.Load().Should().BeEquivalentTo(new AppSettings());
    }

    [Fact]
    public void Unknown_keys_are_ignored_and_known_ones_still_load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {
              "schemaVersion": 7,
              "pdfPath": "C:/x.pdf",
              "futureSetting": { "a": 1 },
              "dpi": 144,
              "start": { "year": 2028, "month": 4, "someday": true }
            }
            """);

        AppSettings loaded = new SettingsStore(FilePath).Load(out string? problem);

        problem.Should().BeNull();
        loaded.SchemaVersion.Should().Be(7);
        loaded.PdfPath.Should().Be("C:/x.pdf");
        loaded.Dpi.Should().Be(144);
        loaded.Start.Should().Be(new QuarterSpec(2028, 4));
        loaded.NotebookName.Should().BeNull();
    }

    [Fact]
    public void Enums_are_written_as_camel_case_strings_and_nulls_are_omitted()
    {
        string json = SettingsStore.Serialize(new AppSettings { WeeksRemaining = WeeksRemainingMode.AsPrinted });

        json.Should().Contain("\"weeksRemaining\": \"asPrinted\"");
        json.Should().Contain("\"dateOrder\": \"monthDayYear\"");
        json.Should().NotContain("pdfPath", "null members are left out");
        json.Should().Contain("\"timeBlocks\": {");
    }

    [Fact]
    public void Default_path_is_under_the_application_data_folder()
    {
        SettingsStore.DefaultPath.Should().EndWith(Path.Combine("sfp2onenote", "settings.json"));
        new SettingsStore().Path.Should().Be(SettingsStore.DefaultPath);
    }

    [Fact]
    public void Save_overwrites_an_existing_file_atomically()
    {
        SettingsStore store = new(FilePath);
        store.Save(new AppSettings { Dpi = 144 });
        store.Save(new AppSettings { Dpi = 300 });

        store.Load().Dpi.Should().Be(300);
        Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Should().ContainSingle();
    }

    [Fact]
    public void Deserialize_returns_null_for_the_json_null_literal()
    {
        SettingsStore.Deserialize("null").Should().BeNull();
        new SettingsStore(FilePath).Save(new AppSettings());
        File.WriteAllText(FilePath, "null");
        new SettingsStore(FilePath).Load().Should().BeEquivalentTo(new AppSettings());
    }
}
