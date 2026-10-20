using Sfp.Core.Manifests;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Writer;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>A locator whose answers are scripted: installed or not, and the app it opens (or the exception it throws).</summary>
internal sealed class FakeOneNoteLocator : IOneNoteLocator
{
    private readonly Func<IOneNoteApp> _open;

    public FakeOneNoteLocator(bool isInstalled, Func<IOneNoteApp> open)
    {
        IsInstalled = isInstalled;
        _open = open;
    }

    public FakeOneNoteLocator(bool isInstalled, IOneNoteApp app)
        : this(isInstalled, () => app)
    {
    }

    public bool IsInstalled { get; }

    public int OpenCalls { get; private set; }

    public IOneNoteApp Open()
    {
        OpenCalls++;
        return _open();
    }
}

/// <summary>A file system whose folders, files and writability are declared by the test.</summary>
internal sealed class FakePreflightFileSystem : IPreflightFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Folder to the reason it is not writable; folders not listed are writable.</summary>
    public Dictionary<string, string> Unwritable { get; } = new(StringComparer.Ordinal);

    public List<string> EnsuredWritable { get; } = new();

    public List<string> OneDrive { get; } = new();

    public IReadOnlyList<string> OneDriveFolders => OneDrive;

    public string DefaultOutputFolder { get; set; } = Path.Combine("/home/user/Documents", OutputFolders.DefaultFolderName);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.Contains(path);

    public string? EnsureWritable(string folder)
    {
        EnsuredWritable.Add(folder);
        return Unwritable.TryGetValue(folder, out string? reason) ? reason : null;
    }
}

/// <summary>A validator returning a fixed result.</summary>
internal sealed class FakeManifestValidator : IManifestValidator
{
    private readonly ValidationResult _result;

    public FakeManifestValidator(ValidationResult? result = null)
    {
        _result = result ?? ValidationResult.Ok;
    }

    public List<string> Paths { get; } = new();

    public ValidationResult Validate(Manifest m, string pdfPath)
    {
        Paths.Add(pdfPath);
        return _result;
    }
}

/// <summary>A preflight returning a scripted result and recording the request it saw.</summary>
internal sealed class FakePreflight : IGeneratePreflight
{
    private readonly PreflightResult _result;

    public FakePreflight(PreflightResult? result = null)
    {
        _result = result ?? new PreflightResult(Array.Empty<(string, int)>(), Array.Empty<string>());
    }

    public GenerateRequest? Request { get; private set; }

    public PreflightResult Check(GenerateRequest request)
    {
        Request = request;
        return _result;
    }
}

/// <summary>Collects progress reports synchronously, in the order they were made.</summary>
internal sealed class ProgressLog : IProgress<GenerateProgress>
{
    private readonly List<GenerateProgress> _reports = new();
    private readonly object _gate = new();

    /// <summary>Called on every report, before it is stored (used to cancel at a chosen point).</summary>
    public Action<GenerateProgress>? OnReport { get; set; }

    public IReadOnlyList<GenerateProgress> Reports
    {
        get
        {
            lock (_gate)
            {
                return _reports.ToList();
            }
        }
    }

    public IReadOnlyList<GeneratePhase> Phases => Reports.Select(r => r.Phase).Distinct().ToList();

    public void Report(GenerateProgress value)
    {
        OnReport?.Invoke(value);
        lock (_gate)
        {
            _reports.Add(value);
        }
    }
}
