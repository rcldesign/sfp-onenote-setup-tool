using Sfp.Core.Dates;
using Sfp.OneNote;
using Sfp.Refresh.Orchestration;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>M4.7: the registry lists the notebooks whose sidecars sit in the given folders.</summary>
public class NotebookRegistryTests
{
    private static Sidecar NewSidecar(string name, string path, int year, int month, DateTimeOffset? refreshed = null) => new()
    {
        ToolVersion = "1",
        ManifestId = "sfp-quarterly-2023",
        Quarter = new QuarterSpec(year, month),
        NotebookName = name,
        NotebookPath = path,
        NotebookId = "{" + name + "}",
        Generated = new DateTimeOffset(year, month, 1, 8, 0, 0, TimeSpan.Zero),
        LastRefresh = refreshed,
    };

    [Fact]
    public void Lists_every_sidecar_in_the_folders_once_and_skips_junk_and_missing_folders()
    {
        string folder = TestPaths.NewTempDir("registry");
        SidecarStore store = new();
        string q1 = Path.Combine(folder, "SFP Q1 2027");
        string q2 = Path.Combine(folder, "SFP Q2 2027");
        store.Save(store.PathFor(q1), NewSidecar("SFP Q1 2027", q1, 2027, 1, new DateTimeOffset(2027, 1, 5, 5, 0, 0, TimeSpan.Zero)));
        store.Save(store.PathFor(q2), NewSidecar("SFP Q2 2027", "", 2027, 4));
        File.WriteAllText(Path.Combine(folder, "junk.sfp-state.json"), "{ not json");
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{}");
        NotebookRegistry registry = new(store);

        IReadOnlyList<KnownNotebook> known = registry.List(new[] { folder, folder, Path.Combine(folder, "missing"), "" });

        known.Should().HaveCount(2);
        known[0].Should().Be(new KnownNotebook("SFP Q1 2027", q1, new QuarterSpec(2027, 1), new DateTimeOffset(2027, 1, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 5, 5, 0, 0, TimeSpan.Zero), store.PathFor(q1)));
        known[1].Name.Should().Be("SFP Q2 2027");
        known[1].Path.Should().Be(q2, "an empty NotebookPath is derived from the sidecar file name");
        known[1].LastRefresh.Should().BeNull();
        registry.List(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void Notebook_folder_is_derived_from_the_sidecar_file_name()
    {
        string path = Path.Combine("C:", "Notebooks", "SFP Q1 2027." + Sidecar.FileName);

        NotebookRegistry.NotebookFolderOf(path).Should().Be(Path.Combine("C:", "Notebooks", "SFP Q1 2027"));
        NotebookRegistry.Pattern.Should().Be("*.sfp-state.json");
    }
}
