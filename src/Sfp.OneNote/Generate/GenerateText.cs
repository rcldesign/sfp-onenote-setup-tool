using System.Globalization;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>The user-facing sentences of the Generate flow from the PRD, in one place for the CLI and the app.</summary>
public static class GenerateText
{
    /// <summary>PRD error table: OneNote not signed in.</summary>
    public const string SignInMessage = "Open OneNote and sign in once, then retry.";

    /// <summary>
    /// The preflight's first call was refused because a dialog holds OneNote (busy twice, or hrAppInModalUI), which is
    /// what OneNote's first-run sign-in dialog does: the sign-in row with the dialog beside it.
    /// </summary>
    public const string DialogOrSignInMessage = "OneNote is showing a dialog or is not signed in. Close the dialog in OneNote, or open OneNote and sign in once, then retry.";

    /// <summary>
    /// The preflight's first call failed after OneNote started (activation answered) with E_FAIL, TYPE_E_LIBNOTREGISTERED
    /// or TYPE_E_CANTLOADLIBRARY, so OneNote is installed and running but refused the request. It names the two known
    /// causes, OneNote unable to load its own type library first (the type library check's warning, when there is one,
    /// names the registration and its remedy, and the log's type library line shows the registration and OneNote's
    /// bitness as the preflight read them) and different elevations second, then the remedies: restart OneNote at the
    /// same elevation, then an Online Repair of Office. The call's own message, which names the HRESULT, follows it.
    /// </summary>
    public const string FirstCallFailedMessage = "OneNote started but refused the first request. This is what OneNote answers when it cannot load its own type library (see the type library warning, when one is shown, and the type library line in the log) or when this tool and OneNote run at different elevations. Close OneNote completely (also the OneNote icon in the notification area), run both at the same elevation, then retry. If it keeps failing, run an Online Repair of Office from Settings, Apps.";

    /// <summary>PRD error table: the output folder sits under OneDrive sync (warn only). The text lives in Sfp.Core so the Generate form shows the same words.</summary>
    public const string OneDriveWarningReason = GenerateMessages.OneDriveWarningReason;

    /// <summary>The one-paragraph instruction shown after a successful run.</summary>
    public const string OneDriveMoveInstruction =
        "To use the notebook on the iPad: in OneNote on this PC open the notebook, choose File, Share, Move Notebook, pick OneDrive and press Move. "
        + "OneNote uploads it and it appears in OneNote for iPad after signing in with the same Microsoft account. "
        + "The tool cannot create the notebook inside OneDrive directly, which is why this step is done once by hand.";

    /// <summary>PRD error table: a notebook of that name already exists in the output folder.</summary>
    public static string Collision(string name) => string.Format(CultureInfo.InvariantCulture, "A notebook named {0} already exists here. Rename or choose another folder.", name);

    /// <summary>The OneDrive warning with the folder named: the one text the form and the preflight share (<see cref="GenerateMessages.OneDriveWarning"/>).</summary>
    public static string OneDriveWarning(string folder) => GenerateMessages.OneDriveWarning(folder);

    /// <summary>Start month outside the supported range.</summary>
    public static string StartMonthOutOfRange(int year, int month) =>
        string.Format(CultureInfo.InvariantCulture, "Start month must be between 1900-01 and 2199-12; got {0:0000}-{1:00}.", year, month);

    /// <summary>Output folder cannot be created or written.</summary>
    public static string FolderNotWritable(string folder, string reason) =>
        string.Format(CultureInfo.InvariantCulture, "Output folder {0} is not writable: {1}", folder, reason);

    /// <summary>
    /// PRD error table: a render error on a page, "Rendering {type} page '{title}' (PDF page {n}) failed: {reason}". The
    /// sentence lives in Sfp.Render (<see cref="PageRenderException.Describe"/>), so render-only prints the same words.
    /// </summary>
    public static string RenderFailed(PageSpec page, string reason) => PageRenderException.Describe(page, reason);
}
