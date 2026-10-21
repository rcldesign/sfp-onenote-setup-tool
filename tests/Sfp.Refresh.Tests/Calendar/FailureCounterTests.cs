using Sfp.Refresh.Calendar;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Calendar;

public sealed class FailureCounterTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2027, 1, 4, 5, 0, 0, TimeSpan.FromHours(-5));
    private readonly string dir = TestPaths.NewTempDir("failure-counter");

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Each_notebook_has_its_own_counter_beside_its_sidecar()
    {
        string q1 = Path.Combine(dir, "SFP Q1 2027.sfp-state.json");
        string q2 = Path.Combine(dir, "SFP Q2 2027.sfp-state.json");

        FailureCounter.PathBeside(q1).Should().Be(Path.Combine(dir, "SFP Q1 2027.sfp-refresh-failures.json"));
        FailureCounter.PathBeside(q2).Should().Be(Path.Combine(dir, "SFP Q2 2027.sfp-refresh-failures.json"));
        FailureCounter.PathBeside(Path.Combine(dir, "elsewhere.json")).Should().Be(Path.Combine(dir, "elsewhere.sfp-refresh-failures.json"));
        FailureCounter first = FailureCounter.LoadBeside(q1);
        first.RecordTotalFailure(T0, "Work: timeout");
        first.Save();

        FailureCounter.LoadBeside(q2).ConsecutiveTotalFailures.Should().Be(0, "two notebooks in one folder never share a streak");
        FailureCounter.LoadBeside(q1).ConsecutiveTotalFailures.Should().Be(1);
    }

    [Fact]
    public void Save_replaces_the_counter_through_a_temp_file_and_leaves_none_behind()
    {
        string path = Path.Combine(dir, "SFP Q1 2027." + FailureCounter.FileName);
        File.WriteAllText(path, "{ \"consecutiveTotalFailures\": 7, \"lastMessage\": \"a long message from an earlier run\" }");
        FailureCounter counter = FailureCounter.Load(path);
        counter.Reset(T0);
        FileStream? reader = OperatingSystem.IsWindows() ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        try
        {
            counter.Save();
            if (reader is not null)
            {
                new StreamReader(reader).ReadToEnd().Should().Contain("\"consecutiveTotalFailures\": 7", "the file is replaced by a new one, never rewritten in place under a reader");
            }
        }
        finally
        {
            reader?.Dispose();
        }

        Directory.GetFiles(dir).Should().Equal(path);
        FailureCounter.Load(path).ConsecutiveTotalFailures.Should().Be(0);
        File.ReadAllText(path).Should().NotContain("earlier run");
    }

    [Fact]
    public void A_missing_file_starts_at_zero()
    {
        FailureCounter counter = FailureCounter.Load(Path.Combine(dir, FailureCounter.FileName));

        counter.ConsecutiveTotalFailures.Should().Be(0);
        counter.ShouldToast.Should().BeFalse();
        counter.LastTotalFailure.Should().BeNull();
    }

    [Fact]
    public void Three_consecutive_total_failures_set_ShouldToast()
    {
        FailureCounter counter = FailureCounter.Load(Path.Combine(dir, FailureCounter.FileName));

        counter.RecordTotalFailure(T0, "Work: timeout");
        counter.RecordTotalFailure(T0.AddDays(1), "Work: timeout");
        counter.ShouldToast.Should().BeFalse("two failures are not yet three");
        counter.RecordTotalFailure(T0.AddDays(2), "Work: 503");

        counter.ConsecutiveTotalFailures.Should().Be(3);
        counter.ShouldToast.Should().BeTrue();
        counter.LastTotalFailure.Should().Be(T0.AddDays(2));
        counter.LastMessage.Should().Be("Work: 503");
    }

    [Fact]
    public void A_success_resets_the_streak()
    {
        FailureCounter counter = FailureCounter.Load(Path.Combine(dir, FailureCounter.FileName));
        counter.RecordTotalFailure(T0);
        counter.RecordTotalFailure(T0.AddDays(1));
        counter.RecordTotalFailure(T0.AddDays(2));

        counter.Reset(T0.AddDays(3));

        counter.ConsecutiveTotalFailures.Should().Be(0);
        counter.ShouldToast.Should().BeFalse();
        counter.LastSuccess.Should().Be(T0.AddDays(3));
        counter.LastTotalFailure.Should().Be(T0.AddDays(2), "history is kept");
    }

    [Fact]
    public void The_count_survives_a_save_and_reload()
    {
        string path = Path.Combine(dir, "nested", FailureCounter.FileName);
        FailureCounter counter = FailureCounter.Load(path);
        counter.RecordTotalFailure(T0, "Work: timeout");
        counter.RecordTotalFailure(T0.AddDays(1), "Work: timeout");
        counter.Save();

        FailureCounter reloaded = FailureCounter.Load(path);

        File.Exists(path).Should().BeTrue();
        reloaded.ConsecutiveTotalFailures.Should().Be(2);
        reloaded.LastTotalFailure.Should().Be(T0.AddDays(1));
        reloaded.LastMessage.Should().Be("Work: timeout");
        File.ReadAllText(path).Should().Contain("\"consecutiveTotalFailures\": 2", "the file is camelCase JSON");
    }

    [Fact]
    public void A_corrupt_file_is_treated_as_zero_rather_than_blocking_the_run()
    {
        string path = Path.Combine(dir, FailureCounter.FileName);
        File.WriteAllText(path, "{ not json");

        FailureCounter counter = FailureCounter.Load(path);

        counter.ConsecutiveTotalFailures.Should().Be(0);
    }
}
