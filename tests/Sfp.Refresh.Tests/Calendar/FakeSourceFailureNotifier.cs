using Sfp.Refresh.Calendar;

namespace Sfp.Refresh.Tests.Calendar;

/// <summary>Records every notification instead of showing it.</summary>
public sealed class FakeSourceFailureNotifier : ISourceFailureNotifier
{
    public List<(string Title, string Message)> Notifications { get; } = new();

    public void Notify(string title, string message) => Notifications.Add((title, message));
}

/// <summary>A source that returns fixed events, or throws when built with an exception.</summary>
public sealed class StubCalendarSource : ICalendarSource
{
    private readonly IReadOnlyList<CalendarEvent> events;
    private readonly Exception? failure;

    public StubCalendarSource(params CalendarEvent[] events)
    {
        this.events = events;
    }

    public StubCalendarSource(Exception failure)
    {
        events = Array.Empty<CalendarEvent>();
        this.failure = failure;
    }

    public int FetchCalls { get; private set; }

    public IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to)
    {
        FetchCalls++;
        if (failure is not null)
        {
            throw failure;
        }

        return events;
    }
}
