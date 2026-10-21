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
/// refresh-6: during a partial calendar outage a slot that a live source changes keeps the dead source's lines; the run
/// only adds, says so truthfully, and changes nothing more while the outage lasts or once the source is back.
/// </summary>
public class CalendarOutageTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly DateOnly Jan4 = new(2027, 1, 4);
    private static readonly PageKey TueJan5 = PageKey.Day(new DateOnly(2027, 1, 5));
    private static readonly PageKey Week02 = PageKey.WeekOverview(2);

    private static double Mono(string text, double sizePt) => text.Length * sizePt * 0.55;

    private static CalendarEvent Event(string uid, string title, int hour, string label)
    {
        DateTime start = new(2027, 1, 5, hour, 0, 0);
        return new CalendarEvent(uid, title, start, start.AddHours(1), false, false, false, label, null);
    }

    [Fact]
    public void A_dead_sources_line_stays_in_a_slot_the_live_source_adds_to()
    {
        TwoSources calendars = new();
        FakeNotebook notebook = FakeNotebook.Generate(folder: TestPaths.NewTempDir("outage"));
        SidecarStore store = new();
        string sidecarPath = store.PathFor(notebook.NotebookPath);
        store.Save(sidecarPath, notebook.Sidecar);
        notebook.TypeUserText(PageKey.Setup, S.CalendarSources[0], "Work | https://example.com/work.ics");
        notebook.TypeUserText(PageKey.Setup, S.CalendarSources[1], "Personal | https://example.com/me.ics");
        calendars.Work = new[] { Event("w1", "Staff meeting", 9, "Work") };
        calendars.Personal = new[] { Event("p1", "Lunch with Ana", 12, "Personal") };
        RefreshResult Run() => new RefreshOrchestrator(Services(notebook, store, calendars)).Run(new RefreshRequest("SFP Q1 2027", null, true, 14, false, false, Jan4), null, CancellationToken.None);
        Run().Summary!.EventsWritten.Should().Be(3, "the 9 and 12 rows and the overview column");
        string Column() => notebook.Read(Week02).Outlines.Single(o => o.ObjectId == store.Load(sidecarPath)!.Pages[Week02.Value].Anchors["overviewDay:1"]).PlainText;
        Column().Should().Be("12:00 Lunch with Ana\n09:00 Staff meeting");

        calendars.WorkDown = true;
        calendars.Personal = new[] { Event("p1", "Lunch with Ana", 12, "Personal"), Event("p2", "Dentist", 15, "Personal") };
        RefreshResult outage = Run();

        outage.ExitCode.Should().Be(0, outage.Error);
        outage.Summary!.FailedSources.Should().Equal("Work: The request timed out after 30 s.");
        outage.Summary.Warnings.Should().Contain("Some calendar sources failed; every event line already on the pages was kept, and new events were added only where a slot had room beside them.");
        outage.Summary.Changes.Select(c => (c.Page.Value, c.SlotId, c.Kind, c.Blocked, c.NewText)).Should().BeEquivalentTo(new[]
        {
            (TueJan5.Value, "timeBlockEvent:15", ChangeKind.Create, false, "15:00 Dentist"),
            (Week02.Value, "overviewDay:1", ChangeKind.Update, false, "15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting"),
        });
        Column().Should().Be("15:00 Dentist\n12:00 Lunch with Ana\n09:00 Staff meeting", "the dead source's meeting stays and the dentist is added");
        notebook.Read(TueJan5).Outlines.Should().Contain(o => o.PlainText == "9:00 Staff meeting");

        Run().Summary!.Changes.Should().BeEmpty("nothing changed while the outage lasts");
        calendars.WorkDown = false;
        Run().Summary!.Changes.Should().BeEmpty("with Work back, the column already holds every event");
    }

    private static RefreshServices Services(FakeNotebook notebook, SidecarStore store, ICalendarSourceFactory calendars)
    {
        CapturingLog log = new();
        return new RefreshServices
        {
            App = notebook.App,
            Sidecars = store,
            Manifest = RefreshManifest.Shipped,
            Dates = new DateEngine(),
            Plans = new PlanBuilder(new DateEngine()),
            SetupReader = new SetupReader(),
            Propagators = (catalog, reader) => new Propagator(catalog, reader, Mono),
            Writer = new AnchorWriter(notebook.App, notebook.Options, log.Logger),
            Measure = Mono,
            Log = log.Logger,
            CalendarSources = calendars,
            Notifier = new FakeSourceFailureNotifier(),
            SearchFolders = new[] { notebook.Folder },
            Clock = () => new DateTimeOffset(2027, 1, 4, 5, 0, 0, TimeSpan.Zero),
            ToolVersion = "9.9.9",
            LogPath = Path.Combine(notebook.Folder, "run.log"),
            IncludeBundledHolidays = false,
            OpenTimeout = TimeSpan.FromSeconds(4),
            Sleep = _ => { },
        };
    }

    /// <summary>A Work source that can time out and a Personal source, by Setup label.</summary>
    private sealed class TwoSources : ICalendarSourceFactory
    {
        public IReadOnlyList<CalendarEvent> Work { get; set; } = Array.Empty<CalendarEvent>();

        public IReadOnlyList<CalendarEvent> Personal { get; set; } = Array.Empty<CalendarEvent>();

        public bool WorkDown { get; set; }

        public ICalendarSource Create(CalendarSource source) => source.Label switch
        {
            "Work" => new Source(() => WorkDown ? throw new TimeoutException("The request timed out after 30 s.") : Work),
            "Personal" => new Source(() => Personal),
            _ => throw new InvalidOperationException("Unexpected calendar source " + source.Label),
        };

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
}
