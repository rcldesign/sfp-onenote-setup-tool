using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.Render;

namespace Sfp.App.ViewModels.Services;

/// <summary>
/// Stand-ins for the services other cards implement. The composition root uses one until the integrator wires the real
/// class; each fails with a message naming the missing card so the UI shows it as an error line instead of crashing.
/// </summary>
public static class NotAvailable
{
    /// <summary>Exit code for a run that could not start because its module is not wired (the PRD's write-error code).</summary>
    public const int ExitCode = 5;

    public static string MessageFor(string module, string card) => $"{module} ({card}) is not wired into the app yet.";
}

/// <summary>Fails every Generate run with the not-wired message.</summary>
public sealed class NotAvailableGenerateOrchestrator : IGenerateOrchestrator
{
    public static string Message { get; } = NotAvailable.MessageFor("The Generate orchestrator", "M3.9");

    public GenerateResult Run(GenerateRequest request, IProgress<GenerateProgress>? progress, CancellationToken cancellationToken) => new()
    {
        Success = false,
        ExitCode = NotAvailable.ExitCode,
        Phase = GeneratePhase.Failed,
        Error = Message,
    };
}

/// <summary>Fails every Refresh run with the not-wired message.</summary>
public sealed class NotAvailableRefreshOrchestrator : IRefreshOrchestrator
{
    public static string Message { get; } = NotAvailable.MessageFor("The Refresh orchestrator", "M4.7");

    public RefreshResult Run(RefreshRequest request, IProgress<string>? progress, CancellationToken cancellationToken) => new()
    {
        Success = false,
        ExitCode = NotAvailable.ExitCode,
        Error = Message,
    };
}

/// <summary>Throws <see cref="NotSupportedException"/> on every listing; the Refresh tab shows the message.</summary>
public sealed class NotAvailableNotebookRegistry : INotebookRegistry
{
    public static string Message { get; } = NotAvailable.MessageFor("The notebook registry", "M4.3");

    public IReadOnlyList<KnownNotebook> List(IEnumerable<string> folders) => throw new NotSupportedException(Message);
}

/// <summary>Reports no task registered and refuses to register one.</summary>
public sealed class NotAvailableScheduledTaskService : IScheduledTaskService
{
    public static string Message { get; } = NotAvailable.MessageFor("The scheduled task service", "M5.4");

    public bool IsRegistered(string notebookName) => false;

    public void Register(string notebookName, string executablePath, int horizonDays) => throw new NotSupportedException(Message);

    public void Remove(string notebookName) => throw new NotSupportedException(Message);
}

/// <summary>Throws on every build; the preview strip shows the message.</summary>
public sealed class NotAvailablePlanBuilder : IPlanBuilder
{
    public static string Message { get; } = NotAvailable.MessageFor("The plan builder", "M1.6");

    public NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o) => throw new NotSupportedException(Message);
}

/// <summary>Throws on every render; the preview strip shows the message.</summary>
public sealed class NotAvailablePageRenderer : IPageRenderer
{
    public static string Message { get; } = NotAvailable.MessageFor("The page renderer", "M2.9");

    public RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o) => throw new NotSupportedException(Message);
}
