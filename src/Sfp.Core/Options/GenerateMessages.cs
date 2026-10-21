using System.Globalization;

namespace Sfp.Core.Options;

/// <summary>
/// User-facing sentences of the Generate flow that more than one front end shows (the Generate form, the run's preflight
/// in Sfp.OneNote GenerateText, the app's and the CLI's cancelled summaries), defined once so they never word the same
/// condition differently.
/// </summary>
public static class GenerateMessages
{
    /// <summary>The one-line reason given with the OneDrive warning (PRD error table: warn only, the run may proceed).</summary>
    public const string OneDriveWarningReason =
        "OneNote desktop refuses local notebooks placed under the OneDrive sync folder. Generate elsewhere, then move the notebook to OneDrive from inside OneNote.";

    private const string PartialNotebookPrefix = "The partial notebook at ";
    private const string NotDeletedMarker = " could not be deleted: ";

    /// <summary>The OneDrive warning with the folder named, as the form shows it under the folder field and the run lists it.</summary>
    public static string OneDriveWarning(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return string.Format(CultureInfo.InvariantCulture, "The output folder {0} is under the OneDrive sync folder: {1}", folder, OneDriveWarningReason);
    }

    /// <summary>
    /// The warning a cancelled run carries when its partial notebook could not be deleted: "The partial notebook at
    /// {path} could not be deleted: {reason}", the wording GenerateOrchestrator uses today. The orchestrator should build
    /// it here so <see cref="IsPartialNotebookNotDeleted"/> can never drift from it.
    /// </summary>
    public static string PartialNotebookNotDeleted(string notebookPath, string reason)
    {
        ArgumentNullException.ThrowIfNull(notebookPath);
        ArgumentNullException.ThrowIfNull(reason);
        return PartialNotebookPrefix + notebookPath + NotDeletedMarker + reason;
    }

    /// <summary>
    /// True for the warning of <see cref="PartialNotebookNotDeleted"/>. The cancelled summaries (app and CLI) then say the
    /// partial notebook is still on disk instead of claiming it was deleted.
    /// </summary>
    public static bool IsPartialNotebookNotDeleted(string warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return warning.StartsWith(PartialNotebookPrefix, StringComparison.Ordinal)
            && warning.Contains(NotDeletedMarker, StringComparison.Ordinal);
    }
}
