using Sfp.OneNote;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// Production <see cref="INotebookRegistry"/>: scans the given folders for sidecar files named by the
/// <see cref="SidecarStore.PathFor"/> convention ("{NotebookFolder}.sfp-state.json" beside each notebook folder) and
/// lists the notebooks they describe. Unreadable files are skipped.
/// </summary>
public sealed class NotebookRegistry : INotebookRegistry
{
    private readonly ISidecarStore store;

    public NotebookRegistry(ISidecarStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>The file pattern a sidecar matches.</summary>
    public static string Pattern => "*." + Sidecar.FileName;

    /// <inheritdoc />
    public IReadOnlyList<KnownNotebook> List(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        List<KnownNotebook> known = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            string full;
            try
            {
                full = Path.GetFullPath(folder);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!seen.Add(full) || !Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, Pattern, SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                KnownNotebook? notebook = TryDescribe(file);
                if (notebook is not null)
                {
                    known.Add(notebook);
                }
            }
        }

        return known;
    }

    private KnownNotebook? TryDescribe(string sidecarPath)
    {
        Sidecar? sidecar;
        try
        {
            sidecar = store.Load(sidecarPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }

        if (sidecar is null)
        {
            return null;
        }

        string path = string.IsNullOrWhiteSpace(sidecar.NotebookPath) ? NotebookFolderOf(sidecarPath) : sidecar.NotebookPath;
        return new KnownNotebook(sidecar.NotebookName, path, sidecar.Quarter, sidecar.Generated, sidecar.LastRefresh, sidecarPath);
    }

    /// <summary>"C:\Notebooks\SFP Q1 2027" from "C:\Notebooks\SFP Q1 2027.sfp-state.json".</summary>
    public static string NotebookFolderOf(string sidecarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        string file = Path.GetFileName(sidecarPath);
        string suffix = "." + Sidecar.FileName;
        string name = file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? file[..^suffix.Length] : Path.GetFileNameWithoutExtension(file);
        return Path.Combine(Path.GetDirectoryName(sidecarPath) ?? "", name);
    }
}
