using System.Diagnostics;
using System.Globalization;
using Sfp.Cli;
using Sfp.Cli.Logging;
using Sfp.TestSupport;
using Xunit.Abstractions;

namespace Sfp.Render.Tests;

/// <summary>
/// The PRD's memory budget (under 1.5 GB peak; pages rendered, written and released one at a time): sfp2onenote
/// render-only renders the whole Q1 2027 quarter in its own process, on the real planner when it is present (else the
/// synthetic template), while the test polls that process's peak working set. Before the wave 3 fix the PNG encoder's
/// SKBitmap.Copy left a full-size pixel snapshot per page to the finalizer: the run peaked at 2304 MB at 220 DPI and
/// 2936 MB at 300 DPI on the real planner (4256 MB at 220 DPI on the synthetic template). The page cache sized in pages
/// (713 MB at 300 DPI) is the other half of the budget.
/// </summary>
public sealed class MemoryBudgetTests
{
    /// <summary>The PRD's peak memory budget, 1.5 GB.</summary>
    public const long BudgetMegabytes = 1536;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private readonly ITestOutputHelper output;

    public MemoryBudgetTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [SkippableTheory]
    [InlineData(220)]
    [InlineData(300)]
    public void A_full_render_only_run_peaks_under_the_PRD_memory_budget(int dpi)
    {
        string cli = Path.Combine(AppContext.BaseDirectory, "sfp2onenote.dll");
        Skip.IfNot(File.Exists(cli), "The CLI assembly is not beside the tests: " + cli);
        string pdf = SfpPdf.Path ?? SyntheticTemplate.Build();
        string dir = TestPaths.NewTempDir("memory-budget");
        try
        {
            string outDir = Path.Combine(dir, "out");
            ProcessStartInfo info = new(DotnetHost())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[] { cli, "render-only", "--pdf", pdf, "--start", "2027-01", "--out", outDir, "--dpi", dpi.ToString(CultureInfo.InvariantCulture), "--templates", TestPaths.TemplatesDir, "--quiet" })
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment[RunLog.LogDirEnvVar] = Path.Combine(dir, "logs");

            (int exitCode, long peakBytes, string stderr, TimeSpan elapsed) = RunAndPoll(info);
            long peakMb = peakBytes / (1024 * 1024);
            string what = string.Format(CultureInfo.InvariantCulture, "render-only of Q1 2027 at {0} DPI from {1}: peak {2} MB in {3:0.0} s", dpi, Path.GetFileName(pdf), peakMb, elapsed.TotalSeconds);
            output.WriteLine(what);

            exitCode.Should().Be(ExitCodes.Success, "the run must finish; stderr: " + stderr);
            Directory.GetFiles(outDir, "*.png").Should().HaveCount(142);
            peakBytes.Should().BePositive("the peak working set was read while the process ran");
            peakMb.Should().BeLessThan(BudgetMegabytes, what);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Starts the process, polls its peak working set every 100 ms until it exits, and returns the exit code and the largest peak read.</summary>
    private static (int ExitCode, long PeakBytes, string Stderr, TimeSpan Elapsed) RunAndPoll(ProcessStartInfo info)
    {
        Stopwatch clock = Stopwatch.StartNew();
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + info.FileName);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        long peak = 0;
        while (!process.WaitForExit(100))
        {
            peak = Math.Max(peak, PeakWorkingSet(process));
            if (clock.Elapsed > Timeout)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("render-only did not finish within " + Timeout);
            }
        }

        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, peak, stderr.Result, clock.Elapsed);
    }

    /// <summary>The process's peak resident set so far (VmHWM on Linux), or 0 once it has exited.</summary>
    private static long PeakWorkingSet(Process process)
    {
        try
        {
            process.Refresh();
            return process.PeakWorkingSet64;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>The dotnet host running these tests (DOTNET_HOST_PATH as the SDK sets it, else this process when it is the host, else PATH).</summary>
    private static string DotnetHost()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(host) && File.Exists(host))
        {
            return host;
        }

        string? self = Environment.ProcessPath;
        string name = Path.GetFileNameWithoutExtension(self ?? "");
        return self is not null && string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase) ? self : "dotnet";
    }
}
