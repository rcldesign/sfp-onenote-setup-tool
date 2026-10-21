using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Calendar;
using Sfp.Refresh.Writer;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// A notebook generated into the fake OneNote in its own temp folder, its sidecar saved beside it, and the Refresh
/// services built over it the way the CLI builds them (a fixed clock, no sleeping, a capturing log and notifier).
/// </summary>
public sealed class RefreshHarness
{
    public static readonly DateOnly Jan4 = new(2027, 1, 4);

    public static readonly DateTimeOffset Now = new(2027, 1, 4, 5, 0, 0, TimeSpan.Zero);

    /// <param name="saveSidecar">Save the generated sidecar beside the notebook (false: the run rebuilds it).</param>
    /// <param name="calendars">The calendar source factory, or null for none.</param>
    /// <param name="shape">Changes the Generate options (see <see cref="FakeNotebook.Generate"/>).</param>
    /// <param name="folder">The notebooks folder; a new temp folder when null.</param>
    /// <param name="start">The quarter; Q1 2027 when null.</param>
    /// <param name="app">The fake OneNote to generate into; a new one when null.</param>
    public RefreshHarness(bool saveSidecar = true, ICalendarSourceFactory? calendars = null, Func<GenerateOptions, GenerateOptions>? shape = null, string? folder = null, QuarterSpec? start = null, FakeOneNoteApp? app = null)
    {
        Folder = folder ?? TestPaths.NewTempDir("refresh-harness");
        Notebook = FakeNotebook.Generate(start, Folder, app, shape: shape);
        Store = new SidecarStore();
        SidecarPath = Store.PathFor(Notebook.NotebookPath);
        if (saveSidecar)
        {
            Store.Save(SidecarPath, Notebook.Sidecar);
        }

        Log = new CapturingLog();
        Services = new RefreshServices
        {
            App = Notebook.App,
            Sidecars = Store,
            Manifest = Notebook.Manifest,
            Dates = new DateEngine(),
            Plans = new PlanBuilder(new DateEngine()),
            SetupReader = new SetupReader(),
            Propagators = (catalog, reader) => new Propagator(catalog, reader, Mono),
            Writer = new AnchorWriter(Notebook.App, Notebook.Options, Log.Logger),
            Measure = Mono,
            Log = Log.Logger,
            CalendarSources = calendars,
            Notifier = Notifier,
            SearchFolders = new[] { Folder },
            Clock = () => Now,
            ToolVersion = "9.9.9",
            LogPath = Path.Combine(Folder, "run.log"),
            IncludeBundledHolidays = false,
            OpenTimeout = TimeSpan.FromSeconds(4),
            Sleep = Sleeps.Add,
        };
    }

    public string Folder { get; }

    public FakeNotebook Notebook { get; }

    public SidecarStore Store { get; }

    public string SidecarPath { get; }

    public CapturingLog Log { get; }

    public FakeSourceFailureNotifier Notifier { get; } = new();

    /// <summary>Every wait the run asked for, in order (nothing really sleeps).</summary>
    public List<TimeSpan> Sleeps { get; } = new();

    public RefreshServices Services { get; set; }

    public FakeOneNoteApp App => Notebook.App;

    public string Name => Notebook.Plan.Name;

    public static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    public RefreshRequest Request(bool dryRun = false, bool calendar = false, bool writeInked = false, DateOnly? today = null, string? folder = null) =>
        new(Name, folder, calendar, 14, dryRun, writeInked, today ?? Jan4);

    public RefreshResult Run(bool dryRun = false, bool calendar = false, bool writeInked = false, DateOnly? today = null, string? folder = null) =>
        Run(Request(dryRun, calendar, writeInked, today, folder));

    public RefreshResult Run(RefreshRequest request) => new RefreshOrchestrator(Services).Run(request, null, CancellationToken.None);

    public Sidecar SavedSidecar() => Store.Load(SidecarPath)!;

    /// <summary>Types a value on the Setup page at a slot rectangle.</summary>
    public void Type(RectPt rect, string text) => Notebook.TypeUserText(PageKey.Setup, rect, text);
}

/// <summary>A calendar source factory: the bundled holiday list for a holidays source, else a source with the events or the failure the test sets.</summary>
public sealed class StubCalendars : ICalendarSourceFactory
{
    public IReadOnlyList<CalendarEvent> Events { get; set; } = Array.Empty<CalendarEvent>();

    public Exception? Failure { get; set; }

    public ICalendarSource Create(CalendarSource source) =>
        source.Kind == CalendarSourceKind.Holidays
            ? new HolidayCalendarSource(source, HolidayList.Load(Path.Combine(TestPaths.TemplatesDir, "holidays-us.json")))
            : new Source(this, source);

    private sealed class Source : ICalendarSource
    {
        private readonly StubCalendars owner;
        private readonly CalendarSource source;

        public Source(StubCalendars owner, CalendarSource source)
        {
            this.owner = owner;
            this.source = source;
        }

        public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
        {
            if (owner.Failure is not null)
            {
                throw owner.Failure;
            }

            return owner.Events.Select(e => e with { SourceLabel = source.Label }).ToList();
        }
    }
}
