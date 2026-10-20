using System.Globalization;

namespace Sfp.Core.Options;

/// <summary>
/// User-facing sentences that the Generate form (<see cref="GenerateFormValidator"/>) and the run's preflight
/// (Sfp.OneNote GenerateText) both show, defined once so the two never word the same condition differently.
/// </summary>
public static class GenerateMessages
{
    /// <summary>The one-line reason given with the OneDrive warning (PRD error table: warn only, the run may proceed).</summary>
    public const string OneDriveWarningReason =
        "OneNote desktop refuses local notebooks placed under the OneDrive sync folder. Generate elsewhere, then move the notebook to OneDrive from inside OneNote.";

    /// <summary>The OneDrive warning with the folder named, as the form shows it under the folder field and the run lists it.</summary>
    public static string OneDriveWarning(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return string.Format(CultureInfo.InvariantCulture, "The output folder {0} is under the OneDrive sync folder: {1}", folder, OneDriveWarningReason);
    }
}
