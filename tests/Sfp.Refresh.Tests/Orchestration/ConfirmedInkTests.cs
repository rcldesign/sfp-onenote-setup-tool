using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Tests.Support;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Propagation;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 2 (refresh-2): a run told to write inked values records them in the sidecar as confirmed, and a later run
/// without that confirmation (the 05:00 task) treats a recorded value as typed while its Setup slot still reads the same
/// text, so it never takes it out of a legend. A changed inked value is held back as before; a typed one is written.
/// </summary>
public class ConfirmedInkTests
{
    private static readonly Manifest M = RefreshManifest.Shipped;
    private static readonly SetupPageSpec S = M.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));
    private static readonly PageKey January = PageKey.Calendar(2027, 1);
    private static readonly DateOnly Jan5 = new(2027, 1, 5);

    private static string SchemaPath => Path.Combine(TestPaths.RepoRoot, "tests", "Sfp.OneNote.Tests", "schema", "sfp-state.schema.json");

    [Fact]
    public void A_confirmed_run_followed_by_an_unconfirmed_run_plans_no_legend_change()
    {
        (RefreshHarness h, _) = ConfirmedObjective3();
        InkSetup(h, S.Habits[1], "Read");
        h.Run(writeInked: true).Success.Should().BeTrue("the habit is confirmed too");
        string habitLegend = HabitLegend(h);
        habitLegend.Should().Contain("Read");
        Sidecar saved = h.SavedSidecar();
        saved.ConfirmedInked.Should().Equal(new Dictionary<string, string> { ["objective:3"] = "Launch v2", ["habit:2"] = "Read" });
        JsonSchemaLite.AssertValid(File.ReadAllText(SchemaPath), File.ReadAllText(h.SidecarPath));
        int updates = h.App.CallCount("UpdatePageContent");

        RefreshResult preview = h.Run(dryRun: true, today: Jan5);
        RefreshResult daily = h.Run(today: Jan5);

        preview.Summary!.Changes.Should().BeEmpty("the preview of the daily task plans nothing either");
        preview.Summary.Skipped.Should().Be(0);
        daily.Success.Should().BeTrue(daily.Error);
        daily.Summary!.Changes.Should().BeEmpty("the daily task neither strips O3 or the habit from the legends nor holds back their own slots");
        h.App.CallCount("UpdatePageContent").Should().Be(updates, "nothing is written");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v2");
        HabitLegend(h).Should().Be(habitLegend);
        h.SavedSidecar().ConfirmedInked.Should().Equal(saved.ConfirmedInked, "the values stay confirmed while the Setup slots read them");
        h.Run(today: Jan5.AddDays(1)).Summary!.Changes.Should().BeEmpty("and on every later morning");
    }

    [Fact]
    public void A_changed_inked_value_is_held_back_as_before_and_its_confirmation_is_dropped()
    {
        (RefreshHarness h, string ink) = ConfirmedObjective3();
        Erase(h, ink);
        InkSetup(h, S.Objectives[2], "Launch", "v3");

        RefreshResult daily = h.Run(today: Jan5);

        daily.Success.Should().BeTrue(daily.Error);
        daily.Summary!.Changes.Where(c => c.SlotId == "objectiveLegend").Should().HaveCount(104).And.AllSatisfy(c =>
        {
            c.NewText.Should().Be("O1 Ship the connector", "an inked value that is not confirmed is left out of the legend, as before");
            c.Blocked.Should().BeFalse();
        });
        daily.Summary.Changes.Where(c => c.SlotId != "objectiveLegend").Should().HaveCount(3).And.AllSatisfy(c =>
        {
            c.NewText.Should().Be("Launch v3");
            c.Blocked.Should().BeTrue();
            c.Reason.Should().Be(RefreshOrchestrator.InkedSkipReason);
        });
        h.SavedSidecar().ConfirmedInked.Should().BeEmpty("the confirmation was for 'Launch v2'");

        RefreshResult confirmed = h.Run(writeInked: true, today: Jan5);

        confirmed.Summary!.ValuesWritten.Should().Be(104 + 3);
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v3");
        h.SavedSidecar().ConfirmedInked.Should().Equal(new Dictionary<string, string> { ["objective:3"] = "Launch v3" });
        h.Run(today: Jan5.AddDays(1)).Summary!.Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_typed_value_in_place_of_a_confirmed_inked_one_is_written_and_the_confirmation_dropped()
    {
        (RefreshHarness h, string ink) = ConfirmedObjective3();
        Erase(h, ink);
        h.Type(S.Objectives[2], "Launch v3");

        RefreshResult daily = h.Run(today: Jan5);

        daily.Success.Should().BeTrue(daily.Error);
        daily.Summary!.Changes.Should().NotContain(c => c.Blocked);
        daily.Summary.ValuesWritten.Should().Be(104 + 3);
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v3");
        h.SavedSidecar().ConfirmedInked.Should().BeEmpty();
    }

    [Fact]
    public void Only_a_finished_run_told_to_write_inked_values_records_a_confirmation()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        InkSetup(h, S.Objectives[2], "Launch", "v2");

        h.Run().Success.Should().BeTrue();
        h.SavedSidecar().ConfirmedInked.Should().BeEmpty("the daily task confirms nothing");
        h.Run(dryRun: true, writeInked: true).Success.Should().BeTrue();
        h.SavedSidecar().ConfirmedInked.Should().BeEmpty("a preview writes nothing and confirms nothing");
        using CancellationTokenSource cancel = new();
        RefreshResult cancelled = new RefreshOrchestrator(h.Services).Run(h.Request(writeInked: true), new CancelOnPage(2, cancel), cancel.Token);
        cancelled.Error.Should().Be("Cancelled.");
        h.SavedSidecar().Pages[PageKey.Annual(1).Value].AnchorTexts.Should().Contain("annualRow:3", "Launch v2", "the first page was written before the cancel");
        h.SavedSidecar().ConfirmedInked.Should().BeEmpty("a run cancelled part way confirms nothing");

        RefreshResult next = h.Run();

        next.Success.Should().BeTrue(next.Error);
        next.Summary!.Changes.Should().NotContain(c => c.SlotId == "objectiveLegend" && c.NewText.Contains("O3", StringComparison.Ordinal), "objective 3 is still not confirmed");
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector");
    }

    [Fact]
    public void The_record_keeps_a_confirmation_only_while_the_setup_slot_reads_the_same_inked_text()
    {
        string?[] objectives = new string?[8];
        objectives[0] = "Ship the connector";
        objectives[2] = "Launch v2";
        string?[] habits = new string?[16];
        habits[1] = "Read";
        SetupValues values = new(objectives, new string?[3], new string?[3], habits, Array.Empty<CalendarSource>(), true) { InkedSlots = new[] { "objective:3", "habit:2" } };
        Sidecar none = new() { ToolVersion = "t", ManifestId = "m", Quarter = new QuarterSpec(2027, 1), NotebookName = "n", NotebookPath = "p", NotebookId = "i" };
        Sidecar recorded = none with { ConfirmedInked = new Dictionary<string, string> { ["objective:3"] = "Launch v2", ["habit:2"] = "Run", ["objective:1"] = "Ship the connector" } };

        Propagator.ConfirmedInked(values, none, confirmedNow: true).Should().Equal(new Dictionary<string, string> { ["objective:3"] = "Launch v2", ["habit:2"] = "Read" });
        Propagator.ConfirmedInked(values, none, confirmedNow: false).Should().BeEmpty();
        Propagator.ConfirmedInked(values, recorded, confirmedNow: false).Should().Equal(
            new Dictionary<string, string> { ["objective:3"] = "Launch v2" },
            "habit 2 now reads other ink and objective 1 is typed");
    }

    /// <summary>Objective 1 typed and objective 3 inked "Launch v2", confirmed by a run that writes inked values; returns the ink outline's ID.</summary>
    private static (RefreshHarness H, string Ink) ConfirmedObjective3()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        string ink = InkSetup(h, S.Objectives[2], "Launch", "v2");
        RefreshResult confirmed = h.Run(writeInked: true);
        confirmed.Success.Should().BeTrue(confirmed.Error);
        h.Notebook.Read(MonJan4).Outlines.Should().Contain(o => o.PlainText == "O1 Ship the connector  O3 Launch v2");
        return (h, ink);
    }

    /// <summary>Writes ink words in a Setup slot and returns the new outline's ID.</summary>
    private static string InkSetup(RefreshHarness h, RectPt rect, params string[] words)
    {
        HashSet<string> before = h.Notebook.Read(PageKey.Setup).Outlines.Select(o => o.ObjectId).ToHashSet(StringComparer.Ordinal);
        h.Notebook.InkUserText(PageKey.Setup, rect, words);
        return h.Notebook.Read(PageKey.Setup).Outlines.Single(o => !before.Contains(o.ObjectId)).ObjectId;
    }

    /// <summary>Erases an outline from the Setup page, as the user does.</summary>
    private static void Erase(RefreshHarness h, string objectId) => h.App.DeletePageContent(h.Notebook.PageIds[PageKey.Setup], objectId, DateTime.MinValue);

    private static string HabitLegend(RefreshHarness h)
    {
        string id = h.SavedSidecar().Pages[January.Value].Anchors["habitLegend"];
        return h.Notebook.Read(January).Outlines.Single(o => o.ObjectId == id).PlainText;
    }

    /// <summary>Cancels the run when it reports writing its n-th page.</summary>
    private sealed class CancelOnPage : IProgress<string>
    {
        private readonly int page;
        private readonly CancellationTokenSource cancel;
        private int pages;

        public CancelOnPage(int page, CancellationTokenSource cancel)
        {
            this.page = page;
            this.cancel = cancel;
        }

        public void Report(string value)
        {
            if (value.StartsWith("Writing ", StringComparison.Ordinal) && value != "Writing changes" && ++pages == page)
            {
                cancel.Cancel();
            }
        }
    }
}
