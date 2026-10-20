namespace Sfp.Core.Manifests;

/// <summary>
/// The page names the manifest uses in slot and link <c>appliesTo</c> lists and in notebook page templates. Slots name the
/// seven weekdays individually (mon ... sun); a page template uses <see cref="Day"/> with <c>each: true</c> for all of them.
/// The loader rejects any other name at load time.
/// </summary>
public static class PageNames
{
    public const string Cover = "cover";
    public const string Contents = "contents";
    public const string Setup = "setup";
    public const string Annual = "annual";
    public const string Blank = "blank";
    public const string Objective = "objective";
    public const string Calendar = "calendar";
    public const string Overview = "overview";
    public const string Mon = "mon";
    public const string Tue = "tue";
    public const string Wed = "wed";
    public const string Thu = "thu";
    public const string Fri = "fri";
    public const string Sat = "sat";
    public const string Sun = "sun";
    public const string Reflection = "reflection";
    public const string Scorecard = "scorecard";
    public const string MonthlyReview = "monthlyReview";
    public const string QuarterlyReview = "quarterlyReview";
    public const string Notes = "notes";

    /// <summary>Page template type standing for the seven weekday pages.</summary>
    public const string Day = "day";

    /// <summary>The weekday page names, Monday first.</summary>
    public static IReadOnlyList<string> Days { get; } = new[] { Mon, Tue, Wed, Thu, Fri, Sat, Sun };

    /// <summary>Every name a slot or link <c>appliesTo</c> entry may use.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Cover, Contents, Annual, Objective, Calendar, Overview, Mon, Tue, Wed, Thu, Fri, Sat, Sun, Reflection,
        Setup, Scorecard, MonthlyReview, QuarterlyReview, Notes, Blank,
    };

    /// <summary>Every type a notebook page template may declare (the weekdays collapse into <see cref="Day"/>).</summary>
    public static IReadOnlyList<string> TemplateTypes { get; } = new[]
    {
        Cover, Contents, Setup, Annual, Blank, Objective, Calendar, Overview, Day, Reflection,
        Scorecard, MonthlyReview, QuarterlyReview, Notes,
    };

    /// <summary>True when the name is a valid <c>appliesTo</c> entry.</summary>
    public static bool IsKnown(string name) => All.Contains(name, StringComparer.Ordinal);

    /// <summary>True when the name is a valid page template type.</summary>
    public static bool IsTemplateType(string name) => TemplateTypes.Contains(name, StringComparer.Ordinal);
}
