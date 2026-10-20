namespace Sfp.Refresh.Calendar;

/// <summary>
/// Raises the user-facing notice the PRD asks for after three consecutive runs in which every calendar source failed.
/// One production implementation (<see cref="WindowsToastNotifier"/>) and a fake in the tests.
/// </summary>
public interface ISourceFailureNotifier
{
    /// <summary>Shows a notification. Must not throw: a failed notification is logged, never fatal to the run.</summary>
    void Notify(string title, string message);
}
