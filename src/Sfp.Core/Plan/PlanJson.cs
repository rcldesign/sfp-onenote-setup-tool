using System.Text.Json;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;

namespace Sfp.Core.Plan;

/// <summary>
/// The stable JSON shape of a <see cref="NotebookPlan"/>: name, quarter, sections (names, colors, groups and page keys),
/// the flat page list (each with its section name) and the links. Serialized with <see cref="ManifestLoader.JsonOptions"/>
/// (camelCase, enums as camelCase strings, dates as yyyy-MM-dd), and converted back with <see cref="ToPlan"/>, so the
/// "plan --json" output and a render-only pages index round-trip.
/// </summary>
/// <param name="Name">Notebook name.</param>
/// <param name="Quarter">The quarter.</param>
/// <param name="Sections">Sections in tab order, each listing its page keys.</param>
/// <param name="Pages">Every page in notebook order.</param>
/// <param name="Links">Every link anchor.</param>
public sealed record PlanJson(
    string Name,
    QuarterJson Quarter,
    IReadOnlyList<SectionJson> Sections,
    IReadOnlyList<PageJson> Pages,
    IReadOnlyList<LinkJson> Links)
{
    /// <summary>Builds the JSON shape of a plan.</summary>
    public static PlanJson From(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<SectionJson> sections = new(plan.Sections.Count);
        List<PageJson> pages = new();
        foreach (SectionSpec section in plan.Sections)
        {
            sections.Add(new SectionJson(section.Name, section.ColorHex, section.GroupName, section.Pages.Select(p => p.Key.Value).ToList()));
            foreach (PageSpec page in section.Pages)
            {
                pages.Add(new PageJson(page.Key.Value, section.Name, page.Type, page.Title, page.Level, page.PdfPage, page.Date, page.Tokens));
            }
        }

        List<LinkJson> links = plan.Links.Select(l => new LinkJson(l.From.Value, l.AnchorId, l.To.Value, l.Text)).ToList();
        return new PlanJson(plan.Name, QuarterJson.From(plan.Quarter), sections, pages, links);
    }

    /// <summary>Parses JSON written by <see cref="Serialize"/>. Throws <see cref="JsonException"/> on a shape mismatch.</summary>
    public static PlanJson Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<PlanJson>(json, ManifestLoader.JsonOptions)
            ?? throw new JsonException("Plan JSON is empty (JSON null).");
    }

    /// <summary>The indented JSON text.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, ManifestLoader.JsonOptions);

    /// <summary>Rebuilds the <see cref="NotebookPlan"/>. Throws <see cref="KeyNotFoundException"/> when a section lists a key that has no page.</summary>
    public NotebookPlan ToPlan()
    {
        Dictionary<string, PageSpec> byKey = new(StringComparer.Ordinal);
        foreach (PageJson page in Pages)
        {
            byKey[page.Key] = new PageSpec(new PageKey(page.Key), page.Type, page.Title, page.Level, page.PdfPage, page.Date, page.Tokens);
        }

        List<SectionSpec> sections = Sections
            .Select(s => new SectionSpec(s.Name, s.ColorHex, s.Pages.Select(k => byKey[k]).ToList()) { GroupName = s.GroupName })
            .ToList();
        List<LinkSpec> links = Links.Select(l => new LinkSpec(new PageKey(l.From), l.AnchorId, new PageKey(l.To), l.Text)).ToList();
        return new NotebookPlan(Name, Quarter.ToQuarter(), sections, links);
    }
}

/// <summary>The quarter in JSON: start and end as yyyy-MM-dd, the week count, the day count and the label.</summary>
public sealed record QuarterJson(DateOnly Start, DateOnly End, int WeekCount, int Length, string Label)
{
    public static QuarterJson From(Quarter quarter)
    {
        ArgumentNullException.ThrowIfNull(quarter);
        return new QuarterJson(quarter.Start, quarter.End, quarter.WeekCount, quarter.Length, quarter.Label);
    }

    public Quarter ToQuarter() => new(Start, End, WeekCount, Label);
}

/// <summary>A section in JSON: its pages are listed by key.</summary>
public sealed record SectionJson(string Name, string? ColorHex, string? GroupName, IReadOnlyList<string> Pages);

/// <summary>A page in JSON, with the name of the section that holds it.</summary>
public sealed record PageJson(
    string Key,
    string Section,
    PageType Type,
    string Title,
    int Level,
    int? PdfPage,
    DateOnly? Date,
    IReadOnlyDictionary<string, string> Tokens);

/// <summary>A link in JSON, page keys as strings.</summary>
public sealed record LinkJson(string From, string AnchorId, string To, string Text);
