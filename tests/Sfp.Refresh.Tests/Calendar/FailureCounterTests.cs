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
    public void Counter_lives_beside_the_sidecar()
    {
        string sidecar = Path.Combine(dir, "SFP Q1 2027", "sfp-state.json");

        FailureCounter.PathBeside(sidecar).Should().Be(Path.Combine(dir, "SFP Q1 2027", "sfp-refresh-failures.json"));
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
