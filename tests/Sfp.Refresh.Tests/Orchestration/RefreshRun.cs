using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Calendar;
using Sfp.Refresh.Tests.Support;
using Sfp.Refresh.Writer;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// A generated Q1 2027 notebook on the fake app with its sidecar saved, refreshed on Mon Jan 4, 2027 with the given
/// calendar sources and no bundled holiday list.
/// </summary>
internal sealed class RefreshRun
{
    private readonly ICalendarSourceFactory? calendars;

    public RefreshRun(ICalendarSourceFactory? calendars = null)
    {
        this.calendars = calendars;
        Notebook = FakeNotebook.Generate(folder: TestPaths.NewTempDir("refresh-run"));
        SidecarPath = Store.PathFor(Notebook.NotebookPath);
        Store.Save(SidecarPath, Notebook.Sidecar);
    }

    public FakeNotebook Notebook { get; }

    public SidecarStore Store { get; } = new();

    public string SidecarPath { get; }

    /// <summary>Types text on the Setup page as the user would.</summary>
    public void Type(RectPt rect, string text) => Notebook.TypeUserText(PageKey.Setup, rect, text);

    public RefreshResult Run(bool calendar = true) =>
        new RefreshOrchestrator(Services()).Run(new RefreshRequest("SFP Q1 2027", null, calendar, 14, false, false, new DateOnly(2027, 1, 4)), null, CancellationToken.None);

    /// <summary>The text of the tool's outline in a slot, as the page reads it back.</summary>
    public string SlotText(PageKey page, string slotId)
    {
        string id = Store.Load(SidecarPath)!.Pages[page.Value].Anchors[slotId];
        return Notebook.Read(page).Outlines.Single(o => o.ObjectId == id).PlainText;
    }

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private RefreshServices Services()
    {
        CapturingLog log = new();
        return new RefreshServices
        {
            App = Notebook.App,
            Sidecars = Store,
            Manifest = RefreshManifest.Shipped,
            Dates = new DateEngine(),
            Plans = new PlanBuilder(new DateEngine()),
            SetupReader = new SetupReader(),
            Propagators = (catalog, reader) => new Propagator(catalog, reader, Mono),
            Writer = new AnchorWriter(Notebook.App, Notebook.Options, log.Logger),
            Measure = Mono,
            Log = log.Logger,
            CalendarSources = calendars,
            Notifier = new FakeSourceFailureNotifier(),
            SearchFolders = new[] { Notebook.Folder },
            Clock = () => new DateTimeOffset(2027, 1, 4, 5, 0, 0, TimeSpan.Zero),
            ToolVersion = "9.9.9",
            LogPath = Path.Combine(Notebook.Folder, "run.log"),
            IncludeBundledHolidays = false,
            OpenTimeout = TimeSpan.FromSeconds(4),
            Sleep = _ => { },
        };
    }
}

/// <summary>Calendar sources by Setup label: each returns what its function gives, or throws what it throws.</summary>
internal sealed class LabelledSources : ICalendarSourceFactory
{
    private readonly Dictionary<string, Func<IReadOnlyList<CalendarEvent>>> sources = new(StringComparer.Ordinal);

    public LabelledSources Add(string label, Func<IReadOnlyList<CalendarEvent>> events)
    {
        sources[label] = events;
        return this;
    }

    public ICalendarSource Create(CalendarSource source) =>
        sources.TryGetValue(source.Label, out Func<IReadOnlyList<CalendarEvent>>? events)
            ? new Source(events)
            : throw new InvalidOperationException("Unexpected calendar source " + source.Label);

    private sealed class Source : ICalendarSource
    {
        private readonly Func<IReadOnlyList<CalendarEvent>> events;

        public Source(Func<IReadOnlyList<CalendarEvent>> events)
        {
            this.events = events;
        }

        public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to) => events();
    }
}
