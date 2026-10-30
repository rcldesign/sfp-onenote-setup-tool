using System.Globalization;
using Serilog.Events;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Writer;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Fix round 5f: on the owner's PC the type library registration is complete and OneNote activates, yet the first call
/// still fails with E_FAIL, because the tool ran as administrator (from an elevated shell) while OneNote runs as the
/// normal user. Both sides are read the same way, from each process token's TokenElevation flag, and every OneNote
/// process is compared with the tool: FromProcesses decides which processes run at the other elevation, Problem words
/// the result, and Read and the production locator are checked where they run.
/// </summary>
public class ProcessElevationTests
{
    private const string ToolElevatedProblem =
        "This tool is running as administrator but OneNote is not (OneNote process 1234). OneNote refuses automation across that boundary and answers E_FAIL. Close this window and run the tool from a normal, non-elevated prompt or shortcut, then retry.";

    private const string OneNoteElevatedProblem =
        "OneNote is running as administrator (OneNote process 1234) but this tool is not. OneNote refuses automation across that boundary and answers E_FAIL. Close OneNote completely (also its icon in the notification area) and open it normally, not as administrator, then retry.";

    [Fact]
    public void Unknown_knows_neither_side_and_is_no_problem()
    {
        ElevationState.Unknown.Should().Be(new ElevationState(null, null, new List<int>(), 0), "the process IDs are compared by value");
        ProcessElevation.Problem(ElevationState.Unknown).Should().BeNull();
    }

    [Fact]
    public void States_are_equal_when_their_values_and_their_process_ids_in_order_are()
    {
        ElevationState state = new(false, true, new[] { 1234, 5678 }, 1);
        ElevationState same = new(false, true, new List<int> { 1234, 5678 }, 1);

        state.Should().Be(same);
        state.GetHashCode().Should().Be(same.GetHashCode());
        state.Should().NotBe(new ElevationState(false, true, new[] { 5678, 1234 }, 1), "the order of the IDs counts");
        state.Should().NotBe(new ElevationState(false, true, new[] { 1234 }, 1));
        state.Should().NotBe(new ElevationState(false, true, new[] { 1234, 5678 }, 0));
        state.Should().NotBe(new ElevationState(true, true, new[] { 1234, 5678 }, 1));
        state.Should().NotBe(new ElevationState(false, false, new[] { 1234, 5678 }, 1));
    }

    /// <summary>
    /// Nothing is reported without a OneNote process at the other elevation: none running (COM would start OneNote at
    /// the tool's own elevation), all at the tool's elevation, or none whose token could be read, which are only counted.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, true)]
    [InlineData(null, false)]
    public void There_is_no_problem_without_a_onenote_process_at_the_other_elevation(bool? tool, bool? oneNote)
    {
        ProcessElevation.Problem(new ElevationState(tool, oneNote, Array.Empty<int>(), 0)).Should().BeNull();
        ProcessElevation.Problem(new ElevationState(tool, oneNote, Array.Empty<int>(), 2)).Should().BeNull("an unreadable process is left out");
    }

    [Fact]
    public void Process_ids_without_the_tools_elevation_are_no_problem()
    {
        ProcessElevation.Problem(new ElevationState(null, true, new[] { 1234 }, 0)).Should().BeNull("which side to restart follows from the tool's elevation");
    }

    /// <summary>The owner's case: the tool runs as administrator, OneNote as the normal user.</summary>
    [Fact]
    public void A_tool_running_as_administrator_beside_a_normal_onenote_is_told_to_run_from_a_normal_prompt()
    {
        ProcessElevation.Problem(new ElevationState(true, false, new[] { 1234 }, 0)).Should().Be(ToolElevatedProblem);
    }

    [Fact]
    public void A_onenote_running_as_administrator_beside_a_normal_tool_is_told_to_reopen_onenote_normally()
    {
        ProcessElevation.Problem(new ElevationState(false, true, new[] { 1234 }, 0)).Should().Be(OneNoteElevatedProblem);
    }

    [Fact]
    public void Several_processes_at_the_other_elevation_are_named_together_as_processes()
    {
        ProcessElevation.Problem(new ElevationState(true, false, new[] { 1234, 5678 }, 0)).Should().Be(
            "This tool is running as administrator but OneNote is not (OneNote processes 1234, 5678). OneNote refuses automation across that boundary and answers E_FAIL. Close this window and run the tool from a normal, non-elevated prompt or shortcut, then retry.");
        ProcessElevation.Problem(new ElevationState(false, true, new[] { 1234, 5678, 9012 }, 1)).Should().Be(
            "OneNote is running as administrator (OneNote processes 1234, 5678, 9012) but this tool is not. OneNote refuses automation across that boundary and answers E_FAIL. Close OneNote completely (also its icon in the notification area) and open it normally, not as administrator, then retry.");
    }

    [Fact]
    public void A_null_state_is_refused()
    {
        Action noState = () => ProcessElevation.Problem(null!);

        noState.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Every OneNote process is compared with the tool, not the first one listed: those at the tool's elevation are left
    /// out, those at the other elevation are named in ascending order, and the unreadable ones are only counted.
    /// </summary>
    [Fact]
    public void Every_onenote_process_is_compared_and_those_at_the_other_elevation_are_named_in_order()
    {
        ElevationState normalTool = ProcessElevation.FromProcesses(false, new[] { (5678, true), (1111, false), (1234, true) }, 1);
        ElevationState elevatedTool = ProcessElevation.FromProcesses(true, new[] { (1111, false), (2222, true) }, 0);

        normalTool.Should().Be(new ElevationState(false, true, new[] { 1234, 5678 }, 1));
        ProcessElevation.Problem(normalTool).Should().Be(
            "OneNote is running as administrator (OneNote processes 1234, 5678) but this tool is not. OneNote refuses automation across that boundary and answers E_FAIL. Close OneNote completely (also its icon in the notification area) and open it normally, not as administrator, then retry.");
        elevatedTool.Should().Be(new ElevationState(true, false, new[] { 1111 }, 0), "the elevated leftover beside the user's OneNote matches the tool");
        ProcessElevation.Problem(elevatedTool).Should().Be(ToolElevatedProblem.Replace("process 1234", "process 1111", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without a process at the other elevation there are no IDs, and OneNote's elevation is the one every readable
    /// process shares (the tool's own when it is known), or unknown when none was read or they differ among themselves.
    /// </summary>
    [Fact]
    public void Without_a_process_at_the_other_elevation_onenotes_elevation_is_the_one_the_readable_processes_share()
    {
        ProcessElevation.FromProcesses(false, new[] { (1111, false) }, 0).Should().Be(new ElevationState(false, false, Array.Empty<int>(), 0));
        ProcessElevation.FromProcesses(true, new[] { (1111, true), (2222, true) }, 0).Should().Be(new ElevationState(true, true, Array.Empty<int>(), 0));
        ProcessElevation.FromProcesses(false, Array.Empty<(int, bool)>(), 0).Should().Be(new ElevationState(false, null, Array.Empty<int>(), 0), "no OneNote is running");
        ProcessElevation.FromProcesses(false, Array.Empty<(int, bool)>(), 2).Should().Be(new ElevationState(false, null, Array.Empty<int>(), 2), "no OneNote process could be read");
        ProcessElevation.FromProcesses(true, new[] { (1111, true) }, 1).Should().Be(new ElevationState(true, true, Array.Empty<int>(), 1), "the unreadable one is left out");
        ProcessElevation.FromProcesses(null, new[] { (1111, true), (2222, true) }, 0).Should().Be(new ElevationState(null, true, Array.Empty<int>(), 0));
        ProcessElevation.FromProcesses(null, new[] { (1111, true), (2222, false) }, 0).Should().Be(new ElevationState(null, null, Array.Empty<int>(), 0), "nothing is compared without the tool's elevation");
    }

    [SkippableFact]
    public void Read_returns_both_sides_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Elevation is read from Windows access tokens.");

        // Skip.IfNot is not a guard the platform analyzer knows, so the check is repeated for it.
        if (OperatingSystem.IsWindows())
        {
            ElevationState state = ProcessElevation.Read();

            state.ToolElevated.Should().NotBeNull("this process can always read its own token");
            state.OneNoteUnreadableCount.Should().BeGreaterThanOrEqualTo(0);
            state.OneNotePids.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            if (state.OneNotePids.Count > 0)
            {
                state.OneNoteElevated.Should().Be(!state.ToolElevated, "the processes named run at the other elevation");
            }
        }
    }

    /// <summary>
    /// On Windows the locator logs one Information line with both elevations, each as "true", "false" or "unknown", the
    /// IDs of the OneNote processes at the other elevation or "none", and how many could not be read, all as strings, and
    /// answers the problem of the state that line shows. Off Windows it answers null and logs nothing.
    /// </summary>
    [Fact]
    public void The_production_locator_logs_the_elevations_on_windows_and_reports_nothing_elsewhere()
    {
        CapturingSink log = new();
        using OneNoteLocator locator = new(log.CreateLogger());

        string? problem = locator.ElevationProblem;

        if (OperatingSystem.IsWindows())
        {
            LogEvent line = log.Events.Should().ContainSingle().Subject;
            line.Level.Should().Be(LogEventLevel.Information);
            line.MessageTemplate.Text.Should().Be("Elevation: tool = {ToolElevated}, OneNote = {OneNoteElevated} (differing processes {OneNotePids}, unreadable {OneNoteUnreadableCount})");
            string tool = Scalar(line, "ToolElevated");
            string oneNote = Scalar(line, "OneNoteElevated");
            string pids = Scalar(line, "OneNotePids");
            string unreadable = Scalar(line, "OneNoteUnreadableCount");
            tool.Should().BeOneOf("true", "false");
            oneNote.Should().BeOneOf("true", "false", "unknown");

            int[] differing = pids == "none" ? Array.Empty<int>() : pids.Split(", ").Select(Number).ToArray();
            ElevationState shown = new(Side(tool), Side(oneNote), differing, Number(unreadable));
            problem.Should().Be(ProcessElevation.Problem(shown), "the answer is the problem of the state the line shows");
        }
        else
        {
            problem.Should().BeNull("off Windows there is no access token to read and no OneNote to start");
            log.Events.Should().BeEmpty();
        }
    }

    /// <summary>The string a log property holds; each value goes in as a string, so the run log shows it without quotes.</summary>
    private static string Scalar(LogEvent line, string name) =>
        line.Properties[name].Should().BeOfType<ScalarValue>().Which.Value.Should().BeOfType<string>().Which;

    private static int Number(string logged) => int.Parse(logged, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool? Side(string logged) => logged switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}
