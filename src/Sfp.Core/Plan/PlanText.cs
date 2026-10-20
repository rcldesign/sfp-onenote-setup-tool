using System.Globalization;

namespace Sfp.Core.Plan;

/// <summary>
/// The plan as an indented text tree: one header line, one counts line, then every section (with its color and group)
/// and its pages indented by level with title, key, PDF page and planner date. Deterministic, so the plan snapshot
/// test and the "plan" verb share it. Links are not listed here; the verb prints a link summary after the tree.
/// </summary>
public static class PlanText
{
    private const string IsoDate = "yyyy-MM-dd";

    /// <summary>The tree as one string with "\n" line ends.</summary>
    public static string Tree(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        WriteTree(plan, writer);
        return writer.ToString();
    }

    /// <summary>Writes the tree to <paramref name="writer"/>.</summary>
    public static void WriteTree(NotebookPlan plan, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine(Header(plan));
        writer.WriteLine(Counts(plan));
        foreach (SectionSpec section in plan.Sections)
        {
            writer.WriteLine(SectionLine(section));
            foreach (PageSpec page in section.Pages)
            {
                writer.WriteLine(PageLine(page));
            }
        }
    }

    /// <summary>"SFP Q1 2027: Q1 2027, 2027-01-01 to 2027-03-31, 14 weeks, 90 days".</summary>
    public static string Header(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Inv($"{plan.Name}: {plan.Quarter.Label}, {Date(plan.Quarter.Start)} to {Date(plan.Quarter.End)}, {plan.Quarter.WeekCount} weeks, {plan.Quarter.Length} days");
    }

    /// <summary>"19 sections, 144 pages".</summary>
    public static string Counts(NotebookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Inv($"{plan.Sections.Count} sections, {plan.Pages.Count()} pages");
    }

    /// <summary>"Week 02 (Jan 4 to Jan 10)  color #E4D6C4  group Weeks" (color and group only when set).</summary>
    public static string SectionLine(SectionSpec section)
    {
        ArgumentNullException.ThrowIfNull(section);
        string line = section.Name;
        if (section.ColorHex is not null)
        {
            line += "  color " + section.ColorHex;
        }

        if (section.GroupName is not null)
        {
            line += "  group " + section.GroupName;
        }

        return line;
    }

    /// <summary>"    Mon Jan 4  [day:2027-01-04]  pdf 26  2027-01-04", indented two spaces per level.</summary>
    public static string PageLine(PageSpec page)
    {
        ArgumentNullException.ThrowIfNull(page);
        string indent = new(' ', 2 * page.Level);
        string pdf = page.PdfPage is int n ? n.ToString(CultureInfo.InvariantCulture) : "none";
        string date = page.Date is DateOnly d ? Date(d) : "none";
        return Inv($"{indent}{page.Title}  [{page.Key}]  pdf {pdf}  {date}");
    }

    private static string Date(DateOnly date) => date.ToString(IsoDate, CultureInfo.InvariantCulture);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
