using System.Windows.Threading;
using Sfp.App.ViewModels.Services;

namespace Sfp.App.Composition;

/// <summary>Posts to the WPF dispatcher.</summary>
public sealed class DispatcherUiThread : IUiThread
{
    private readonly Dispatcher _dispatcher;

    public DispatcherUiThread(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}
