using System.Xml.Linq;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.OneNote.Com;
using Sfp.OneNote.Writer;

namespace Sfp.OneNote.Generate;

/// <summary>The file system questions the preflight asks, behind an interface so every row is unit-testable.</summary>
public interface IPreflightFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>Creates the folder when it is missing and probes a write inside it; null on success, else the reason.</summary>
    string? EnsureWritable(string folder);

    /// <summary>The OneDrive sync folders on this machine (environment variables OneDrive, OneDriveConsumer, OneDriveCommercial).</summary>
    IReadOnlyList<string> OneDriveFolders { get; }

    /// <summary>The default output folder (Documents\OneNote Notebooks).</summary>
    string DefaultOutputFolder { get; }
}

/// <summary>Production <see cref="IPreflightFileSystem"/>.</summary>
public sealed class PreflightFileSystem : IPreflightFileSystem
{
    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public IReadOnlyList<string> OneDriveFolders =>
        OneDriveVariables
            .Select(Environment.GetEnvironmentVariable)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string DefaultOutputFolder => OutputFolders.Default();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public string? EnsureWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".sfp2onenote-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}

/// <summary>
/// The PRD's pre-run checks in the order the Generate flow shows them: OneNote installed (ProgID resolves), its type
/// library registration usable (read before OneNote is started) and OneNote reachable (GetHierarchy answers), the PDF matching
/// the manifest, the start month in range, the notebook name rule, the output folder writable, the OneDrive warning,
/// the name collision rule, and the fallback-font warning. An empty notebook list is recorded
/// (<see cref="PreflightResult.NoOpenNotebooks"/>), not refused: the PRD's not-signed-in row needs it and a failing
/// first OpenHierarchy, which the orchestrator checks. Render-only skips the OneNote and notebook checks and only needs
/// its output folder.
/// </summary>
public sealed class GeneratePreflight : IGeneratePreflight
{
    private const int ValidationError = 2;
    private const int OneNoteUnavailable = 3;

    private readonly IManifestValidator _validator;
    private readonly IOneNoteLocator _oneNote;
    private readonly IPreflightFileSystem _fs;
    private readonly ISidecarStore _sidecars;
    private readonly IDateEngine _dates;
    private readonly IReadOnlyList<string> _fontWarnings;

    /// <param name="validator">PDF validation.</param>
    /// <param name="oneNote">OneNote presence and the run's session.</param>
    /// <param name="fs">File system checks.</param>
    /// <param name="sidecars">For the sidecar path of the collision rule.</param>
    /// <param name="dates">For the default notebook name.</param>
    /// <param name="fontWarnings">The font service's warnings (empty when the template typeface is installed).</param>
    public GeneratePreflight(
        IManifestValidator validator,
        IOneNoteLocator oneNote,
        IPreflightFileSystem fs,
        ISidecarStore sidecars,
        IDateEngine dates,
        IReadOnlyList<string>? fontWarnings = null)
    {
        _validator = validator;
        _oneNote = oneNote;
        _fs = fs;
        _sidecars = sidecars;
        _dates = dates;
        _fontWarnings = fontWarnings ?? Array.Empty<string>();
    }

    public PreflightResult Check(GenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<(string Message, int ExitCode)> errors = new();
        List<string> warnings = new();
        GenerateOptions o = request.Options;

        bool noOpenNotebooks = !request.RenderOnly && CheckOneNote(errors, warnings);

        ValidationResult pdf = _validator.Validate(request.Manifest, o.PdfPath);
        errors.AddRange(pdf.Errors.Select(e => (e, ValidationError)));
        warnings.AddRange(pdf.Warnings);

        bool startOk = o.Start.Year is >= QuarterSpec.MinYear and <= QuarterSpec.MaxYear && o.Start.Month is >= 1 and <= 12;
        if (!startOk)
        {
            errors.Add((GenerateText.StartMonthOutOfRange(o.Start.Year, o.Start.Month), ValidationError));
        }

        if (request.RenderOnly)
        {
            CheckRenderFolder(request.RenderOutputDir, errors);
        }
        else
        {
            CheckNotebook(request, startOk, errors, warnings);
        }

        warnings.AddRange(_fontWarnings);
        return new PreflightResult(errors, warnings) { NoOpenNotebooks = noOpenNotebooks };
    }

    /// <summary>True when the hierarchy XML lists at least one notebook (the first half of the sign-in rule).</summary>
    public static bool HasOpenNotebook(string hierarchyXml)
    {
        if (string.IsNullOrWhiteSpace(hierarchyXml))
        {
            return false;
        }

        try
        {
            XElement root = XElement.Parse(hierarchyXml);
            return root.Name.LocalName == "Notebook" || root.Elements().Any(e => e.Name.LocalName == "Notebook");
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// OneNote installed, its type library registration usable, and OneNote answering GetHierarchy; a failure is an error
    /// with exit 3. The type library check (<see cref="IOneNoteLocator.TypeLibrary"/>) runs after the install check and
    /// before OneNote is started: its error (registered entries whose files are all missing) is the only error and OneNote
    /// is not opened; its warning (nothing registered in the registry this process sees, or a stale entry beside a sound
    /// one) joins <paramref name="warnings"/> and the run goes on. A refusal because a dialog holds OneNote (busy twice, or
    /// hrAppInModalUI; OneNote's first-run sign-in dialog does this, and this is the run's first call) says to close the
    /// dialog or sign in. E_FAIL, TYPE_E_LIBNOTREGISTERED or TYPE_E_CANTLOADLIBRARY from that first call, after OneNote
    /// started, gives the type library and elevation remedy (<see cref="GenerateText.FirstCallFailedMessage"/>) ahead of
    /// the call's message, which names the HRESULT. A OneNote.Application object without the automation members
    /// (DISP_E_UNKNOWNNAME, DISP_E_MEMBERNOTFOUND, or the runtime binder finding no such member) arrives as
    /// <see cref="OneNoteUnavailableException"/> opening with <see cref="OneNoteUnavailableException.NoAutomationMessage"/>
    /// and is shown as it is. Returns true when OneNote answered with no open notebook, which is recorded for the
    /// orchestrator rather than refused (see <see cref="PreflightResult.NoOpenNotebooks"/>).
    /// </summary>
    private bool CheckOneNote(List<(string Message, int ExitCode)> errors, List<string> warnings)
    {
        if (!_oneNote.IsInstalled)
        {
            errors.Add((OneNoteUnavailableException.UserMessage, OneNoteUnavailable));
            return false;
        }

        TypeLibraryCheck typeLibrary = _oneNote.TypeLibrary;
        if (typeLibrary.Error is not null)
        {
            errors.Add((typeLibrary.Error, OneNoteUnavailable));
            return false;
        }

        if (typeLibrary.Warning is not null)
        {
            warnings.Add(typeLibrary.Warning);
        }

        try
        {
            string xml = _oneNote.Open().GetHierarchy(null, HierarchyScope.Notebooks);
            return !HasOpenNotebook(xml);
        }
        catch (OneNoteUnavailableException ex)
        {
            errors.Add((ex.Message, OneNoteUnavailable));
        }
        catch (OneNoteException ex) when (ex is OneNoteBusyException || ex.HResult == OneNoteHResults.AppInModalUI)
        {
            errors.Add((GenerateText.DialogOrSignInMessage + " " + ex.Message, OneNoteUnavailable));
        }
        catch (OneNoteException ex) when (ex.HResult == OneNoteHResults.Fail || OneNoteHResults.IsTypeLibraryFailure(ex.HResult))
        {
            errors.Add((GenerateText.FirstCallFailedMessage + " " + ex.Message, OneNoteUnavailable));
        }
        catch (OneNoteException ex)
        {
            errors.Add(("OneNote could not be reached: " + ex.Message, OneNoteUnavailable));
        }

        return false;
    }

    private void CheckRenderFolder(string? dir, List<(string Message, int ExitCode)> errors)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            errors.Add(("Render-only needs an output folder for the PNG files.", ValidationError));
            return;
        }

        string? problem = _fs.EnsureWritable(dir);
        if (problem is not null)
        {
            errors.Add((GenerateText.FolderNotWritable(dir, problem), ValidationError));
        }
    }

    private void CheckNotebook(GenerateRequest request, bool startOk, List<(string Message, int ExitCode)> errors, List<string> warnings)
    {
        GenerateOptions o = request.Options;
        string? name = startOk ? NotebookNames.Resolve(request.Manifest, _dates.Build(o.Start), o) : o.NotebookName;
        string? nameProblem = NotebookNames.Problem(name);
        if (nameProblem is not null)
        {
            errors.Add((nameProblem, ValidationError));
        }

        string folder = string.IsNullOrWhiteSpace(o.OutputFolder) ? _fs.DefaultOutputFolder : o.OutputFolder;
        string? folderProblem = _fs.EnsureWritable(folder);
        if (folderProblem is not null)
        {
            errors.Add((GenerateText.FolderNotWritable(folder, folderProblem), ValidationError));
        }

        if (_fs.OneDriveFolders.Any(root => OutputFolders.IsUnder(folder, root)))
        {
            warnings.Add(GenerateText.OneDriveWarning(folder));
        }

        if (nameProblem is null && name is not null)
        {
            string notebookPath = Path.Combine(folder, name);
            if (_fs.DirectoryExists(notebookPath) || _fs.FileExists(_sidecars.PathFor(notebookPath)))
            {
                errors.Add((GenerateText.Collision(name), ValidationError));
            }
        }
    }
}
