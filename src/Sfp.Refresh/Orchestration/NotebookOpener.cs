using Serilog;
using Sfp.OneNote;
using Sfp.OneNote.Com;

namespace Sfp.Refresh.Orchestration;

/// <summary>A notebook found for a Refresh run: its handle, the sidecar read for it (null when missing) and where the sidecar lives.</summary>
public sealed record OpenedNotebook(NotebookHandle Notebook, Sidecar? Sidecar, string SidecarPath);

/// <summary>
/// Finds and opens the notebook a Refresh run names. The sidecar is looked for first (in the given folder, else in the
/// search folders); then OneNote's open notebooks are matched by name; when the notebook is not open, OpenHierarchy is
/// called on the folder path from the sidecar or the given folder. When the folders held no sidecar and the notebook
/// OneNote resolved has a local folder path, the sidecar beside that folder is loaded, so a notebook generated outside
/// the search folders keeps its sidecar instead of having it rebuilt and overwritten every run. The scheduled task may
/// run while OneNote is still starting, so after OpenHierarchy the opener waits up to <see cref="DefaultOpenTimeout"/>
/// for the notebook to appear in the hierarchy (the PRD's "the run waits up to 60 s for the notebook to open").
/// </summary>
public sealed class NotebookOpener
{
    /// <summary>How long to wait for a notebook OpenHierarchy returned an ID for to be listed by GetHierarchy.</summary>
    public static readonly TimeSpan DefaultOpenTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IOneNoteApp app;
    private readonly ISidecarStore sidecars;
    private readonly ILogger log;
    private readonly TimeSpan openTimeout;
    private readonly Action<TimeSpan> sleep;

    /// <param name="app">The run's COM session.</param>
    /// <param name="sidecars">Sidecar file store.</param>
    /// <param name="log">Run log.</param>
    /// <param name="openTimeout">Wait for a notebook to open; default 60 s.</param>
    /// <param name="sleep">How to wait between polls; tests pass a no-op.</param>
    public NotebookOpener(IOneNoteApp app, ISidecarStore sidecars, ILogger log, TimeSpan? openTimeout = null, Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentNullException.ThrowIfNull(log);
        this.app = app;
        this.sidecars = sidecars;
        this.log = log;
        this.openTimeout = openTimeout ?? DefaultOpenTimeout;
        this.sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>
    /// Opens the notebook named <paramref name="name"/>. Returns null when no sidecar names it, OneNote does not list
    /// it and no folder path can be tried (or opening the path fails). OneNote being unavailable propagates as
    /// <see cref="OneNoteUnavailableException"/>.
    /// </summary>
    /// <param name="name">The notebook name ("SFP Q1 2027").</param>
    /// <param name="folder">The notebooks folder given on the command line, or null.</param>
    /// <param name="searchFolders">Folders to look for a sidecar in when <paramref name="folder"/> is null.</param>
    public OpenedNotebook? Open(string name, string? folder, IReadOnlyList<string> searchFolders) =>
        Open(name, folder, searchFolders, FindSidecar(name, folder, searchFolders));

    /// <summary>
    /// <see cref="Open(string, string?, IReadOnlyList{string})"/> with the folders already searched for the sidecar
    /// (<see cref="FindSidecar"/>), so a caller that needs the sidecar's path before any COM call reads it once.
    /// </summary>
    /// <param name="name">The notebook name ("SFP Q1 2027").</param>
    /// <param name="folder">The notebooks folder given on the command line, or null.</param>
    /// <param name="searchFolders">Folders to look for a sidecar in when <paramref name="folder"/> is null.</param>
    /// <param name="found">What <see cref="FindSidecar"/> returned for the same name and folders.</param>
    public OpenedNotebook? Open(string name, string? folder, IReadOnlyList<string> searchFolders, (Sidecar? Sidecar, string? Path) found)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(searchFolders);
        (Sidecar? sidecar, string? sidecarPath) = found;
        if (sidecar is not null)
        {
            log.Information("Sidecar for {Name} found at {Path}", name, sidecarPath);
        }

        HierarchyNotebook? open = HierarchyXmlReader.Notebooks(app.GetHierarchy(null, HierarchyScope.Notebooks))
            .FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        if (open is null)
        {
            string? path = sidecar?.NotebookPath;
            if (string.IsNullOrWhiteSpace(path) && folder is not null)
            {
                path = Path.Combine(folder, name);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                log.Warning("Notebook {Name} is not open in OneNote and no sidecar or folder names its path", name);
                return null;
            }

            open = TryOpen(path, name);
            if (open is null)
            {
                return null;
            }
        }

        NotebookHandle handle = new(open.Id, string.IsNullOrWhiteSpace(open.Path) ? sidecar?.NotebookPath ?? "" : open.Path, open.Name);
        if (sidecar is null && IsLocalPath(handle.Path))
        {
            string beside = sidecars.PathFor(handle.Path);
            Sidecar? besideSidecar = TryLoad(beside);
            if (besideSidecar is not null)
            {
                log.Information("Sidecar for {Name} found beside the notebook at {Path}", name, beside);
                sidecar = besideSidecar;
                sidecarPath = beside;
            }
        }

        string file = sidecarPath ?? sidecars.PathFor(SidecarFolderFor(handle, name, folder, searchFolders));
        return new OpenedNotebook(handle, sidecar, file);
    }

    /// <summary>True for a folder path on disk, false for a URL (a notebook moved to OneDrive reports one) or nothing.</summary>
    private static bool IsLocalPath(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains("://", StringComparison.Ordinal);

    /// <summary>The sidecar for a notebook name in the given folder, else the first match in the search folders.</summary>
    public (Sidecar? Sidecar, string? Path) FindSidecar(string name, string? folder, IReadOnlyList<string> searchFolders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(searchFolders);
        IEnumerable<string> folders = folder is not null ? new[] { folder } : searchFolders;
        foreach (string candidate in folders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            string path = sidecars.PathFor(Path.Combine(candidate, name));
            Sidecar? sidecar = TryLoad(path);
            if (sidecar is not null)
            {
                return (sidecar, path);
            }
        }

        return (null, null);
    }

    private static string SidecarFolderFor(NotebookHandle handle, string name, string? folder, IReadOnlyList<string> searchFolders)
    {
        if (folder is not null)
        {
            return Path.Combine(folder, name);
        }

        if (IsLocalPath(handle.Path))
        {
            return handle.Path;
        }

        string first = searchFolders.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(first, name);
    }

    private Sidecar? TryLoad(string path)
    {
        try
        {
            return sidecars.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log.Warning(ex, "Sidecar {Path} could not be read and is treated as missing", path);
            return null;
        }
    }

    private HierarchyNotebook? TryOpen(string path, string name)
    {
        string id;
        try
        {
            id = app.OpenHierarchy(path, null, CreateFileType.None);
        }
        catch (OneNoteUnavailableException)
        {
            throw;
        }
        catch (OneNoteException ex)
        {
            log.Warning(ex, "Notebook {Name} could not be opened from {Path}", name, path);
            return null;
        }

        log.Information("Notebook {Name} opened from {Path} as {Id}", name, path, id);
        return WaitForNotebook(id, name);
    }

    /// <summary>Polls GetHierarchy on the new ID until OneNote lists the notebook, up to the open timeout.</summary>
    private HierarchyNotebook WaitForNotebook(string id, string name)
    {
        TimeSpan waited = TimeSpan.Zero;
        while (true)
        {
            try
            {
                return HierarchyXmlReader.Notebook(app.GetHierarchy(id, HierarchyScope.Self));
            }
            catch (OneNoteUnavailableException)
            {
                throw;
            }
            catch (Exception ex) when (ex is OneNoteException or FormatException or System.Xml.XmlException)
            {
                if (waited >= openTimeout)
                {
                    throw new OneNoteException($"Notebook '{name}' did not open within {openTimeout.TotalSeconds:0} s.", ex);
                }

                log.Information("Waiting for notebook {Name} to open ({Waited:0} s)", name, waited.TotalSeconds);
                sleep(PollInterval);
                waited += PollInterval;
            }
        }
    }
}
