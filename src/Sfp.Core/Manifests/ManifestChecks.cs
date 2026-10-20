using System.Globalization;

namespace Sfp.Core.Manifests;

/// <summary>
/// Structural sanity and token validation run on every loaded manifest: page size and count, the nine-page week run,
/// PDF page numbers inside the document, known page names in every <c>appliesTo</c> list and page template, and the
/// PRD token set in every section name and page title pattern. Each failure is a <see cref="ManifestException"/>
/// naming the manifest and the offending value.
/// </summary>
public static class ManifestChecks
{
    /// <summary>The nine pages of a week run: overview, seven days, reflection.</summary>
    public const int WeeklyRunLength = 9;

    /// <summary>OneNote supports page levels 1 to 3.</summary>
    public const int MaxPageLevel = 3;

    /// <summary>Runs every check; <paramref name="sourceName"/> names the manifest in error messages.</summary>
    public static void Run(Manifest manifest, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(sourceName);
        CheckPage(manifest, sourceName);
        CheckPageTypes(manifest, sourceName);
        CheckSlots(manifest, sourceName);
        CheckNotebook(manifest, sourceName);
    }

    private static void CheckPage(Manifest m, string source)
    {
        if (m.PageSizePt.Count != 2)
        {
            Fail(source, $"pageSizePt must hold [width, height], found {m.PageSizePt.Count} entries.");
        }

        if (m.PageSizePt[0] <= 0 || m.PageSizePt[1] <= 0)
        {
            Fail(source, $"pageSizePt must be positive, found [{Pt(m.PageSizePt[0])}, {Pt(m.PageSizePt[1])}].");
        }

        if (m.PageCount <= 0)
        {
            Fail(source, $"pageCount must be greater than 0, found {m.PageCount.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void CheckPageTypes(Manifest m, string source)
    {
        foreach ((string name, FixedPagesSpec spec) in FixedPageTypes(m.PageTypes))
        {
            if (spec.PdfPages.Count == 0)
            {
                Fail(source, $"pageTypes.{name}.pdfPages must list at least one page.");
            }

            foreach (int page in spec.PdfPages)
            {
                if (page < 1 || page > m.PageCount)
                {
                    Fail(source, $"pageTypes.{name}.pdfPages holds page {Int(page)}, outside 1 to {Int(m.PageCount)}.");
                }
            }

            if (string.IsNullOrWhiteSpace(spec.Probe))
            {
                Fail(source, $"pageTypes.{name}.probe must not be empty.");
            }
        }

        WeeklyPagesSpec weekly = m.PageTypes.Weekly;
        if (weekly.Pages.Count != WeeklyRunLength)
        {
            Fail(source, $"pageTypes.weekly.pages must list {Int(WeeklyRunLength)} names (overview, mon to sun, reflection), found {Int(weekly.Pages.Count)}.");
        }

        if (weekly.Pages.Distinct(StringComparer.Ordinal).Count() != weekly.Pages.Count)
        {
            Fail(source, "pageTypes.weekly.pages holds a duplicate name.");
        }

        foreach (string page in weekly.Pages)
        {
            if (!PageNames.IsKnown(page))
            {
                Fail(source, $"pageTypes.weekly.pages holds unknown page name '{page}'.");
            }

            if (!weekly.Probes.ContainsKey(page))
            {
                Fail(source, $"pageTypes.weekly.probes has no probe text for '{page}'.");
            }
        }

        if (weekly.Count <= 0 || weekly.Stride <= 0 || weekly.FirstPdfPage < 1)
        {
            Fail(source, "pageTypes.weekly needs firstPdfPage >= 1, stride > 0 and count > 0.");
        }

        int lastPage = weekly.FirstPdfPage + (weekly.Stride * (weekly.Count - 1)) + weekly.Pages.Count - 1;
        if (lastPage > m.PageCount)
        {
            Fail(source, $"pageTypes.weekly ends on page {Int(lastPage)}, beyond pageCount {Int(m.PageCount)}.");
        }
    }

    private static void CheckSlots(Manifest m, string source)
    {
        foreach ((string slot, IReadOnlyList<string> appliesTo) in m.Slots.AppliesToBySlot())
        {
            if (appliesTo.Count == 0)
            {
                Fail(source, $"slots.{slot}.appliesTo must name at least one page.");
            }

            foreach (string page in appliesTo)
            {
                if (!PageNames.IsKnown(page))
                {
                    Fail(source, $"slots.{slot}.appliesTo holds unknown page name '{page}'. Known names: {string.Join(", ", PageNames.All)}.");
                }
            }
        }
    }

    private static void CheckNotebook(Manifest m, string source)
    {
        string[] patternTokens = Tokens.All.Where(t => t != Tokens.NoteName).ToArray();
        ValidatePattern(source, "notebook.namePattern", m.Notebook.NamePattern, patternTokens);

        if (m.Notebook.Sections.Count == 0)
        {
            Fail(source, "notebook.sections must hold at least one section.");
        }

        for (int s = 0; s < m.Notebook.Sections.Count; s++)
        {
            SectionTemplate section = m.Notebook.Sections[s];
            string sectionPath = $"notebook.sections[{Int(s)}]";
            ValidatePattern(source, $"{sectionPath}.name", section.Name, patternTokens);

            if (section.Pages.Count == 0)
            {
                Fail(source, $"{sectionPath} ('{section.Name}') has no pages.");
            }

            for (int p = 0; p < section.Pages.Count; p++)
            {
                PageTemplate page = section.Pages[p];
                string pagePath = $"{sectionPath}.pages[{Int(p)}]";
                if (!PageNames.IsTemplateType(page.Type))
                {
                    Fail(source, $"{pagePath}.type '{page.Type}' is not a page type. Known types: {string.Join(", ", PageNames.TemplateTypes)}.");
                }

                if (page.Level < 1 || page.Level > MaxPageLevel)
                {
                    Fail(source, $"{pagePath}.level must be 1 to {Int(MaxPageLevel)}, found {Int(page.Level)}.");
                }

                if (page.Title is not null)
                {
                    IEnumerable<string> allowed = page.EachNote ? Tokens.All : patternTokens;
                    ValidatePattern(source, $"{pagePath}.title", page.Title, allowed);
                }
            }
        }
    }

    private static void ValidatePattern(string source, string path, string pattern, IEnumerable<string> allowed)
    {
        try
        {
            TokenExpander.Validate(pattern, allowed);
        }
        catch (ManifestException ex)
        {
            throw new ManifestException($"Manifest '{source}': {path}: {ex.Message}", ex);
        }
    }

    private static IEnumerable<(string Name, FixedPagesSpec Spec)> FixedPageTypes(PageTypesSpec types)
    {
        yield return (PageNames.Cover, types.Cover);
        yield return (PageNames.Contents, types.Contents);
        yield return (PageNames.Annual, types.Annual);
        yield return (PageNames.Objective, types.Objective);
        yield return (PageNames.Calendar, types.Calendar);
    }

    /// <summary>The fixed page types of a manifest with their manifest names, in PDF order.</summary>
    public static IReadOnlyList<(string Name, FixedPagesSpec Spec)> FixedPageTypesOf(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return FixedPageTypes(manifest.PageTypes).ToList();
    }

    private static void Fail(string source, string message) => throw new ManifestException($"Manifest '{source}': {message}");

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Pt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
