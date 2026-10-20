using System.Globalization;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;

namespace Sfp.Core.Options;

/// <summary>
/// Every validation rule of the PRD's main screen table, as one testable class. <see cref="CheckPdf"/> is the slow
/// rule (it opens the PDF through <see cref="IManifestValidator"/>) and runs in the background; <see cref="Validate"/>
/// is the fast pass over every other field and takes the last PDF result as an input.
/// </summary>
public sealed class GenerateFormValidator
{
    public const int MaxNameLength = 64;

    /// <summary>Inclusive bounds of the Advanced page scale.</summary>
    public const double MinPageScale = 0.25;

    public const double MaxPageScale = 2.0;

    /// <summary>The characters a notebook name may not hold (a OneNote folder name plus OneNote's own # and %).</summary>
    public static IReadOnlyList<char> ForbiddenNameChars { get; } = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '#', '%' };

    private readonly IManifestValidator _pdfValidator;
    private readonly IPathProbe _paths;
    private readonly IReadOnlyList<string> _oneDriveRoots;

    /// <param name="pdfValidator">Checks a PDF against the manifest.</param>
    /// <param name="paths">File-system facts.</param>
    /// <param name="oneDriveRoots">The OneDrive sync folders (from the OneDrive environment variables), or empty.</param>
    public GenerateFormValidator(IManifestValidator pdfValidator, IPathProbe paths, IReadOnlyList<string> oneDriveRoots)
    {
        _pdfValidator = pdfValidator;
        _paths = paths;
        _oneDriveRoots = oneDriveRoots;
    }

    /// <summary>The PDF row: the file must exist, then page count, page sizes and probes must match the manifest.</summary>
    public ValidationResult CheckPdf(Manifest manifest, string? pdfPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return ValidationResult.Fail("Choose the SFP template PDF.");
        }

        if (!_paths.FileExists(pdfPath))
        {
            return ValidationResult.Fail($"PDF not found: {pdfPath}");
        }

        return _pdfValidator.Validate(manifest, pdfPath);
    }

    /// <summary>
    /// The fast rules. <paramref name="pdfResult"/> is the last <see cref="CheckPdf"/> outcome for the input's path,
    /// or null while that check is still running (the PDF row is then pending and Generate stays disabled).
    /// </summary>
    public GenerateFormValidation Validate(GenerateFormInput input, ValidationResult? pdfResult)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<FormMessage> messages = new();
        CheckPdfRow(input.PdfPath, pdfResult, messages);
        CheckStart(input.Year, input.Month, messages);
        bool nameOk = CheckName(input.NotebookName, messages);
        bool folderOk = CheckOutputFolder(input.OutputFolder, messages);
        if (nameOk && folderOk)
        {
            CheckNameCollision(input.NotebookName!, input.OutputFolder!, messages);
        }

        if (!GenerateOptions.AllowedDpis.Contains(input.Dpi))
        {
            messages.Add(Error(GenerateFormFields.Dpi, "Render DPI must be 144, 220 or 300."));
        }

        if (input.PageScale is not double scale || double.IsNaN(scale) || scale < MinPageScale || scale > MaxPageScale)
        {
            messages.Add(Error(GenerateFormFields.PageScale, string.Format(
                CultureInfo.InvariantCulture,
                "Page scale must be a number between {0} and {1}.",
                MinPageScale,
                MaxPageScale)));
        }

        if (!input.TimeBlocks.IsValid)
        {
            messages.Add(Error(GenerateFormFields.TimeBlocks, "The time-block window must start between 0 and 23 and end after it starts, at 24 at the latest."));
        }

        return new GenerateFormValidation(messages);
    }

    /// <summary>The forbidden characters a name holds, in the order they appear, each once.</summary>
    public static IReadOnlyList<char> ForbiddenCharsIn(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Where(ForbiddenNameChars.Contains).Distinct().ToList();
    }

    /// <summary>
    /// True when the folder is one of the OneDrive roots or lies under one, or a path segment is named like a OneDrive
    /// sync folder ("OneDrive", "OneDrive - Contoso"). Both slash kinds are separators so the rule is the same on every OS.
    /// </summary>
    public bool IsUnderOneDrive(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string full = NormalizePath(folder);
        foreach (string root in _oneDriveRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string rootFull = NormalizePath(root);
            if (rootFull.Length == 0)
            {
                continue;
            }

            if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(rootFull + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return full.Split('/')
            .Any(segment => segment.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Forward slashes only, no trailing slash, whitespace trimmed.</summary>
    private static string NormalizePath(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');

    private void CheckPdfRow(string? pdfPath, ValidationResult? pdfResult, List<FormMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            messages.Add(Error(GenerateFormFields.PdfPath, "Choose the SFP template PDF."));
            return;
        }

        if (!_paths.FileExists(pdfPath))
        {
            messages.Add(Error(GenerateFormFields.PdfPath, $"PDF not found: {pdfPath}"));
            return;
        }

        if (pdfResult is null)
        {
            messages.Add(new FormMessage(GenerateFormFields.PdfPath, "Checking the PDF against the template manifest...", FormMessageKind.Pending));
            return;
        }

        if (!pdfResult.IsValid)
        {
            messages.Add(Error(GenerateFormFields.PdfPath, string.Join("\n", pdfResult.Errors)));
        }
        else if (pdfResult.Warnings.Count > 0)
        {
            messages.Add(Warning(GenerateFormFields.PdfPath, string.Join("\n", pdfResult.Warnings)));
        }
    }

    private static void CheckStart(int? year, int month, List<FormMessage> messages)
    {
        if (year is not int y || y < QuarterSpec.MinYear || y > QuarterSpec.MaxYear)
        {
            messages.Add(Error(GenerateFormFields.Start, $"Year must be a whole number between {QuarterSpec.MinYear} and {QuarterSpec.MaxYear}."));
        }

        if (month is < 1 or > 12)
        {
            messages.Add(Error(GenerateFormFields.Start, "Choose the first month of the quarter."));
        }
    }

    private static bool CheckName(string? name, List<FormMessage> messages)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            messages.Add(Error(GenerateFormFields.NotebookName, "Enter a notebook name."));
            return false;
        }

        if (trimmed.Length > MaxNameLength)
        {
            messages.Add(Error(GenerateFormFields.NotebookName, $"Notebook name must be 1 to {MaxNameLength} characters; this one has {trimmed.Length}."));
            return false;
        }

        IReadOnlyList<char> bad = ForbiddenCharsIn(trimmed);
        if (bad.Count > 0)
        {
            string found = string.Join(" ", bad);
            string all = string.Join(" ", ForbiddenNameChars);
            messages.Add(Error(GenerateFormFields.NotebookName, $"Notebook name cannot contain {found} (none of {all} are allowed)."));
            return false;
        }

        return true;
    }

    private bool CheckOutputFolder(string? folder, List<FormMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            messages.Add(Error(GenerateFormFields.OutputFolder, "Choose an output folder."));
            return false;
        }

        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            messages.Add(Error(GenerateFormFields.OutputFolder, "Output folder is not a valid path."));
            return false;
        }

        if (!_paths.CanWrite(folder))
        {
            messages.Add(Error(GenerateFormFields.OutputFolder, $"Output folder is not writable: {folder}"));
            return false;
        }

        if (IsUnderOneDrive(folder))
        {
            messages.Add(Warning(
                GenerateFormFields.OutputFolder,
                "This folder is inside a OneDrive sync folder; OneNote desktop refuses local notebooks there. Generate elsewhere, then move the notebook to OneDrive from inside OneNote."));
        }

        return true;
    }

    private void CheckNameCollision(string name, string folder, List<FormMessage> messages)
    {
        string trimmed = name.Trim();
        string candidate;
        try
        {
            candidate = Path.Combine(folder, trimmed);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (_paths.DirectoryExists(candidate) || _paths.FileExists(candidate))
        {
            messages.Add(Error(GenerateFormFields.NotebookName, $"A notebook named {trimmed} already exists here. Rename or choose another folder."));
        }
    }

    private static FormMessage Error(string field, string text) => new(field, text, FormMessageKind.Error);

    private static FormMessage Warning(string field, string text) => new(field, text, FormMessageKind.Warning);
}
