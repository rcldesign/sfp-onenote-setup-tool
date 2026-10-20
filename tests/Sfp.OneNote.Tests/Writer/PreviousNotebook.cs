using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Xml;
using Sfp.OneNote.Writer;
using Sfp.OneNote.Xml;
using Sfp.Render;

namespace Sfp.OneNote.Tests.Writer;

/// <summary>
/// A previous quarter's notebook as Generate leaves it, built straight into the in-memory OneNote: "SFP Q4 2026" with a
/// Reviews section holding the "Q4 2026 Review" page (the locked sheet, its sfp.key meta and the Scorecard link the tool
/// wrote, nothing in the Next quarter slot) and its state file beside the folder, recording the page and the Scorecard
/// link's object ID the way the generate run does.
/// </summary>
internal sealed class PreviousNotebook
{
    public const string Name = "SFP Q4 2026";

    public const string ScorecardHref = "onenote:#Quarter%20scorecard&section-id={prev-reviews}&page-id={prev-scorecard}&end";

    private PreviousNotebook(string folder, string sidecarPath, string notebookId, string sectionId, string pageId, string scorecardObjectId)
    {
        Folder = folder;
        SidecarPath = sidecarPath;
        NotebookId = notebookId;
        SectionId = sectionId;
        PageId = pageId;
        ScorecardObjectId = scorecardObjectId;
    }

    /// <summary>The notebook folder (the --previous argument).</summary>
    public string Folder { get; }

    /// <summary>The state file beside the folder.</summary>
    public string SidecarPath { get; }

    public string NotebookId { get; }

    public string SectionId { get; }

    /// <summary>The Q4 2026 Review page.</summary>
    public string PageId { get; }

    /// <summary>Object ID of the Scorecard link outline on the review page.</summary>
    public string ScorecardObjectId { get; }

    /// <summary>
    /// Generates the notebook into <paramref name="app"/> under <paramref name="notebooksFolder"/> with the page XML
    /// options given (default: titled, scale 1). The notebook folder is created on disk and the state file saved beside
    /// it. With <paramref name="movedTo"/> the notebook is the one moved to OneDrive: OneNote knows it by that location,
    /// the state file (left beside the old folder) records it there, and the old folder is not on disk.
    /// </summary>
    public static PreviousNotebook Create(FakeOneNoteApp app, string notebooksFolder, Manifest manifest, PageXmlOptions? options = null, string? movedTo = null)
    {
        PageXmlOptions xml = options ?? MonJan4.Options(manifest);
        string folder = Path.Combine(notebooksFolder, Name);
        string location = movedTo ?? folder;
        if (movedTo is null)
        {
            Directory.CreateDirectory(folder);
        }

        string notebookId = app.OpenHierarchy(location, null, CreateFileType.Notebook);
        string sectionId = app.OpenHierarchy(Path.Combine(location, "Reviews.one"), notebookId, CreateFileType.Section);
        string pageId = app.CreateNewPage(sectionId, xml.HideTitle ? NewPageStyle.BlankPageNoTitle : NewPageStyle.BlankPageWithTitle);
        PageSpec review = Review();
        app.UpdateHierarchy(HierarchyXmlBuilder.SectionPages(sectionId, "Reviews", null, new[] { (pageId, review.Title, 1) }));

        FixedAnchor scorecard = manifest.Links.QuarterReviewScorecard;
        ResolvedAnchor link = new(
            new AnchorPlacement("quarterReviewScorecard", (float)scorecard.X, (float)scorecard.Y, (float)scorecard.W, (float)scorecard.H, "Scorecard ›", scorecard.Align, (float)scorecard.SizePt) { Kind = AnchorKind.Link },
            ScorecardHref,
            true);
        RenderedPage art = new(review.Key, MonJan4.Png, 3398, 2353, new[] { link.Placement });
        app.UpdatePageContent(PageXmlBuilder.Build(pageId, review, art, new[] { link }, xml), DateTime.MinValue);
        PageXml written = PageXmlParser.Parse(app.GetPageContent(pageId, PageInfo.Basic));
        string scorecardId = AnchorIdMatcher.Match(written, new[] { link }, xml).ObjectIds["quarterReviewScorecard"];

        Sidecar sidecar = new()
        {
            ToolVersion = "1.0.0",
            ManifestId = manifest.TemplateId,
            Quarter = new QuarterSpec(2026, 10),
            NotebookName = Name,
            NotebookPath = location,
            NotebookId = notebookId,
            Sections = new Dictionary<string, string>(StringComparer.Ordinal) { ["Reviews"] = sectionId },
            Pages = new Dictionary<string, SidecarPage>(StringComparer.Ordinal)
            {
                [PageKey.QuarterlyReview.Value] = new SidecarPage
                {
                    PageId = pageId,
                    SectionId = sectionId,
                    Anchors = new Dictionary<string, string>(StringComparer.Ordinal) { ["quarterReviewScorecard"] = scorecardId },
                },
            },
            Generated = new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero),
        };
        SidecarStore store = new();
        string sidecarPath = store.PathFor(folder);
        store.Save(sidecarPath, sidecar);
        return new PreviousNotebook(folder, sidecarPath, notebookId, sectionId, pageId, scorecardId);
    }

    /// <summary>The quarterly review page spec of Q4 2026 (the week-13 reflection art, PDF page 132).</summary>
    public static PageSpec Review(PageKey? key = null) => new(
        key ?? PageKey.QuarterlyReview,
        PageType.QuarterlyReview,
        "Q4 2026 Review",
        1,
        132,
        new DateOnly(2026, 10, 1),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Qn"] = "Q4", ["YYYY"] = "2026" });

    /// <summary>The state file as saved now.</summary>
    public Sidecar Sidecar() => new SidecarStore().Load(SidecarPath)!;

    /// <summary>The review page as OneNote returns it now (without binary data).</summary>
    public PageXml Read(FakeOneNoteApp app) => PageXmlParser.Parse(app.GetPageContent(PageId, PageInfo.Basic));
}
