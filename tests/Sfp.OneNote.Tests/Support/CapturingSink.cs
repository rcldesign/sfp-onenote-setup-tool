using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Sfp.OneNote.Tests.Support;

/// <summary>Collects every Serilog event so tests can inspect what the COM wrapper logged.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = new();
    private readonly object _gate = new();

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToList();
            }
        }
    }

    /// <summary>Rendered message text of every event, in order.</summary>
    public IReadOnlyList<string> Messages => Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)).ToList();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>A verbose logger writing only to this sink.</summary>
    public ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
}
