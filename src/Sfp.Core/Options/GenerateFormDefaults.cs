using Sfp.Core.Dates;
using Sfp.Core.Manifests;

namespace Sfp.Core.Options;

/// <summary>The default values of the Generate form fields, as pure functions of "today", the manifest and the quarter.</summary>
public static class GenerateFormDefaults
{
    /// <summary>Folder name under Documents where OneNote keeps local notebooks.</summary>
    public const string NotebooksFolderName = "OneNote Notebooks";

    /// <summary>The first month of the next calendar quarter after the one holding <paramref name="today"/>: Oct 20, 2026 gives January 2027.</summary>
    public static QuarterSpec NextQuarterStart(DateOnly today)
    {
        int nextQuarterIndex = ((today.Month - 1) / 3) + 1;
        int month = (nextQuarterIndex * 3) + 1;
        int year = today.Year;
        if (month > 12)
        {
            month = 1;
            year++;
        }

        return new QuarterSpec(year, month);
    }

    /// <summary>The notebook name from the manifest pattern ("SFP {Qn} {YYYY}") for a quarter.</summary>
    public static string NotebookName(string namePattern, Quarter quarter)
    {
        ArgumentNullException.ThrowIfNull(namePattern);
        ArgumentNullException.ThrowIfNull(quarter);
        return TokenExpander.Expand(namePattern, PlannerText.TokenValues(quarter));
    }

    /// <summary>Documents\OneNote Notebooks for the given Documents folder.</summary>
    public static string OutputFolder(string documentsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsFolder);
        return Path.Combine(documentsFolder, NotebooksFolderName);
    }

    /// <summary>
    /// Pages a notebook holds for a quarter: Quarter 3 (cover, contents, setup), Objectives 11, Calendar 3, one overview and one
    /// reflection per week plus one page per quarter day, Reviews 5, Notes 4. Q1 2027 gives 144, the PRD's worked example.
    /// </summary>
    public static int PageCount(Quarter quarter)
    {
        ArgumentNullException.ThrowIfNull(quarter);
        const int fixedPages = 3 + 11 + 3 + 5 + 4;
        return fixedPages + (2 * quarter.WeekCount) + quarter.Length;
    }
}
