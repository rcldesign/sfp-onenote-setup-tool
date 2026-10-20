using System.Diagnostics;

namespace Sfp.Refresh.Scheduler;

/// <summary>What a process run returned.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs a command line tool to completion. One production implementation (<see cref="ProcessRunner"/>) and a fake in the tests that records the exact arguments.</summary>
public interface IProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> with the arguments (each passed as its own argument, quoting handled by the runtime) and waits for it to exit.</summary>
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}

/// <summary>Production runner over <see cref="Process"/>, no window, output captured, bounded by a timeout.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly TimeSpan timeout;

    public ProcessRunner(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + fileName + ".");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill();
            throw new TimeoutException(fileName + " did not finish within " + timeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s.");
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
