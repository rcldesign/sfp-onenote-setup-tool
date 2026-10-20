using Sfp.Core.Plan;

namespace Sfp.OneNote.Generate;

/// <summary>Builds the sidecar record after a Generate run from the handles and anchor IDs the run collected.</summary>
public static class SidecarBuilder
{
    /// <param name="request">The run's request (manifest id and quarter come from it).</param>
    /// <param name="plan">The plan (section names, page keys).</param>
    /// <param name="notebook">The created notebook.</param>
    /// <param name="sections">Section handle per plan section name.</param>
    /// <param name="pages">Page handle per page key.</param>
    /// <param name="anchorIds">Anchor object IDs per page key (pages without anchors may be absent).</param>
    /// <param name="toolVersion">The tool version string.</param>
    /// <param name="generated">When the run finished.</param>
    public static Sidecar Build(
        GenerateRequest request,
        NotebookPlan plan,
        NotebookHandle notebook,
        IReadOnlyDictionary<string, SectionHandle> sections,
        IReadOnlyDictionary<PageKey, PageHandle> pages,
        IReadOnlyDictionary<PageKey, IReadOnlyDictionary<string, string>> anchorIds,
        string toolVersion,
        DateTimeOffset generated)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(anchorIds);

        Dictionary<string, string> sectionIds = new(StringComparer.Ordinal);
        foreach (SectionSpec section in plan.Sections)
        {
            if (sections.TryGetValue(section.Name, out SectionHandle? handle))
            {
                sectionIds[section.Name] = handle.Id;
            }
        }

        Dictionary<string, SidecarPage> pageRecords = new(StringComparer.Ordinal);
        foreach (PageSpec page in plan.Pages)
        {
            if (!pages.TryGetValue(page.Key, out PageHandle? handle))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> anchors = anchorIds.TryGetValue(page.Key, out IReadOnlyDictionary<string, string>? ids)
                ? ids
                : new Dictionary<string, string>(StringComparer.Ordinal);
            pageRecords[page.Key.Value] = new SidecarPage { PageId = handle.Id, SectionId = handle.Section.Id, Anchors = anchors };
        }

        return new Sidecar
        {
            ToolVersion = toolVersion,
            ManifestId = request.Manifest.TemplateId,
            Quarter = request.Options.Start,
            NotebookName = notebook.Name,
            NotebookPath = notebook.Path,
            NotebookId = notebook.Id,
            Sections = sectionIds,
            Pages = pageRecords,
            Generated = generated,
            PreviousNotebook = request.Options.PreviousNotebook,
        };
    }
}
