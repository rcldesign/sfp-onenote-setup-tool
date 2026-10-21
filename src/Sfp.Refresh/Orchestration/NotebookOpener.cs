using Serilog;
using Sfp.OneNote;
using Sfp.OneNote.Com;

namespace Sfp.Refresh.Orchestration;

/// <summary>A notebook found for a Refresh run: its handle, the sidecar read for it (null when missing) and where the sidecar lives.</summary>
public sealed record OpenedNotebook(NotebookHandle Notebook, Sidecar? Sidecar, string SidecarPath);

/// <summary>
/// Finds and opens the notebook a Refresh run names. The sidecar is looked for first (in the given folder, else in the
/// search folders); then OneNote's open notebooks of that name are matched to it by notebook ID, then by path, and by
/// name alone only when one is open, its own path is a URL, and the local folder the sidecar records holds no notebook
/// file (it is gone, or was left empty: the notebook moved to OneDrive). A sidecar that records a URL is only ever
/// matched by ID or path, else its URL is opened. A sidecar whose folder still holds notebook files while the one open
/// notebook of that name is at a URL stops the run rather than open a copy left behind, and a sidecar whose folder is
/// gone while other notebooks of that name are open stops it too (<see cref="Problem"/>); otherwise the recorded folder
/// is opened. Known limit: when the real notebook is closed and its old folder is gone or empty, another notebook of
/// that name on OneDrive (a trial) is taken for it, as no ID or path tells them apart. When the notebook is not open,
/// OpenHierarchy is called on the folder path from the sidecar or the given folder. When the folders held no sidecar and the notebook
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

    /// <summary>What <see cref="HoldsNotebookFiles"/> looks through: every file under the folder, hidden and system files too.</summary>
    private static readonly EnumerationOptions EveryFileInside = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };

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
        Problem = null;
        (Sidecar? sidecar, string? sidecarPath) = found;
        if (sidecar is not null)
        {
            log.Information("Sidecar for {Name} found at {Path}", name, sidecarPath);
        }

        List<HierarchyNotebook> named = HierarchyXmlReader.Notebooks(app.GetHierarchy(null, HierarchyScope.Notebooks))
            .Where(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        HierarchyNotebook? open = Choose(named, sidecar, folder, name);
        bool openedFromSidecarPath = false;
        if (open is null)
        {
            if (sidecar is not null && named.Count > 0 && IsGoneLocalFolder(sidecar.NotebookPath))
            {
                // The recorded folder is gone and the open notebooks of that name are not the one notebook moved to OneDrive.
                Problem = $"The sidecar at {sidecarPath} describes the notebook at {sidecar.NotebookPath}, which is no longer there, and {Paths(named)} {(named.Count == 1 ? "is another notebook" : "are other notebooks")} named '{name}'. The sidecar was left as it is. Open the notebook it describes in OneNote and close {(named.Count == 1 ? "the other" : "the others")}, or delete the sidecar if the open notebook is that notebook moved, and run Refresh again.";
                log.Warning("{Problem}", Problem);
                return null;
            }

            if (sidecar is not null && named.Count == 1 && IsUrl(named[0].Path) && IsLocalPath(sidecar.NotebookPath) && HoldsNotebookFiles(sidecar.NotebookPath))
            {
                // The one notebook of that name may be the sidecar's moved to OneDrive, but the recorded folder still holds
                // notebook files: opening that folder could open a copy left behind, and taking the notebook at the URL
                // could rebuild the sidecar from another notebook.
                Problem = $"The sidecar at {sidecarPath} describes the notebook at {sidecar.NotebookPath}, which still holds notebook files, and the notebook '{name}' open at {named[0].Path} may be that notebook moved to OneDrive. Refresh used neither and left the sidecar as it is. If the open notebook is the one that moved, move or rename the old folder; otherwise open the notebook the sidecar describes and close the other. Then run Refresh again.";
                log.Warning("{Problem}", Problem);
                return null;
            }

            string? path = sidecar?.NotebookPath;
            if (string.IsNullOrWhiteSpace(path) && folder is not null)
            {
                path = Path.Combine(folder, name);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                if (named.Count > 1)
                {
                    Problem = $"Several notebooks named '{name}' are open in OneNote ({Paths(named)}); pass --folder with the folder of the one to refresh.";
                    log.Warning("{Problem}", Problem);
                }
                else
                {
                    log.Warning("Notebook {Name} is not open in OneNote and no sidecar or folder names its path", name);
                }

                return null;
            }

            open = TryOpen(path, name);
            if (open is null)
            {
                if (named.Count > 0)
                {
                    Problem = $"The notebook '{name}' at {path} could not be opened, and {Paths(named)} {(named.Count == 1 ? "is another notebook" : "are other notebooks")} of that name. Open the right notebook in OneNote, or close the other, and run Refresh again.";
                    log.Warning("{Problem}", Problem);
                }
                else if (sidecar is not null && IsUrl(sidecar.NotebookPath))
                {
                    Problem = $"The notebook '{name}' at {path}, which its sidecar at {sidecarPath} records, could not be opened. Open it in OneNote and run Refresh again.";
                    log.Warning("{Problem}", Problem);
                }

                return null;
            }

            // OneNote opened the path the sidecar records, so whatever path it now reports, this is the sidecar's notebook.
            openedFromSidecarPath = sidecar is not null && string.Equals(path, sidecar.NotebookPath, StringComparison.Ordinal);
        }

        NotebookHandle handle = new(open.Id, string.IsNullOrWhiteSpace(open.Path) ? sidecar?.NotebookPath ?? "" : open.Path, open.Name);
        if (sidecar is not null && !openedFromSidecarPath && !string.Equals(sidecar.NotebookId, handle.Id, StringComparison.Ordinal) && DescribesAnotherNotebook(sidecar, handle.Path))
        {
            // The save guard, by the same rule as the choice: a sidecar is never rebuilt from, or saved over by, another notebook of the same name.
            Problem = $"The sidecar at {sidecarPath} describes the notebook at {sidecar.NotebookPath}, not the open notebook '{name}' at {handle.Path}, and was left as it is. Open the notebook it describes and run Refresh again.";
            log.Warning("{Problem}", Problem);
            return null;
        }

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

    /// <summary>
    /// Why the last Open returned null when the notebook could not be told apart from another open notebook of the same
    /// name, the sidecar found describes another notebook, or the URL the sidecar records could not be opened; null after
    /// any other outcome.
    /// </summary>
    public string? Problem { get; private set; }

    /// <summary>True for a folder path on disk, false for a URL (a notebook moved to OneDrive reports one) or nothing.</summary>
    private static bool IsLocalPath(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains("://", StringComparison.Ordinal);

    /// <summary>True for a URL path (https://d.docs.live.net/...), as OneNote reports a notebook stored on OneDrive.</summary>
    private static bool IsUrl(string? path) => !string.IsNullOrWhiteSpace(path) && path.Contains("://", StringComparison.Ordinal);

    /// <summary>True for a local folder path that is not on disk (any more).</summary>
    private static bool IsGoneLocalFolder(string path) => IsLocalPath(path) && !Directory.Exists(path);

    /// <summary>
    /// True when the local folder holds a notebook file anywhere inside: a table of contents (.onetoc2) or a section
    /// (.one). False when it is gone or holds neither. A folder that cannot be searched counts as holding one, so the run
    /// stops rather than take another notebook for the one that moved.
    /// </summary>
    private bool HoldsNotebookFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", EveryFileInside).Any(IsNotebookFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(ex, "The folder {Path} could not be searched for notebook files and is taken to hold some", folder);
            return true;
        }
    }

    private static bool IsNotebookFile(string file)
    {
        string extension = Path.GetExtension(file);
        return extension.Equals(".onetoc2", StringComparison.OrdinalIgnoreCase) || extension.Equals(".one", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The open notebook to use among those named like the request: with a sidecar, the one with its notebook ID, else the
    /// one at its path, else the only one of that name when the sidecar does not describe another notebook (see
    /// <see cref="DescribesAnotherNotebook"/>: only a notebook at a URL whose recorded local folder holds no notebook file
    /// qualifies); without a sidecar, the only one of that name, or the one in <paramref name="folder"/> when several are
    /// open. Null when a path has to be opened instead (or the run stops).
    /// </summary>
    private HierarchyNotebook? Choose(List<HierarchyNotebook> named, Sidecar? sidecar, string? folder, string name)
    {
        if (sidecar is not null)
        {
            HierarchyNotebook? match = named.FirstOrDefault(n => string.Equals(n.Id, sidecar.NotebookId, StringComparison.Ordinal))
                ?? named.FirstOrDefault(n => SamePath(n.Path, sidecar.NotebookPath));
            if (match is not null)
            {
                return match;
            }

            return named.Count == 1 && !DescribesAnotherNotebook(sidecar, named[0].Path) ? named[0] : null;
        }

        if (named.Count == 1)
        {
            return named[0];
        }

        return folder is null ? null : named.FirstOrDefault(n => SamePath(n.Path, Path.Combine(folder, name)));
    }

    /// <summary>
    /// True when the sidecar describes another notebook than the one at <paramref name="path"/>, which has another ID: the
    /// sidecar records a URL (a notebook on OneDrive is only ever its own by ID or path), or a local folder while
    /// <paramref name="path"/> is not a URL, or a local folder that still holds notebook files (see
    /// <see cref="HoldsNotebookFiles"/>). Only a notebook that OneNote reports at a URL (it moved to OneDrive) and whose
    /// old folder is gone or holds no notebook file is taken as the sidecar's own. A sidecar that records no path tells
    /// nothing, so it never describes another notebook.
    /// </summary>
    private bool DescribesAnotherNotebook(Sidecar sidecar, string? path)
    {
        string recorded = sidecar.NotebookPath;
        if (string.IsNullOrWhiteSpace(recorded) || SamePath(recorded, path))
        {
            return false;
        }

        if (IsUrl(recorded))
        {
            return true;
        }

        return !IsUrl(path) || HoldsNotebookFiles(recorded);
    }

    private static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.TrimEndingDirectorySeparator(a.Trim()), Path.TrimEndingDirectorySeparator(b.Trim()), StringComparison.OrdinalIgnoreCase);

    private static string Paths(IEnumerable<HierarchyNotebook> notebooks) =>
        string.Join("; ", notebooks.Select(n => string.IsNullOrWhiteSpace(n.Path) ? n.Id : n.Path));

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
