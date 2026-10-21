using System.Globalization;

namespace Sfp.Core.Manifests;

/// <summary>
/// Structural sanity and token validation run on every loaded manifest: no null where the record needs a value, page
/// size and count, the nine-page week run, PDF page numbers inside the document, known page names in every
/// <c>appliesTo</c> list and page template, every erase rectangle containing the printed extent it removes, a stacked
/// day inset that leaves room in the day box, and the PRD token set in every section name and page title pattern. Each
/// failure is a <see cref="ManifestException"/> naming the manifest and the offending value.
/// </summary>
public static class ManifestChecks
{
    /// <summary>The nine pages of a week run: overview, seven days, reflection.</summary>
    public const int WeeklyRunLength = 9;

    /// <summary>OneNote supports page levels 1 to 3.</summary>
    public const int MaxPageLevel = 3;

    /// <summary>
    /// Runs every check; <paramref name="sourceName"/> names the manifest in error messages. The null walk runs first
    /// (a user manifest's {"slots": {"date": null}} is reported as "$.slots.date must not be null" instead of failing
    /// later with a NullReferenceException), so the remaining checks can rely on every non-nullable member.
    /// </summary>
    public static void Run(Manifest manifest, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(sourceName);
        ManifestNullChecks.Run(manifest, sourceName);
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

        SlotsSpec slots = m.Slots;
        CheckEraseCovers(source, "slots.date.slashErase", slots.Date.SlashErase, "slots.date.slashPrinted", slots.Date.SlashPrinted);
        CheckEraseCovers(source, "slots.weeksRemaining.erase", slots.WeeksRemaining.Erase, "slots.weeksRemaining.printed", slots.WeeksRemaining.Printed);
        CheckEraseCovers(source, "slots.timeBlocks.erase", slots.TimeBlocks.Erase, "slots.timeBlocks.printed", slots.TimeBlocks.Printed);

        DayGridSlot grid = slots.DayGrid;
        if (grid.StackedInsetPt < 0 || 2 * grid.StackedInsetPt >= grid.BoxPt)
        {
            Fail(source, $"slots.dayGrid.stackedInsetPt must be at least 0 and leave room inside the {Pt(grid.BoxPt)} pt day box (slots.dayGrid.boxPt) on both sides, found {Pt(grid.StackedInsetPt)}.");
        }
    }

    /// <summary>
    /// An erase rectangle must contain the measured extent of the printed art it removes, or a sliver of that art stays
    /// beside the overlay drawn back into it. Names both keys, both rectangles and every side that falls short.
    /// </summary>
    private static void CheckEraseCovers(string source, string eraseKey, RectPt erase, string printedKey, RectPt printed)
    {
        if (printed.W <= 0 || printed.H <= 0)
        {
            Fail(source, $"{printedKey} {Rect(printed)} must have a positive width and height.");
        }

        List<string> misses = new();
        if (erase.X > printed.X)
        {
            misses.Add($"its left edge {Pt(erase.X)} is right of {Pt(printed.X)}");
        }

        if (erase.Y > printed.Y)
        {
            misses.Add($"its top {Pt(erase.Y)} is below {Pt(printed.Y)}");
        }

        if (erase.Right < printed.Right)
        {
            misses.Add($"its right edge {Pt(erase.Right)} is left of {Pt(printed.Right)}");
        }

        if (erase.Bottom < printed.Bottom)
        {
            misses.Add($"its bottom {Pt(erase.Bottom)} is above {Pt(printed.Bottom)}");
        }

        if (misses.Count > 0)
        {
            Fail(source, $"{eraseKey} {Rect(erase)} must contain {printedKey} {Rect(printed)}, the printed art it erases: {string.Join("; ", misses)}.");
        }
    }

    /// <summary>
    /// The tokens the notebook name pattern may use: the quarter tokens {Qn} and {YYYY}, the only values the plan
    /// builder (and every other place that names a notebook) supplies for it.
    /// </summary>
    public static IReadOnlyList<string> NamePatternTokens { get; } = InCanonicalOrder(Tokens.QuarterLabel, Tokens.Year);

    /// <summary>
    /// The tokens a section name may use: {Qn} and {YYYY}, plus the week tokens {NN}, {start} and {end} when the
    /// section repeats per week (eachWeek). These are exactly the values the plan builder supplies for a section name.
    /// </summary>
    public static IReadOnlyList<string> SectionNameTokens(bool eachWeek) =>
        eachWeek ? SectionTokensEachWeek : SectionTokensOnce;

    /// <summary>
    /// The tokens a page title may use, by context: the quarter and date tokens ({Qn}, {YYYY}, {Ddd}, {Mon}, {D},
    /// {Month}) everywhere; {NN}, {start} and {end} inside an eachWeek section; {n} on objective templates; {Note} on
    /// notes templates with eachNote. A title that passes this check always expands in the plan builder.
    /// </summary>
    public static IReadOnlyList<string> TitleTokens(PageTemplate page, bool inEachWeekSection)
    {
        ArgumentNullException.ThrowIfNull(page);
        List<string> tokens = new(QuarterAndDateTokens);
        if (inEachWeekSection)
        {
            tokens.AddRange(WeekTokens);
        }

        if (page.Type == PageNames.Objective)
        {
            tokens.Add(Tokens.ObjectiveNumber);
        }

        if (page.Type == PageNames.Notes && page.EachNote)
        {
            tokens.Add(Tokens.NoteName);
        }

        return InCanonicalOrder(tokens.ToArray());
    }

    private static readonly string[] WeekTokens = { Tokens.WeekNumber, Tokens.WeekStart, Tokens.WeekEnd };

    private static readonly IReadOnlyList<string> QuarterAndDateTokens = InCanonicalOrder(
        Tokens.QuarterLabel, Tokens.Year, Tokens.DayAbbrev, Tokens.MonthAbbrev, Tokens.DayOfMonth, Tokens.MonthName);

    private static readonly IReadOnlyList<string> SectionTokensOnce = InCanonicalOrder(Tokens.QuarterLabel, Tokens.Year);

    private static readonly IReadOnlyList<string> SectionTokensEachWeek = InCanonicalOrder(
        Tokens.QuarterLabel, Tokens.Year, Tokens.WeekNumber, Tokens.WeekStart, Tokens.WeekEnd);

    private static void CheckNotebook(Manifest m, string source)
    {
        ValidatePattern(source, "notebook.namePattern", m.Notebook.NamePattern, NamePatternTokens, "in the notebook name pattern");

        if (m.Notebook.Sections.Count == 0)
        {
            Fail(source, "notebook.sections must hold at least one section.");
        }

        for (int s = 0; s < m.Notebook.Sections.Count; s++)
        {
            SectionTemplate section = m.Notebook.Sections[s];
            string sectionPath = $"notebook.sections[{Int(s)}]";
            string sectionContext = section.EachWeek ? "in the name of a section with eachWeek" : "in the name of a section without eachWeek";
            ValidatePattern(source, $"{sectionPath}.name", section.Name, SectionNameTokens(section.EachWeek), sectionContext);

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
                    string pageContext = $"on a '{page.Type}' page{(page.EachNote ? " with eachNote" : "")} {(section.EachWeek ? "inside" : "outside")} an eachWeek section";
                    ValidatePattern(source, $"{pagePath}.title", page.Title, TitleTokens(page, section.EachWeek), pageContext);
                }
            }
        }
    }

    /// <summary>
    /// Rejects a pattern with an unknown token or a stray brace (the PRD token set), then any known token that has no
    /// value in this context, naming the path, the token, the pattern, the context and the tokens available there.
    /// </summary>
    private static void ValidatePattern(string source, string path, string pattern, IReadOnlyList<string> available, string context)
    {
        try
        {
            TokenExpander.Validate(pattern, Tokens.All);
        }
        catch (ManifestException ex)
        {
            throw new ManifestException($"Manifest '{source}': {path}: {ex.Message}", ex);
        }

        foreach (string token in TokenExpander.TokensIn(pattern))
        {
            if (!available.Contains(token, StringComparer.Ordinal))
            {
                string list = string.Join(", ", available.Select(t => "{" + t + "}"));
                Fail(source, $"{path}: token {{{token}}} in '{pattern}' has no value {context}. Tokens available there: {list}.");
            }
        }
    }

    /// <summary>The given tokens in the canonical order of <see cref="Tokens.All"/>, each once.</summary>
    private static IReadOnlyList<string> InCanonicalOrder(params string[] tokens) =>
        Tokens.All.Where(t => tokens.Contains(t, StringComparer.Ordinal)).ToArray();

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

    private static string Rect(RectPt r) => $"(x {Pt(r.X)}, y {Pt(r.Y)}, w {Pt(r.W)}, h {Pt(r.H)})";
}
