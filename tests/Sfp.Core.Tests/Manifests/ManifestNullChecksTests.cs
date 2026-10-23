using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Manifests;

/// <summary>An explicit or implied null where the manifest record needs a value is a load error naming its JSON path.</summary>
public class ManifestNullChecksTests : IDisposable
{
    private readonly string _tempDir = TestPaths.NewTempDir("manifest-nulls");
    private readonly ManifestLoader _loader = new();

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string> NulledMembers => new()
    {
        { "{ \"slots\": { \"date\": null } }", "$.slots.date" },
        { "{ \"font\": null }", "$.font" },
        { "{ \"onenote\": null }", "$.onenote" },
        { "{ \"slots\": { \"date\": { \"appliesTo\": null } } }", "$.slots.date.appliesTo" },
        { "{ \"slots\": { \"date\": { \"appliesTo\": [\"overview\", null] } } }", "$.slots.date.appliesTo[1]" },
        { "{ \"font\": { \"fallbackFiles\": { \"regular\": null } } }", "$.font.fallbackFiles.regular" },
        { "{ \"pageTypes\": { \"weekly\": { \"probes\": { \"mon\": null } } } }", "$.pageTypes.weekly.probes.mon" },
        { "{ \"slots\": { \"dayCounter\": { \"format\": null } } }", "$.slots.dayCounter.format" },
        { "{ \"generated\": { \"setup\": { \"labels\": [null] } } }", "$.generated.setup.labels[0]" },
        {
            "{ \"slots\": { \"date\": { \"fields\": [ { \"name\": null, \"align\": \"right\", \"x\": 895 }, { \"name\": \"day\", \"align\": \"center\", \"x\": 935 }, { \"name\": \"year\", \"align\": \"left\", \"x\": 976 } ] } } }",
            "$.slots.date.fields[0].name"
        },
        {
            "{ \"notebook\": { \"sections\": [ { \"name\": null, \"pages\": [ { \"type\": \"cover\", \"title\": \"Cover\", \"level\": 1 } ] } ] } }",
            "$.notebook.sections[0].name"
        },
    };

    [Theory]
    [MemberData(nameof(NulledMembers))]
    public void A_null_where_a_value_is_needed_is_a_manifest_error_naming_the_path(string userJson, string path)
    {
        string user = WriteUser(userJson);

        Action act = () => _loader.Merge(TestPaths.ManifestPath, user);

        act.Should().Throw<ManifestException>().WithMessage($"Manifest '*user.json': {path} must not be null.");
    }

    [Fact]
    public void A_positional_record_member_left_out_of_the_file_is_a_load_error_naming_the_key()
    {
        // Every positional record member is [JsonRequired], so a left-out name fails in deserialization instead of
        // reaching the walk as null (an explicit null still does, see NulledMembers).
        string user = WriteUser(
            "{ \"slots\": { \"date\": { \"fields\": [ { \"align\": \"right\", \"x\": 895 }, { \"name\": \"day\", \"align\": \"center\", \"x\": 935 }, { \"name\": \"year\", \"align\": \"left\", \"x\": 976 } ] } } }");

        Action act = () => _loader.Merge(TestPaths.ManifestPath, user);

        act.Should().Throw<ManifestException>()
            .WithMessage("*does not match the manifest schema at *fields[0]*")
            .Which.Message.Should().MatchRegex(MissingRequiredMember.Pattern("name"));
    }

    [Theory]
    [InlineData("{ \"slots\": { \"monthName\": { \"rule\": null } } }")]
    [InlineData("{ \"slots\": { \"objectiveLegend\": { \"maxWidthPt\": null } } }")]
    [InlineData("{ \"links\": { \"prev\": { \"textFormat\": null } } }")]
    [InlineData("{ \"links\": { \"overviewDayHeader\": { \"erase\": null } } }")]
    [InlineData("{ \"slots\": { \"dayGrid\": { \"trackerStrip\": null, \"notesColumn\": null } } }")]
    public void Members_declared_nullable_may_be_null(string userJson)
    {
        string user = WriteUser(userJson);

        Action act = () => _loader.Merge(TestPaths.ManifestPath, user);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_null_title_is_allowed_because_the_page_type_has_a_default()
    {
        string user = WriteUser(
            "{ \"notebook\": { \"sections\": [ { \"name\": \"Quarter\", \"pages\": [ { \"type\": \"cover\", \"title\": null, \"level\": 1 } ] } ] } }");

        Manifest merged = _loader.Merge(TestPaths.ManifestPath, user);

        merged.Notebook.Sections[0].Pages[0].Title.Should().BeNull();
    }

    [Fact]
    public void The_checks_also_cover_a_manifest_built_in_code()
    {
        Manifest shipped = _loader.Load(TestPaths.ManifestPath);
        Manifest broken = shipped with { Notebook = shipped.Notebook with { NamePattern = null! } };

        Action act = () => ManifestChecks.Run(broken, "in code");

        act.Should().Throw<ManifestException>().WithMessage("Manifest 'in code': $.notebook.namePattern must not be null.");
        shipped.Invoking(m => ManifestChecks.Run(m, "shipped")).Should().NotThrow();
    }

    private string WriteUser(string json)
    {
        string dir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "user.json");
        File.WriteAllText(path, json);
        return path;
    }
}
