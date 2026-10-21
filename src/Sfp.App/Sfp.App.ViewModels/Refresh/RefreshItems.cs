using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Refresh;

namespace Sfp.App.ViewModels.Refresh;

/// <summary>A row of the notebook list.</summary>
public sealed record KnownNotebookItem(KnownNotebook Notebook)
{
    public string Name => Notebook.Name;

    public string Path => Notebook.Path;

    /// <summary>"Q1 2027" or "Feb to Apr 2027"; "unknown" for a sidecar holding "quarter": null.</summary>
    public string QuarterLabel => Notebook.Quarter is null ? "unknown" : PlannerText.QuarterLabel(Notebook.Quarter);

    public string GeneratedText => Stamp(Notebook.Generated) ?? "unknown";

    public string LastRefreshText => Stamp(Notebook.LastRefresh) ?? "never";

    /// <summary>"yyyy-MM-dd HH:mm" in the stamp's own offset, or null.</summary>
    public static string? Stamp(DateTimeOffset? stamp) => stamp?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public override string ToString() => Name;
}

/// <summary>A row of the dry-run listing: page, slot, old, new, blocked with reason, inked mark.</summary>
public sealed record PlannedChangeItem(PlannedChange Change, bool IsInked)
{
    public string Page => Change.Page.Value;

    public string Slot => Change.SlotId;

    public string OldText => Change.OldText ?? "";

    public string NewText => Change.NewText;

    public string KindText => Change.Kind.ToString();

    public bool Blocked => Change.Blocked;

    public string? Reason => Change.Reason;

    /// <summary>"Blocked: a user outline sits in the slot", "Inked", "Blocked: ...; inked" or "".</summary>
    public string StatusText
    {
        get
        {
            List<string> parts = new();
            if (Change.Blocked)
            {
                parts.Add("Blocked" + (string.IsNullOrWhiteSpace(Change.Reason) ? "" : ": " + Change.Reason));
            }

            if (IsInked)
            {
                parts.Add("Inked");
            }

            return string.Join("; ", parts);
        }
    }
}

/// <summary>The summary after a run: values read from Setup, pages touched, events written, skipped, and notes.</summary>
public sealed record RefreshRunSummary(
    string Headline,
    int PagesTouched,
    int ValuesWritten,
    int EventsWritten,
    int Skipped,
    IReadOnlyList<string> FailedSources,
    IReadOnlyList<string> Warnings,
    bool SidecarRebuilt,
    string ElapsedText)
{
    public string FailedSourcesText => FailedSources.Count == 0 ? "" : "Sources that failed: " + string.Join(", ", FailedSources);

    public string WarningsText => string.Join("\n", Warnings);

    public bool HasWarnings => Warnings.Count > 0 || FailedSources.Count > 0 || SidecarRebuilt;

    public string SidecarText => SidecarRebuilt ? "The sidecar file was missing or stale and was rebuilt from the notebook's page keys." : "";
}
