namespace Sfp.Core.Plan;

/// <summary>One page of the notebook plan.</summary>
/// <param name="Key">Stable page key.</param>
/// <param name="Type">Page type.</param>
/// <param name="Title">The OneNote page title as shown in the page list, for example "Mon Jan 4".</param>
/// <param name="Level">OneNote page level, 1 or 2 (3 is allowed by OneNote but unused).</param>
/// <param name="PdfPage">1-based PDF page whose art this page uses, or null for a generated or native page.</param>
/// <param name="Date">The planner date of the page (daily: the day; overview: the Monday; reflection: the Sunday; calendar and reviews: the 1st of the month; cover, setup and the rest: the quarter start).</param>
/// <param name="Tokens">Expanded token values for this page: NN, n, start, end, Ddd, Mon, D, Month, YYYY, Qn, and extras the renderer needs (week, dayOfQuarter, quarterLength, weeksRemaining, weekCount).</param>
public sealed record PageSpec(
    PageKey Key,
    PageType Type,
    string Title,
    int Level,
    int? PdfPage,
    DateOnly? Date,
    IReadOnlyDictionary<string, string> Tokens)
{
    /// <summary>Convenience accessor for a token with a clear error when missing.</summary>
    public string Token(string name) =>
        Tokens.TryGetValue(name, out string? v) ? v : throw new KeyNotFoundException($"Page {Key} has no token '{name}'.");

    /// <summary>Convenience accessor for an integer token.</summary>
    public int IntToken(string name) => int.Parse(Token(name), System.Globalization.CultureInfo.InvariantCulture);
}
