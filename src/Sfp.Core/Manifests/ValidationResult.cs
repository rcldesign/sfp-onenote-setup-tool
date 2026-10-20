namespace Sfp.Core.Manifests;

/// <summary>Outcome of checking a PDF against the manifest.</summary>
/// <param name="Errors">Mismatches that block Generate, worded for the user ("Expected 132 pages, found 130").</param>
/// <param name="Warnings">Notes that do not block.</param>
public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Ok { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    public static ValidationResult Fail(params string[] errors) => new(errors, Array.Empty<string>());
}

/// <summary>Checks that a PDF matches the manifest: page count, page size within 1 pt, and a text probe per page type.</summary>
public interface IManifestValidator
{
    ValidationResult Validate(Manifest m, string pdfPath);
}
