using System.Reflection;
using System.Text.Json;
using Sfp.Core.Options;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options;

/// <summary>
/// Fix round 2 app-4: a null member loads as its default; a file the store could not use as it is (corrupt, or from a
/// newer version) is copied to settings.json.bak before the first save replaces it, and nothing is replaced while that
/// copy fails.
/// </summary>
public class SettingsRecoveryTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("settings-recovery");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string FilePath => Path.Combine(_dir, "settings.json");

    private string BackupFile => FilePath + ".bak";

    [Fact]
    public void Null_members_load_as_their_defaults_and_the_other_values_are_kept()
    {
        File.WriteAllText(FilePath, """{ "schemaVersion": 1, "pdfPath": "C:/planner/SFP.pdf", "dpi": 144, "knownOutputFolders": null, "timeBlocks": null }""");
        SettingsStore store = new(FilePath);

        AppSettings loaded = store.Load(out string? problem);

        problem.Should().BeNull();
        loaded.PdfPath.Should().Be("C:/planner/SFP.pdf");
        loaded.Dpi.Should().Be(144);
        loaded.KnownOutputFolders.Should().NotBeNull().And.BeEmpty();
        loaded.TimeBlocks.Should().Be(TimeBlockWindow.Default);
        store.Save(loaded);
        File.Exists(BackupFile).Should().BeFalse("a current file this version reads is not copied");
    }

    [Fact]
    public void Null_entries_in_the_known_folders_are_dropped()
    {
        AppSettings loaded = SettingsStore.Deserialize("""{ "knownOutputFolders": ["D:/A", null, "E:/B"] }""")!;

        loaded.KnownOutputFolders.Should().Equal("D:/A", "E:/B");
    }

    [Fact]
    public void Every_member_that_must_hold_a_value_gets_its_default_when_the_file_says_null()
    {
        NullabilityInfoContext nullability = new();
        PropertyInfo[] references = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && !p.PropertyType.IsValueType)
            .ToArray();
        string json = "{ " + string.Join(", ", references.Select(p => "\"" + JsonNamingPolicy.CamelCase.ConvertName(p.Name) + "\": null")) + " }";

        AppSettings loaded = SettingsStore.Deserialize(json)!;

        references.Select(p => p.Name).Should().Contain(new[] { nameof(AppSettings.KnownOutputFolders), nameof(AppSettings.TimeBlocks) });
        foreach (PropertyInfo member in references.Where(p => nullability.Create(p).ReadState == NullabilityState.NotNull))
        {
            member.GetValue(loaded).Should().NotBeNull("{0} is declared non-nullable and the window reads it without a check", member.Name);
        }
    }

    [Fact]
    public void A_corrupt_file_is_copied_before_the_first_save_and_only_then()
    {
        const string corrupt = """{ "pdfPath": "C:/x.pdf", oops""";
        File.WriteAllText(FilePath, corrupt);
        SettingsStore store = new(FilePath);

        store.Load(out string? problem);

        problem.Should().Contain(FilePath).And.Contain("defaults are used");
        File.Exists(BackupFile).Should().BeFalse("loading writes nothing");
        store.Save(new AppSettings { Dpi = 144 });
        File.ReadAllText(BackupFile).Should().Be(corrupt);
        new SettingsStore(FilePath).Load().Dpi.Should().Be(144);
        store.Save(new AppSettings { Dpi = 300 });
        File.ReadAllText(BackupFile).Should().Be(corrupt, "only the first save after the load keeps a copy");
    }

    [Fact]
    public void A_newer_versions_file_that_loads_is_used_and_copied_before_it_is_overwritten()
    {
        const string newer = """{ "schemaVersion": 2, "dpi": 144, "pdfPath": "C:/x.pdf", "cloudSync": { "enabled": true } }""";
        File.WriteAllText(FilePath, newer);
        SettingsStore store = new(FilePath);

        AppSettings loaded = store.Load(out string? problem);

        problem.Should().BeNull("the values this version knows load");
        loaded.Dpi.Should().Be(144);
        store.Save(new AppSettings { Dpi = loaded.Dpi, PdfPath = loaded.PdfPath });
        File.ReadAllText(BackupFile).Should().Be(newer);
        File.ReadAllText(FilePath).Should().NotContain("cloudSync").And.Contain("\"schemaVersion\": 1");
    }

    [Fact]
    public void A_newer_versions_file_this_version_cannot_read_says_so_and_is_copied()
    {
        const string newer = """{ "schemaVersion": 2, "weeksRemaining": "rolling", "dpi": 144 }""";
        File.WriteAllText(FilePath, newer);
        SettingsStore store = new(FilePath);

        AppSettings loaded = store.Load(out string? problem);

        loaded.Should().BeEquivalentTo(new AppSettings());
        problem.Should().Contain(FilePath).And.Contain("newer version").And.Contain("defaults are used");
        store.Save(loaded);
        File.ReadAllText(BackupFile).Should().Be(newer);
    }

    [Fact]
    public void When_the_copy_cannot_be_made_nothing_is_replaced_and_the_next_save_tries_again()
    {
        const string corrupt = "{ oops";
        File.WriteAllText(FilePath, corrupt);
        SettingsStore store = new(FilePath);
        store.Load(out _);
        Directory.CreateDirectory(BackupFile);

        store.Invoking(s => s.Save(new AppSettings { Dpi = 144 }))
            .Should().Throw<Exception>()
            .Where(e => e is IOException || e is UnauthorizedAccessException);
        File.ReadAllText(FilePath).Should().Be(corrupt, "the file is never replaced without its copy");

        Directory.Delete(BackupFile);
        store.Save(new AppSettings { Dpi = 144 });
        File.ReadAllText(BackupFile).Should().Be(corrupt);
        new SettingsStore(FilePath).Load().Dpi.Should().Be(144);
    }
}
