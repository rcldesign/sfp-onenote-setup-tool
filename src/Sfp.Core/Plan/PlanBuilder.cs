using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Plan;

/// <summary>
/// The production <see cref="IPlanBuilder"/>. Walks manifest.notebook.sections in order and expands every section
/// template (once, or once per week of the quarter) and every page template (once, per objective, per quarter month,
/// per day of the week or per Notes page) into <see cref="PageSpec"/>s carrying key, title, level, PDF page, planner
/// date and the full token set, so the renderer and the writer never recompute a date. Pure: no I/O, no clock.
/// </summary>
/// <remarks>
/// PDF page mapping: fixed types take their page from pageTypes (annual by the template index, objective n from
/// pdfPages[n - 1], calendar month i from pdfPages[i]); week pages take the page of week run min(k, count) so the
/// 14th week reuses the 13th run; the monthly and quarterly reviews reuse the last run's reflection page; contents,
/// setup, scorecard, notes and the blank parent page are generated (PdfPage null).
/// Dates: a day page is dated with its day, an overview with the week's Monday, a reflection with the week's Sunday
/// (or Monday with <see cref="GenerateOptions.ReflectionDateIsWeekStart"/>), a calendar and a monthly review with the
/// 1st of their month, everything else with the quarter start.
/// Tokens: Qn and YYYY from the quarter, then Ddd, Mon, D, Month and YYYY from the page date (so YYYY is the date's
/// year), NN, start and end on week pages, n on objective pages, Note on notes pages, plus the
/// <see cref="Tokens.Extra"/> values. dayOfQuarter on an out-of-quarter day page is the signed offset from the
/// quarter start (0 for the day before it, length + 1 for the day after), with inQuarter "false".
/// </remarks>
public sealed class PlanBuilder : IPlanBuilder
{
    /// <summary>Section group that holds the week sections when <see cref="GenerateOptions.GroupWeeks"/> is set.</summary>
    public const string WeeksGroupName = "Weeks";

    /// <summary>Title patterns used when a page template has no title, from the PRD naming table.</summary>
    public static IReadOnlyDictionary<string, string> DefaultTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PageNames.Cover] = "Cover",
        [PageNames.Contents] = "Contents",
        [PageNames.Setup] = "Setup",
        [PageNames.Annual] = "Annual Objectives",
        [PageNames.Blank] = "Objective Details",
        [PageNames.Objective] = "Objective {n}",
        [PageNames.Calendar] = "{Month} {YYYY}",
        [PageNames.Overview] = "Weekly Overview",
        [PageNames.Day] = "{Ddd} {Mon} {D}",
        [PageNames.Reflection] = "Weekly Reflection",
        [PageNames.Scorecard] = "Quarter scorecard",
        [PageNames.MonthlyReview] = "{Month} Review",
        [PageNames.QuarterlyReview] = "{Qn} {YYYY} Review",
        [PageNames.Notes] = "{Note}",
    };

    private readonly IDateEngine _dates;
    private readonly LinkGraphBuilder _links;

    public PlanBuilder(IDateEngine dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        _dates = dates;
        _links = new LinkGraphBuilder(dates);
    }

    /// <inheritdoc />
    public NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(o);

        Context ctx = new(m, q, o, _dates.Weeks(q));
        List<SectionSpec> sections = new();
        foreach (SectionTemplate template in m.Notebook.Sections)
        {
            if (template.EachWeek)
            {
                foreach (Week week in ctx.Weeks)
                {
                    sections.Add(BuildSection(template, ctx, week));
                }
            }
            else
            {
                sections.Add(BuildSection(template, ctx, null));
            }
        }

        return new NotebookPlan(NotebookName(m, q, o), q, sections, _links.Build(m, q, sections, o));
    }

    /// <summary>The notebook name: the option override when given, else the manifest name pattern with the quarter tokens ("SFP Q1 2027").</summary>
    public static string NotebookName(Manifest m, Quarter q, GenerateOptions o)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(o);
        return string.IsNullOrWhiteSpace(o.NotebookName)
            ? TokenExpander.Expand(m.Notebook.NamePattern, PlannerText.TokenValues(q))
            : o.NotebookName.Trim();
    }

    /// <summary>Monday = 0 ... Sunday = 6, the index into <see cref="PageNames.Days"/> and the overview columns.</summary>
    public static int WeekdayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static SectionSpec BuildSection(SectionTemplate template, Context ctx, Week? week)
    {
        Dictionary<string, string> nameTokens = new(PlannerText.TokenValues(ctx.Quarter), StringComparer.Ordinal);
        string? color = null;
        string? group = null;
        if (week is not null)
        {
            foreach ((string key, string value) in PlannerText.TokenValues(week))
            {
                nameTokens[key] = value;
            }

            IReadOnlyList<string> tints = ctx.Manifest.Colors.SectionTints;
            if (template.ColorByMonth && ctx.Options.ColorSectionsByMonth && tints.Count > 0)
            {
                color = tints[ctx.MonthIndexOf(week.FirstQuarterDay) % tints.Count];
            }

            if (ctx.Options.GroupWeeks)
            {
                group = WeeksGroupName;
            }
        }

        string name = TokenExpander.Expand(template.Name, nameTokens);
        List<PageSpec> pages = new();
        foreach (PageTemplate page in template.Pages)
        {
            pages.AddRange(ExpandPages(page, ctx, week));
        }

        return new SectionSpec(name, color, pages) { GroupName = group };
    }

    private static IEnumerable<PageSpec> ExpandPages(PageTemplate t, Context ctx, Week? week)
    {
        Manifest m = ctx.Manifest;
        Quarter q = ctx.Quarter;
        switch (t.Type)
        {
            case PageNames.Cover:
                return One(Page(t, ctx, week, PageType.Cover, PageKey.Cover, FixedPdfPage(m.PageTypes.Cover, t, PageNames.Cover), PageNames.Cover, q.Start));
            case PageNames.Contents:
                return One(Page(t, ctx, week, PageType.Contents, PageKey.Contents, null, PageNames.Contents, q.Start));
            case PageNames.Setup:
                return One(Page(t, ctx, week, PageType.Setup, PageKey.Setup, null, PageNames.Setup, q.Start));
            case PageNames.Annual:
            {
                int index = t.Index ?? 0;
                return One(Page(t, ctx, week, PageType.Annual, PageKey.Annual(index + 1), FixedPdfPage(m.PageTypes.Annual, t, PageNames.Annual), PageNames.Annual, q.Start));
            }

            case PageNames.Blank:
                return One(Page(t, ctx, week, PageType.Blank, PageKey.ObjectiveDetails, null, PageNames.Blank, q.Start));
            case PageNames.Objective:
                return ObjectivePages(t, ctx, week);
            case PageNames.Calendar:
                return MonthPages(t, ctx, week, PageType.Calendar, PageNames.Calendar);
            case PageNames.Overview:
            {
                Week w = RequireWeek(t, week);
                return One(Page(t, ctx, w, PageType.WeeklyOverview, PageKey.WeekOverview(w.Number), WeeklyPdfPage(m, w.Number, PageNames.Overview), PageNames.Overview, w.MondayStart));
            }

            case PageNames.Day:
                return DayPages(t, ctx, RequireWeek(t, week));
            case PageNames.Reflection:
            {
                Week w = RequireWeek(t, week);
                DateOnly date = ctx.Options.ReflectionDateIsWeekStart ? w.MondayStart : w.SundayEnd;
                return One(Page(t, ctx, w, PageType.WeeklyReflection, PageKey.WeekReflection(w.Number), WeeklyPdfPage(m, w.Number, PageNames.Reflection), PageNames.Reflection, date));
            }

            case PageNames.Scorecard:
                return One(Page(t, ctx, week, PageType.Scorecard, PageKey.Scorecard, null, PageNames.Scorecard, q.Start));
            case PageNames.MonthlyReview:
                return MonthPages(t, ctx, week, PageType.MonthlyReview, PageNames.MonthlyReview);
            case PageNames.QuarterlyReview:
                return One(Page(t, ctx, week, PageType.QuarterlyReview, PageKey.QuarterlyReview, ReviewPdfPage(m), PageNames.QuarterlyReview, q.Start));
            case PageNames.Notes:
                return NotesPages(t, ctx, week);
            default:
                throw new ManifestException($"Page template type '{t.Type}' is not one the plan builder knows. Known types: {string.Join(", ", PageNames.TemplateTypes)}.");
        }
    }

    private static IEnumerable<PageSpec> ObjectivePages(PageTemplate t, Context ctx, Week? week)
    {
        IReadOnlyList<int> pdfPages = ctx.Manifest.PageTypes.Objective.PdfPages;
        foreach (int n in Instances(t.Each, t.Index, pdfPages.Count, PageNames.Objective))
        {
            yield return Page(t, ctx, week, PageType.Objective, PageKey.Objective(n), pdfPages[n - 1], PageNames.Objective, ctx.Quarter.Start, objective: n);
        }
    }

    private static IEnumerable<PageSpec> MonthPages(PageTemplate t, Context ctx, Week? week, PageType type, string pdfPageName)
    {
        Manifest m = ctx.Manifest;
        foreach (int i in Instances(t.EachMonth, t.Index, ctx.Months.Count, t.Type).Select(n => n - 1))
        {
            (int year, int month) = ctx.Months[i];
            DateOnly first = new(year, month, 1);
            PageKey key = type == PageType.Calendar ? PageKey.Calendar(year, month) : PageKey.MonthlyReview(year, month);
            int pdfPage = type == PageType.Calendar ? CalendarPdfPage(m, i) : ReviewPdfPage(m);
            yield return Page(t, ctx, week, type, key, pdfPage, pdfPageName, first, monthIndex: i);
        }
    }

    private static IEnumerable<PageSpec> DayPages(PageTemplate t, Context ctx, Week week)
    {
        IReadOnlyList<DateOnly> days = ctx.Options.IncludeOutOfQuarterDays ? week.AllDays : week.QuarterDays;
        foreach (DateOnly day in days)
        {
            int weekday = WeekdayIndex(day);
            string name = PageNames.Days[weekday];
            yield return Page(
                t, ctx, week, PageType.Day, PageKey.Day(day), WeeklyPdfPage(ctx.Manifest, week.Number, name), name, day,
                inQuarter: ctx.Quarter.Contains(day), weekdayIndex: weekday);
        }
    }

    private static IEnumerable<PageSpec> NotesPages(PageTemplate t, Context ctx, Week? week)
    {
        IReadOnlyList<string> names = ctx.Options.NotesPages;
        foreach (int n in Instances(t.EachNote, t.Index, names.Count, PageNames.Notes))
        {
            string name = names[n - 1].Trim();
            yield return Page(t, ctx, week, PageType.Notes, PageKey.Notes(name), null, PageNames.Notes, ctx.Quarter.Start, note: name);
        }
    }

    /// <summary>
    /// 1-based instance numbers: all of them when the template repeats, else the one at the template's 0-based
    /// <see cref="PageTemplate.Index"/> (default 0, the first), the same rule as the annual and other fixed types.
    /// </summary>
    private static IEnumerable<int> Instances(bool each, int? index0, int count, string typeName)
    {
        if (each)
        {
            return Enumerable.Range(1, count);
        }

        int index = index0 ?? 0;
        if (index < 0 || index >= count)
        {
            string range = count == 0 ? "there are none" : $"the valid indexes are 0 to {Int(count - 1)}";
            throw new ManifestException($"Page template '{typeName}' index {Int(index)} is out of range: {range}.");
        }

        return new[] { index + 1 };
    }

    private static PageSpec Page(
        PageTemplate t,
        Context ctx,
        Week? week,
        PageType type,
        PageKey key,
        int? pdfPage,
        string pdfPageName,
        DateOnly date,
        bool inQuarter = true,
        int? weekdayIndex = null,
        int? monthIndex = null,
        int? objective = null,
        string? note = null)
    {
        Quarter q = ctx.Quarter;
        Dictionary<string, string> tokens = new(StringComparer.Ordinal);
        Add(tokens, PlannerText.TokenValues(q));
        Add(tokens, PlannerText.TokenValues(date));
        if (week is not null)
        {
            Add(tokens, PlannerText.TokenValues(week));
        }

        if (objective is int n)
        {
            tokens[Tokens.ObjectiveNumber] = Int(n);
        }

        if (note is not null)
        {
            tokens[Tokens.NoteName] = note;
        }

        tokens[Tokens.Extra.PdfPageName] = pdfPageName;
        tokens[Tokens.Extra.DateIso] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        tokens[Tokens.Extra.InQuarter] = inQuarter ? "true" : "false";
        tokens[Tokens.Extra.QuarterLength] = Int(q.Length);
        tokens[Tokens.Extra.WeekCount] = Int(q.WeekCount);
        if (week is not null)
        {
            tokens[Tokens.Extra.Week] = Int(week.Number);
            tokens[Tokens.Extra.WeeksRemaining] = Int(week.WeeksRemaining);
        }

        if (type == PageType.Day)
        {
            tokens[Tokens.Extra.DayOfQuarter] = Int(date.DayNumber - q.Start.DayNumber + 1);
            tokens[Tokens.Extra.WeekdayIndex] = Int(weekdayIndex ?? WeekdayIndex(date));
        }

        if (monthIndex is int mi)
        {
            tokens[Tokens.Extra.MonthIndex] = Int(mi);
        }

        if (objective is int oi)
        {
            tokens[Tokens.Extra.ObjectiveIndex] = Int(oi);
        }

        string pattern = t.Title ?? DefaultTitles[t.Type];
        string title = TokenExpander.Expand(pattern, tokens);
        return new PageSpec(key, type, title, t.Level, pdfPage, date, tokens);
    }

    private static void Add(Dictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach ((string key, string value) in source)
        {
            target[key] = value;
        }
    }

    private static int FixedPdfPage(FixedPagesSpec spec, PageTemplate t, string typeName)
    {
        int index = t.Index ?? 0;
        if (index < 0 || index >= spec.PdfPages.Count)
        {
            throw new ManifestException($"Page template '{typeName}' index {Int(index)} is outside pageTypes.{typeName}.pdfPages (0 to {Int(spec.PdfPages.Count - 1)}).");
        }

        return spec.PdfPages[index];
    }

    /// <summary>Calendar month i uses pdfPages[i]; a manifest that lists one calendar page gets consecutive pages from it.</summary>
    private static int CalendarPdfPage(Manifest m, int monthIndex)
    {
        IReadOnlyList<int> pages = m.PageTypes.Calendar.PdfPages;
        return monthIndex < pages.Count ? pages[monthIndex] : pages[0] + monthIndex;
    }

    /// <summary>The reviews reuse the reflection page of the last week run (week 13).</summary>
    private static int ReviewPdfPage(Manifest m) => m.PageTypes.Weekly.PdfPageFor(m.PageTypes.Weekly.Count, PageNames.Reflection);

    /// <summary>Week k uses run min(k, count): the 14th week reuses the 13th run's art.</summary>
    private static int WeeklyPdfPage(Manifest m, int week, string pageName) =>
        m.PageTypes.Weekly.PdfPageFor(Math.Min(week, m.PageTypes.Weekly.Count), pageName);

    private static Week RequireWeek(PageTemplate t, Week? week) =>
        week ?? throw new ManifestException($"Page template type '{t.Type}' is only valid inside a section with eachWeek.");

    private static IEnumerable<PageSpec> One(PageSpec page) => new[] { page };

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Everything one Build call shares.</summary>
    private sealed class Context
    {
        public Context(Manifest manifest, Quarter quarter, GenerateOptions options, IReadOnlyList<Week> weeks)
        {
            Manifest = manifest;
            Quarter = quarter;
            Options = options;
            Weeks = weeks;
            Months = quarter.Months;
        }

        public Manifest Manifest { get; }

        public Quarter Quarter { get; }

        public GenerateOptions Options { get; }

        public IReadOnlyList<Week> Weeks { get; }

        public IReadOnlyList<(int Year, int Month)> Months { get; }

        /// <summary>0, 1 or 2: which quarter month holds the date.</summary>
        public int MonthIndexOf(DateOnly date)
        {
            for (int i = 0; i < Months.Count; i++)
            {
                if (Months[i].Year == date.Year && Months[i].Month == date.Month)
                {
                    return i;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(date), date, Dates.Quarter.OutsideMessage(Quarter));
        }
    }
}
