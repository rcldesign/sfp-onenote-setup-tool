using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Sfp.Refresh.Tests.Support;

/// <summary>A Serilog logger that keeps every event so tests can assert on warnings.</summary>
public sealed class CapturingLog : ILogEventSink
{
    private readonly List<LogEvent> events = new();

    public CapturingLog()
    {
        Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(this).CreateLogger();
    }

    public ILogger Logger { get; }

    public IReadOnlyList<LogEvent> Events => events;

    public IEnumerable<string> Messages(LogEventLevel level) =>
        events.Where(e => e.Level == level).Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));

    public void Emit(LogEvent logEvent) => events.Add(logEvent);
}
