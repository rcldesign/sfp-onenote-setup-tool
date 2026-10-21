using Sfp.Core.Manifests;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.Refresh.Calendar;
using Sfp.Refresh.Orchestration;
using Sfp.Refresh.Setup;
using Sfp.Refresh.Tests.Support;

namespace Sfp.Refresh.Tests.Orchestration;

/// <summary>
/// Round 4 refresh-5: a Setup value is normalized like an event title, so a character XML 1.0 does not allow (a C0
/// control from a numeric character reference, say) becomes a space and never reaches the writer. A run in which page
/// writes were attempted and every one was refused as unwritable fails with exit 5 and counts toward the failure streak,
/// as a run whose every write conflicted does; a run that wrote any page still succeeds with the per-page warnings.
/// </summary>
public class UnwritableSetupTextTests
{
    private static readonly SetupPageSpec S = RefreshManifest.Shipped.Generated.Setup;
    private static readonly PageKey MonJan4 = PageKey.Day(RefreshHarness.Jan4);

    [Fact]
    public void Normalize_turns_the_characters_a_page_cannot_carry_into_spaces_as_event_titles_do()
    {
        SetupReader.Normalize("Walk\u000130 min").Should().Be("Walk 30 min");
        SetupReader.Normalize("Walk \u0008 30￾min￿").Should().Be("Walk 30 min");
        SetupReader.Normalize("a\uD800b").Should().Be("a b", "a lone surrogate becomes a space too");
        SetupReader.Normalize("\u0001\u0002").Should().BeNull("nothing is left");
        SetupReader.Normalize("Run 5 km \U0001F3C3").Should().Be("Run 5 km \U0001F3C3", "a surrogate pair is kept");
    }

    [Fact]
    public void A_setup_value_holding_a_control_character_reference_is_written_with_a_space_in_its_place()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Type(S.StandingGoals[0], "Walk&#1;30 min");

        RefreshResult result = h.Run();

        result.ExitCode.Should().Be(0, result.Error);
        RefreshSummary summary = result.Summary!;
        summary.Values.StandingGoals[0].Should().Be("Walk 30 min");
        summary.Changes.Should().NotBeEmpty().And.OnlyContain(c => !c.Blocked, "no page is refused");
        summary.Warnings.Should().NotContain(w => w.Contains(" was not written: ", StringComparison.Ordinal));
        h.Notebook.Read(MonJan4).Outlines.Select(o => o.PlainText).Should().Contain(new[] { "O1 Ship the connector", "Walk 30 min" });
        h.Run().Summary!.Changes.Should().BeEmpty("the next run finds every page current");
    }

    [Fact]
    public void A_run_whose_every_page_write_is_refused_as_unwritable_fails_with_exit_5_and_counts_toward_the_streak()
    {
        RefreshHarness h = new();
        h.Type(S.Objectives[0], "Ship the connector");
        h.Type(S.StandingGoals[0], "Walk 30 min");
        PropagatorFactory real = h.Services.Propagators;
        h.Services = h.Services with { Propagators = (catalog, reader) => new EveryTextUnwritable(real(catalog, reader)) };
        int updates = h.App.CallCount("UpdatePageContent");

        for (int day = 0; day < 3; day++)
        {
            RefreshResult result = h.Run(today: RefreshHarness.Jan4.AddDays(day));

            RefreshSummary summary = result.Summary!;
            int pages = summary.Warnings.Count(w => w.Contains(" was not written: ", StringComparison.Ordinal));
            pages.Should().BeGreaterThan(0);
            result.ExitCode.Should().Be(RefreshOrchestrator.WriteFailureExitCode);
            result.Success.Should().BeFalse();
            result.Error.Should().Contain($"({pages} page(s))").And.Contain("cannot carry");
            summary.ValuesWritten.Should().Be(0);
            FailureCounter.LoadBeside(h.SidecarPath).ConsecutiveTotalFailures.Should().Be(day + 1, "a run that could write no page counts toward the streak");
        }

        h.App.CallCount("UpdatePageContent").Should().Be(updates, "no page was written");
        h.Notifier.Notifications.Should().ContainSingle().Which.Message.Should().Contain("failed 3 runs in a row").And.Contain("cannot carry");
    }

    /// <summary>Plans with the real propagator, then puts a U+0001 in every text to write, as if no normalization had seen any value.</summary>
    private sealed class EveryTextUnwritable : IPropagator
    {
        private readonly IPropagator inner;

        public EveryTextUnwritable(IPropagator inner)
        {
            this.inner = inner;
        }

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s) => Spoil(inner.Plan(plan, v, s));

        public IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s, bool writeInkedValues) => Spoil(inner.Plan(plan, v, s, writeInkedValues));

        private static IReadOnlyList<PlannedChange> Spoil(IReadOnlyList<PlannedChange> changes) =>
            changes.Select(c => c.Kind == ChangeKind.Remove ? c : c with { NewText = c.NewText + "\u0001" }).ToList();
    }
}
