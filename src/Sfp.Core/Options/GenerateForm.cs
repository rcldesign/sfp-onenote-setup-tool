namespace Sfp.Core.Options;

/// <summary>Names of the Generate form fields a validation message can attach to.</summary>
public static class GenerateFormFields
{
    public const string PdfPath = "pdfPath";
    public const string Start = "start";
    public const string NotebookName = "notebookName";
    public const string OutputFolder = "outputFolder";
    public const string Dpi = "dpi";
    public const string PageScale = "pageScale";
    public const string TimeBlocks = "timeBlocks";
}

/// <summary>How a form message affects the Generate button.</summary>
public enum FormMessageKind
{
    /// <summary>Blocks Generate until fixed.</summary>
    Error,

    /// <summary>Shown inline; Generate stays enabled.</summary>
    Warning,

    /// <summary>A check is still running; Generate waits for it.</summary>
    Pending,
}

/// <summary>One inline message under a form field.</summary>
public sealed record FormMessage(string Field, string Text, FormMessageKind Kind);

/// <summary>
/// The Generate form as the validator sees it. Year and page scale are nullable because they come from text boxes
/// whose content may not parse; the view model passes null in that case and the validator words the message.
/// </summary>
/// <param name="PdfPath">Template PDF path as typed or picked.</param>
/// <param name="Year">Parsed year, or null when the text is not a number.</param>
/// <param name="Month">Month 1 to 12.</param>
/// <param name="NotebookName">Notebook name as typed.</param>
/// <param name="OutputFolder">Output folder as typed or picked.</param>
/// <param name="Dpi">Render DPI.</param>
/// <param name="PageScale">Parsed page scale, or null when the text is not a number.</param>
/// <param name="TimeBlocks">Time-block window from the two hour pickers.</param>
public sealed record GenerateFormInput(
    string? PdfPath,
    int? Year,
    int Month,
    string? NotebookName,
    string? OutputFolder,
    int Dpi,
    double? PageScale,
    TimeBlockWindow TimeBlocks);

/// <summary>Every message of one validation pass, with lookups per field.</summary>
public sealed record GenerateFormValidation(IReadOnlyList<FormMessage> Messages)
{
    public static GenerateFormValidation Empty { get; } = new(Array.Empty<FormMessage>());

    /// <summary>True when no message blocks Generate (no error and no pending check).</summary>
    public bool CanGenerate => Messages.All(m => m.Kind == FormMessageKind.Warning);

    /// <summary>The blocking message (error or pending) for a field, or null.</summary>
    public string? BlockingFor(string field) =>
        Messages.FirstOrDefault(m => m.Field == field && m.Kind != FormMessageKind.Warning)?.Text;

    /// <summary>The error for a field, or null.</summary>
    public string? ErrorFor(string field) =>
        Messages.FirstOrDefault(m => m.Field == field && m.Kind == FormMessageKind.Error)?.Text;

    /// <summary>The pending-check note for a field, or null.</summary>
    public string? PendingFor(string field) =>
        Messages.FirstOrDefault(m => m.Field == field && m.Kind == FormMessageKind.Pending)?.Text;

    /// <summary>The warning for a field, or null.</summary>
    public string? WarningFor(string field) =>
        Messages.FirstOrDefault(m => m.Field == field && m.Kind == FormMessageKind.Warning)?.Text;
}
