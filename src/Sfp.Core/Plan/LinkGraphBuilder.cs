using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Plan;

/// <summary>
/// Builds every <see cref="LinkSpec"/> of a plan from its sections: the PRD link map (objectives both ways, calendar
/// days and week labels, daily dates, overview day headers), the navigation links (prev and next on days, overviews,
/// reflections and calendars; reflection to the next overview), and the review links (reflection and quarterly review
/// to the scorecard, scorecard columns to reflections, calendar to its monthly review, last reflection to the quarterly
/// review, notes pages to Contents). Links are emitted in page order and only when both ends exist in the plan, so
/// every LinkSpec.To resolves. The "nextQuarter" slot of the quarterly review is a reserved placement only, filled by
/// a later Generate with --previous, so no LinkSpec is emitted for it.
/// </summary>
/// <remarks>
/// AnchorIds, exactly as the renderer's AnchorPlacements name them: annualObjective:{n}, objectiveBack,
/// calendarDay:{yyyy-MM-dd}, calendarWeek:{NN}, dailyDate, prev, next, overviewDayHeader:{i}, reflectionNext,
/// progressScorecard, reflectionQuarterReview, prevCalendar, nextCalendar, calendarReview, scorecardWeek:{NN},
/// quarterReviewScorecard, notesContents.
/// Calendar day anchors cover the in-month days only: out-of-month days are baked greyed per the PRD calendar rules
/// (GenerateOptions.OutOfMonthDays), and a stacked sixth-row day shares its box with the fifth-row day, so neither gets
/// an anchor. Out-of-quarter day pages (IncludeOutOfQuarterDays) sit in the prev and next chain but get no dailyDate
/// anchor (their date is baked at 40% ink) and no overview day header.
/// </remarks>
public sealed class LinkGraphBuilder
{
    /// <summary>The placeholder a navigation textFormat uses for the neighbor's title.</summary>
    public const string TitleToken = "title";

    private const string AnnualObjective = "annualObjective";
    private const string ObjectiveBack = "objectiveBack";
    private const string CalendarDay = "calendarDay";
    private const string CalendarWeek = "calendarWeek";
    private const string DailyDate = "dailyDate";
    private const string OverviewDayHeader = "overviewDayHeader";
    private const string Prev = "prev";
    private const string Next = "next";
    private const string PrevCalendar = "prevCalendar";
    private const string NextCalendar = "nextCalendar";
    private const string ReflectionNext = "reflectionNext";
    private const string ProgressScorecard = "progressScorecard";
    private const string ReflectionQuarterReview = "reflectionQuarterReview";
    private const string CalendarReview = "calendarReview";
    private const string ScorecardWeek = "scorecardWeek";
    private const string QuarterReviewScorecard = "quarterReviewScorecard";
    private const string NotesContents = "notesContents";

    private readonly IDateEngine _dates;

    public LinkGraphBuilder(IDateEngine dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        _dates = dates;
    }

    /// <summary>Every link of the plan whose sections are given, in page order; empty when <see cref="GenerateOptions.Links"/> is off.</summary>
    public IReadOnlyList<LinkSpec> Build(Manifest m, Quarter q, IReadOnlyList<SectionSpec> sections, GenerateOptions o)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(o);
        if (!o.Links)
        {
            return Array.Empty<LinkSpec>();
        }

        Index index = new(m, q, o, sections, _dates.Weeks(q));
        List<LinkSpec> links = new();
        foreach (PageSpec page in index.Pages)
        {
            switch (page.Type)
            {
                case PageType.Annual:
                    AnnualLinks(page, index, links);
                    break;
                case PageType.Objective:
                    ObjectiveLinks(page, index, links);
                    break;
                case PageType.Calendar:
                    CalendarLinks(page, index, links);
                    break;
                case PageType.Day:
                    DayLinks(page, index, links);
                    break;
                case PageType.WeeklyOverview:
                    OverviewLinks(page, index, links);
                    break;
                case PageType.WeeklyReflection:
                    ReflectionLinks(page, index, links);
                    break;
                case PageType.Scorecard:
                    ScorecardLinks(page, index, links);
                    break;
                case PageType.QuarterlyReview:
                    QuarterlyReviewLinks(page, index, links);
                    break;
                case PageType.Notes:
                    NotesLinks(page, index, links);
                    break;
                default:
                    break;
            }
        }

        return links;
    }

    /// <summary>"Week 02": the short week label used as the title of a week neighbor in navigation links.</summary>
    public static string WeekLabel(int week) => "Week " + PlannerText.WeekNumber(week);

    /// <summary>"Objective 3": the text of both objective links.</summary>
    public static string ObjectiveLabel(int n) => "Objective " + n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Fills an anchor's textFormat ("‹ {title}") with the target's title; an anchor without a format shows the title itself.</summary>
    public static string LinkText(FixedAnchor anchor, string title)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(title);
        return anchor.TextFormat is null
            ? title
            : TokenExpander.Expand(anchor.TextFormat, new Dictionary<string, string>(StringComparer.Ordinal) { [TitleToken] = title });
    }

    private static void AnnualLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        for (int n = 1; n <= index.ObjectiveCount; n++)
        {
            PageKey objective = PageKey.Objective(n);
            if (index.AnnualPageFor(n) == page.Key && index.Has(objective))
            {
                links.Add(new LinkSpec(page.Key, Instance(AnnualObjective, Int(n)), objective, ObjectiveLabel(n)));
            }
        }
    }

    private static void ObjectiveLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        int n = page.IntToken(Tokens.ObjectiveNumber);
        PageKey annual = index.AnnualPageFor(n);
        if (index.Has(annual))
        {
            links.Add(new LinkSpec(page.Key, ObjectiveBack, annual, ObjectiveLabel(n)));
        }
    }

    private void CalendarLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        DateOnly first = page.Date ?? throw new InvalidOperationException($"Calendar page {page.Key} has no date.");
        MonthGrid grid = _dates.GridWithMode(first.Year, first.Month, index.Quarter, index.Options.SixthRow);
        foreach (IReadOnlyList<DayCell> row in grid.Rows)
        {
            foreach (DayCell cell in row)
            {
                PageKey day = PageKey.Day(cell.Date);
                if (cell.InMonth && cell.InQuarter && index.Has(day))
                {
                    string date = cell.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    links.Add(new LinkSpec(page.Key, Instance(CalendarDay, date), day, Int(cell.Date.Day)));
                }
            }

            if (index.WeekByMonday.TryGetValue(row[0].Date, out Week? week) && index.Has(PageKey.WeekOverview(week.Number)))
            {
                string nn = PlannerText.WeekNumber(week.Number);
                links.Add(new LinkSpec(page.Key, Instance(CalendarWeek, nn), PageKey.WeekOverview(week.Number), WeekLabel(week.Number)));
            }
        }

        Neighbors(page, index.Calendars, index.Links.PrevCalendar, PrevCalendar, index.Links.NextCalendar, NextCalendar, links);

        PageSpec? review = index.Find(PageKey.MonthlyReview(first.Year, first.Month));
        if (review is not null)
        {
            links.Add(new LinkSpec(page.Key, CalendarReview, review.Key, LinkText(index.Links.CalendarReview, review.Title)));
        }
    }

    private static void DayLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        DateOnly date = page.Date ?? throw new InvalidOperationException($"Day page {page.Key} has no date.");
        PageKey overview = PageKey.WeekOverview(page.IntToken(Tokens.Extra.Week));
        if (index.Quarter.Contains(date) && index.Has(overview))
        {
            string text = PlannerText.DateSlot(date, index.Options.DateOrder, index.Options.PadDateFields).Text;
            links.Add(new LinkSpec(page.Key, DailyDate, overview, text));
        }

        Neighbors(page, index.Days, index.Links.Prev, Prev, index.Links.Next, Next, links);
    }

    private static void OverviewLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        Week week = index.Weeks[page.IntToken(Tokens.Extra.Week) - 1];
        for (int i = 0; i < 7; i++)
        {
            DateOnly date = week.MondayStart.AddDays(i);
            PageKey day = PageKey.Day(date);
            if (index.Quarter.Contains(date) && index.Has(day))
            {
                links.Add(new LinkSpec(page.Key, Instance(OverviewDayHeader, Int(i)), day, DateTimeFormatInfo.InvariantInfo.GetDayName(date.DayOfWeek)));
            }
        }

        WeekNeighbors(page, index.Overviews, index, links);
    }

    private static void ReflectionLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        int week = page.IntToken(Tokens.Extra.Week);
        WeekNeighbors(page, index.Reflections, index, links);

        PageKey nextOverview = PageKey.WeekOverview(week + 1);
        if (index.Has(nextOverview))
        {
            links.Add(new LinkSpec(page.Key, ReflectionNext, nextOverview, LinkText(index.Links.ReflectionNext, WeekLabel(week + 1))));
        }

        PageSpec? scorecard = index.Find(PageKey.Scorecard);
        if (scorecard is not null)
        {
            links.Add(new LinkSpec(page.Key, ProgressScorecard, scorecard.Key, LinkText(index.Links.ProgressScorecard, scorecard.Title)));
        }

        PageSpec? quarterReview = index.Find(PageKey.QuarterlyReview);
        if (quarterReview is not null && index.Reflections[^1].Key == page.Key)
        {
            links.Add(new LinkSpec(page.Key, ReflectionQuarterReview, quarterReview.Key, LinkText(index.Links.ReflectionQuarterReview, quarterReview.Title)));
        }
    }

    private static void ScorecardLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        foreach (PageSpec reflection in index.Reflections)
        {
            string nn = PlannerText.WeekNumber(reflection.IntToken(Tokens.Extra.Week));
            links.Add(new LinkSpec(page.Key, Instance(ScorecardWeek, nn), reflection.Key, nn));
        }
    }

    private static void QuarterlyReviewLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        PageSpec? scorecard = index.Find(PageKey.Scorecard);
        if (scorecard is not null)
        {
            links.Add(new LinkSpec(page.Key, QuarterReviewScorecard, scorecard.Key, LinkText(index.Links.QuarterReviewScorecard, scorecard.Title)));
        }
    }

    private static void NotesLinks(PageSpec page, Index index, List<LinkSpec> links)
    {
        PageSpec? contents = index.Find(PageKey.Contents);
        if (contents is not null)
        {
            links.Add(new LinkSpec(page.Key, NotesContents, contents.Key, LinkText(index.Links.NotesContents, contents.Title)));
        }
    }

    /// <summary>prev and next among the overviews or the reflections, titled by week label.</summary>
    private static void WeekNeighbors(PageSpec page, IReadOnlyList<PageSpec> siblings, Index index, List<LinkSpec> links)
    {
        int i = IndexOf(siblings, page.Key);
        if (i > 0)
        {
            PageSpec prev = siblings[i - 1];
            links.Add(new LinkSpec(page.Key, Prev, prev.Key, LinkText(index.Links.Prev, WeekLabel(prev.IntToken(Tokens.Extra.Week)))));
        }

        if (i >= 0 && i < siblings.Count - 1)
        {
            PageSpec next = siblings[i + 1];
            links.Add(new LinkSpec(page.Key, Next, next.Key, LinkText(index.Links.Next, WeekLabel(next.IntToken(Tokens.Extra.Week)))));
        }
    }

    /// <summary>prev and next among siblings, titled by the neighbor page's title.</summary>
    private static void Neighbors(
        PageSpec page, IReadOnlyList<PageSpec> siblings, FixedAnchor prevAnchor, string prevId, FixedAnchor nextAnchor, string nextId, List<LinkSpec> links)
    {
        int i = IndexOf(siblings, page.Key);
        if (i > 0)
        {
            PageSpec prev = siblings[i - 1];
            links.Add(new LinkSpec(page.Key, prevId, prev.Key, LinkText(prevAnchor, prev.Title)));
        }

        if (i >= 0 && i < siblings.Count - 1)
        {
            PageSpec next = siblings[i + 1];
            links.Add(new LinkSpec(page.Key, nextId, next.Key, LinkText(nextAnchor, next.Title)));
        }
    }

    private static int IndexOf(IReadOnlyList<PageSpec> pages, PageKey key)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Instance(string anchorType, string instance) => anchorType + ":" + instance;

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Lookups over one plan's pages.</summary>
    private sealed class Index
    {
        private readonly Dictionary<PageKey, PageSpec> _byKey;
        private readonly int _blocksPerAnnualPage;

        public Index(Manifest manifest, Quarter quarter, GenerateOptions options, IReadOnlyList<SectionSpec> sections, IReadOnlyList<Week> weeks)
        {
            Quarter = quarter;
            Options = options;
            Links = manifest.Links;
            Weeks = weeks;
            Pages = sections.SelectMany(s => s.Pages).ToList();
            _byKey = new Dictionary<PageKey, PageSpec>();
            foreach (PageSpec page in Pages)
            {
                if (!_byKey.TryAdd(page.Key, page))
                {
                    throw new InvalidOperationException($"The plan holds page key '{page.Key}' twice.");
                }
            }

            Days = Pages.Where(p => p.Type == PageType.Day).ToList();
            Overviews = Pages.Where(p => p.Type == PageType.WeeklyOverview).ToList();
            Reflections = Pages.Where(p => p.Type == PageType.WeeklyReflection).ToList();
            Calendars = Pages.Where(p => p.Type == PageType.Calendar).ToList();
            WeekByMonday = weeks.ToDictionary(w => w.MondayStart);
            ObjectiveCount = manifest.PageTypes.Objective.PdfPages.Count;
            _blocksPerAnnualPage = Math.Max(1, manifest.Links.AnnualObjective.BlockTops.Count);
        }

        public Quarter Quarter { get; }

        public GenerateOptions Options { get; }

        public LinksSpec Links { get; }

        public IReadOnlyList<Week> Weeks { get; }

        public IReadOnlyList<PageSpec> Pages { get; }

        public IReadOnlyList<PageSpec> Days { get; }

        public IReadOnlyList<PageSpec> Overviews { get; }

        public IReadOnlyList<PageSpec> Reflections { get; }

        public IReadOnlyList<PageSpec> Calendars { get; }

        public IReadOnlyDictionary<DateOnly, Week> WeekByMonday { get; }

        public int ObjectiveCount { get; }

        public bool Has(PageKey key) => _byKey.ContainsKey(key);

        public PageSpec? Find(PageKey key) => _byKey.TryGetValue(key, out PageSpec? page) ? page : null;

        /// <summary>The annual page holding objective block n: page 1 for 1 to 4, page 2 for 5 to 8 (four blocks per page).</summary>
        public PageKey AnnualPageFor(int n) => PageKey.Annual(((n - 1) / _blocksPerAnnualPage) + 1);
    }
}
