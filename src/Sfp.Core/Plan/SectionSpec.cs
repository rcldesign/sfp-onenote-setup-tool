namespace Sfp.Core.Plan;

/// <summary>One OneNote section tab, with its pages in display order.</summary>
/// <param name="Name">Tab name, for example "Week 02 (Jan 4 to Jan 10)".</param>
/// <param name="ColorHex">Section color as #RRGGBB, or null for OneNote's default.</param>
/// <param name="Pages">Pages in order; levels inside <see cref="PageSpec"/> give the subpage structure.</param>
public sealed record SectionSpec(string Name, string? ColorHex, IReadOnlyList<PageSpec> Pages);
