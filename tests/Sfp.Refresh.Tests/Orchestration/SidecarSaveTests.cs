using Serilog.Events;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-4 c): the end-of-run sidecar save is tried three times with short waits, since a sync client or a
/// virus scanner may hold the file; when every attempt fails the run fails (exit 5) naming the path and saying the next
/// run re-adopts what it wrote.
/// </summary>
public class SidecarSaveTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    [Fact]
    public void A_save_that_fails_twice_is_tried_again_and_the_run_succeeds()
    {
        RefreshHarness h = new();
        FlakyStore store = new(h.Store) { Failures = 2 };
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeTrue(result.Error);
        result.ExitCode.Should().Be(0);
        store.Attempts.Should().Be(3);
        h.Sleeps.Should().Equal(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
        h.SavedSidecar().Pages[MonJan4.Value].Anchors.Should().ContainKey("objectiveLegend", "the third attempt saved what the run wrote");
        h.Log.Messages(LogEventLevel.Warning).Where(m => m.Contains("trying again", StringComparison.Ordinal)).Should().HaveCount(2);
        h.Run().Summary!.Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_save_that_fails_three_times_fails_the_run_naming_the_path()
    {
        RefreshHarness h = new();
        FlakyStore store = new(h.Store) { Failures = 3 };
        h.Services = h.Services with { Sidecars = store };
        h.Type(S.Objectives[0], "Ship the connector");

        RefreshResult result = h.Run();

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
        result.Error.Should().Be(string.Format(System.Globalization.CultureInfo.InvariantCulture, RefreshOrchestrator.SidecarSaveFailedFormat, h.SidecarPath, 3, FlakyStore.Message));
        result.Error.Should().Contain(h.SidecarPath).And.Contain("re-adopts");
        store.Attempts.Should().Be(3);
        result.Summary!.ValuesWritten.Should().Be(107, "the pages were written before the save");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
        h.SavedSidecar().LastRefresh.Should().BeNull("nothing of this run reached the file");
        FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(1, "a run whose state was not saved counts toward the streak");
    }

    /// <summary>The real store, whose first saves fail with a sharing violation.</summary>
    private sealed class FlakyStore : ISidecarStore
    {
        public const string Message = "The process cannot access the file because it is being used by another process.";

        private readonly ISidecarStore inner;

        public FlakyStore(ISidecarStore inner)
        {
            this.inner = inner;
        }

        public int Failures { get; set; }

        public int Attempts { get; private set; }

        public string PathFor(string notebookFolder) => inner.PathFor(notebookFolder);

        public Sidecar? Load(string path) => inner.Load(path);

        public void Save(string path, Sidecar sidecar)
        {
            Attempts++;
            if (Failures > 0)
            {
                Failures--;
                throw new IOException(Message);
            }

            inner.Save(path, sidecar);
        }
    }
}
