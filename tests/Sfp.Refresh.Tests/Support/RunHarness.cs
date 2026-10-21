using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Writer;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Support;

/// <summary>
/// A Refresh run over a fake notebook generated into a temp folder, its sidecar saved beside it: the services the CLI
/// builds, with the fake app, a fixed clock (Mon Jan 4 2027, 05:00 UTC), a monospace text measure and no waiting.
/// </summary>
public sealed class RunHarness
{
    public static readonly DateOnly Jan4 = new(2027, 1, 4);

    public static readonly PageKey MonJan4 = PageKey.Day(Jan4);

    public static readonly DateTimeOffset Now = new(2027, 1, 4, 5, 0, 0, TimeSpan.Zero);

    /// <param name="saveSidecar">Save the generated sidecar beside the notebook.</param>
    /// <param name="calendars">The calendar sources, or null for no calendar import.</param>
    /// <param name="window">The Time Blocks window the notebook is generated with.</param>
    /// <param name="app">A fake OneNote that already holds other notebooks, or null for a fresh one.</param>
    public RunHarness(bool saveSidecar = true, ICalendarSourceFactory? calendars = null, TimeBlockWindow? window = null, FakeOneNoteApp? app = null)
    {
        Folder = TestPaths.NewTempDir("refresh-run");
        Notebook = FakeNotebook.Generate(folder: Folder, app: app, window: window);
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
            SearchFolders = new[] { Folder },
            Clock = () => Now,
            ToolVersion = "9.9.9",
            IncludeBundledHolidays = false,
            OpenTimeout = TimeSpan.FromSeconds(4),
            Sleep = _ => { },
        };
    }

    public string Folder { get; }

    public FakeNotebook Notebook { get; }

    public FakeOneNoteApp App => Notebook.App;

    public SetupPageSpec Setup => Notebook.Manifest.Generated.Setup;

    public SidecarStore Store { get; }

    public string SidecarPath { get; }

    public CapturingLog Log { get; }

    public RefreshServices Services { get; set; }

    public static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    public static RefreshRequest Request(bool dryRun = false, bool calendar = false, DateOnly? today = null, string? folder = null) =>
        new("SFP Q1 2027", folder, calendar, 14, dryRun, false, today ?? Jan4);

    public RefreshResult Run(RefreshRequest? request = null) =>
        new RefreshOrchestrator(Services).Run(request ?? Request(), null, CancellationToken.None);

    public Sidecar Saved() => Store.Load(SidecarPath)!;

    /// <summary>Types a value into a Setup slot rectangle.</summary>
    public void Type(RectPt rect, string text) => Notebook.TypeUserText(PageKey.Setup, rect, text);

    /// <summary>Replaces the text of the user's Setup outline that holds <paramref name="oldText"/>.</summary>
    public void RetypeSetup(string oldText, string newText) =>
        Notebook.EditOutlineText(PageKey.Setup, Notebook.Read(PageKey.Setup).Outlines.Single(o => o.PlainText == oldText).ObjectId, newText);

    /// <summary>The non-background outlines on a page whose text is <paramref name="text"/>.</summary>
    public IReadOnlyList<OutlineInfo> OutlinesWithText(PageKey key, string text) =>
        Notebook.Read(key).Outlines.Where(o => !o.IsBackgroundImage && o.PlainText == text).ToList();
}

/// <summary>A calendar source factory whose every Setup source returns <see cref="Events"/> (labelled with the source).</summary>
public sealed class FixedCalendarFactory : ICalendarSourceFactory
{
    public IReadOnlyList<CalendarEvent> Events { get; set; } = Array.Empty<CalendarEvent>();

    public ICalendarSource Create(CalendarSource source) => new Source(this, source.Label);

    /// <summary>A timed event on <paramref name="day"/> from <paramref name="hour"/> for an hour.</summary>
    public static CalendarEvent Event(string title, DateOnly day, int hour)
    {
        DateTime start = day.ToDateTime(new TimeOnly(hour, 0));
        return new CalendarEvent(title + "@" + start.ToString("s", System.Globalization.CultureInfo.InvariantCulture), title, start, start.AddHours(1), false, false, false, "Work", null);
    }

    private sealed class Source : ICalendarSource
    {
        private readonly FixedCalendarFactory owner;
        private readonly string label;

        public Source(FixedCalendarFactory owner, string label)
        {
            this.owner = owner;
            this.label = label;
        }

        public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to) => owner.Events.Select(e => e with { SourceLabel = label }).ToList();
    }
}
