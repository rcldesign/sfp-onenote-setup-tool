using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Plan;

/// <summary>Shared pieces of the plan tests: the shipped manifest through the production loader, the date engine, and a one-call Build.</summary>
internal static class PlanFixture
{
    private static readonly Lazy<Manifest> ShippedLazy = new(() => new ManifestLoader().LoadShipped(TestPaths.TemplatesDir));

    public static Manifest Shipped => ShippedLazy.Value;

    public static DateEngine Engine { get; } = new();

    public static QuarterSpec Q1_2027 { get; } = new(2027, 1);

    public static QuarterSpec Q2_2024 { get; } = new(2024, 4);

    public static QuarterSpec Q1_2024 { get; } = new(2024, 1);

    /// <summary>Builds the plan for a quarter with default options, optionally adjusted.</summary>
    public static NotebookPlan Build(QuarterSpec spec, Func<GenerateOptions, GenerateOptions>? adjust = null)
    {
        GenerateOptions options = new() { Start = spec };
        if (adjust is not null)
        {
            options = adjust(options);
        }

        return new PlanBuilder(Engine).Build(Shipped, Engine.Build(spec), options);
    }

    public static SectionSpec Section(NotebookPlan plan, string name) =>
        plan.Sections.Single(s => s.Name == name);

    public static IReadOnlyList<PageSpec> DayPages(NotebookPlan plan) =>
        plan.Pages.Where(p => p.Type == PageType.Day).ToList();

    public static DateOnly D(int year, int month, int day) => new(year, month, day);
}
