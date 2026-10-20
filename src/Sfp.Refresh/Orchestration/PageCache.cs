using Serilog;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;

namespace Sfp.Refresh.Orchestration;

/// <summary>
/// Reads pages by key through the sidecar's page IDs, once each, so the dry run reads only the pages it needs and the
/// real run reads each page once before writing it. A page that fails to read (or has no ID) yields null and the
/// failure is kept for the summary; OneNote being unavailable propagates.
/// </summary>
public sealed class PageCache
{
    private readonly IOneNoteApp app;
    private readonly ILogger log;
    private readonly Dictionary<string, PageXml> pages;
    private readonly Dictionary<string, string> failures = new(StringComparer.Ordinal);
    private Sidecar sidecar;

    /// <param name="app">The run's COM session.</param>
    /// <param name="sidecar">Page IDs by key.</param>
    /// <param name="log">Run log.</param>
    /// <param name="preloaded">Pages already read (by the sidecar recovery), keyed by page key.</param>
    public PageCache(IOneNoteApp app, Sidecar sidecar, ILogger log, IReadOnlyDictionary<string, PageXml>? preloaded = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(log);
        this.app = app;
        this.sidecar = sidecar;
        this.log = log;
        pages = preloaded is null
            ? new Dictionary<string, PageXml>(StringComparer.Ordinal)
            : new Dictionary<string, PageXml>(preloaded, StringComparer.Ordinal);
    }

    /// <summary>The sidecar the IDs come from; replaced when the run updates it.</summary>
    public Sidecar Sidecar
    {
        get => sidecar;
        set => sidecar = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Page key value to the reason it could not be read.</summary>
    public IReadOnlyDictionary<string, string> Failures => failures;

    /// <summary>The OneNote page ID of a key, or null when the sidecar has none.</summary>
    public string? PageId(PageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return sidecar.Pages.TryGetValue(key.Value, out SidecarPage? page) ? page.PageId : null;
    }

    /// <summary>The page, read once; null when it has no ID or the read failed.</summary>
    public PageXml? Read(PageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (pages.TryGetValue(key.Value, out PageXml? cached))
        {
            return cached;
        }

        if (failures.ContainsKey(key.Value))
        {
            return null;
        }

        string? id = PageId(key);
        if (id is null)
        {
            failures[key.Value] = "no page ID in the sidecar";
            return null;
        }

        try
        {
            PageXml page = PageXmlParser.Parse(app.GetPageContent(id, PageInfo.Basic));
            pages[key.Value] = page;
            return page;
        }
        catch (OneNoteUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is OneNoteException or FormatException or System.Xml.XmlException)
        {
            log.Warning(ex, "Page {Key} ({Id}) could not be read", key, id);
            failures[key.Value] = ex.Message;
            return null;
        }
    }

    /// <summary>Forgets a page so the next read fetches it again (after a write).</summary>
    public void Invalidate(PageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        pages.Remove(key.Value);
        failures.Remove(key.Value);
    }
}
