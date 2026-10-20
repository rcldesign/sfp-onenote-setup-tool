using System.Reflection;

namespace Sfp.Core.Tests;

/// <summary>
/// The layer rule from the development plan: Core depends on nothing in the solution, Render on Core, OneNote on Core and
/// Render, Refresh on Core and OneNote. Reflecting over assembly references fails the build if a layer gains a dependency.
/// </summary>
public class DependencyDirectionTests
{
    public static TheoryData<string, string[]> Layers => new()
    {
        { "Sfp.Core", Array.Empty<string>() },
        { "Sfp.Render", new[] { "Sfp.Core" } },
        { "Sfp.OneNote", new[] { "Sfp.Core", "Sfp.Render" } },
        { "Sfp.Refresh", new[] { "Sfp.Core", "Sfp.Render", "Sfp.OneNote" } },
    };

    [Theory]
    [MemberData(nameof(Layers))]
    public void Assembly_references_only_the_allowed_solution_assemblies(string assemblyName, string[] allowed)
    {
        Assembly asm = Assembly.Load(assemblyName);
        string[] sfpRefs = asm.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Sfp.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        sfpRefs.Should().BeSubsetOf(allowed, $"{assemblyName} may only depend on [{string.Join(", ", allowed)}]");
    }
}
