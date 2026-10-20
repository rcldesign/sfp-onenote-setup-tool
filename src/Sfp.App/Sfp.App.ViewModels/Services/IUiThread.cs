namespace Sfp.App.ViewModels.Services;

/// <summary>Marshals work onto the UI thread. The WPF implementation posts to the dispatcher; tests run the action inline.</summary>
public interface IUiThread
{
    void Post(Action action);
}

/// <summary>An <see cref="IProgress{T}"/> whose reports always arrive on the UI thread, whatever thread reports them.</summary>
public sealed class UiProgress<T> : IProgress<T>
{
    private readonly IUiThread _ui;
    private readonly Action<T> _handler;

    public UiProgress(IUiThread ui, Action<T> handler)
    {
        _ui = ui;
        _handler = handler;
    }

    public void Report(T value) => _ui.Post(() => _handler(value));
}
