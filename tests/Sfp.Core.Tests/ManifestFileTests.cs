using System.Text.Json;
using Sfp.TestSupport;

namespace Sfp.Core.Tests;

/// <summary>Placeholder until M1.2 adds the loader: the shipped manifest must at least be well-formed JSON with the expected id.</summary>
public class ManifestFileTests
{
    [Fact]
    public void Shipped_manifest_is_well_formed_json_with_template_id()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(TestPaths.ManifestPath));
        doc.RootElement.GetProperty("templateId").GetString().Should().Be("sfp-quarterly-2023");
        doc.RootElement.GetProperty("pageCount").GetInt32().Should().Be(132);
    }
}
