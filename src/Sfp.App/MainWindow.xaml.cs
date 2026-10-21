using System.ComponentModel;
using Sfp.App.ViewModels;

namespace Sfp.App;

/// <summary>Single window with a Generate tab and a Refresh tab. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : System.Windows.Window
{
    public const string TitleText = MainViewModel.WindowTitle;

    private readonly MainViewModel _viewModel;
    private Task<bool>? _pendingClose;
    private bool _readyToClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// The window closes only when the view model is ready (<see cref="MainViewModel.PrepareToCloseAsync"/>): at once
    /// when nothing runs; during a Generate or Refresh run only after the user agreed to cancel it and the run has
    /// stopped, so the Generate orchestrator has deleted the partial notebook and the Refresh orchestrator has saved what
    /// it wrote; and never while a preview render still uses the renderer that App.OnExit disposes.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose)
        {
            return;
        }

        if (_pendingClose is { IsCompleted: false })
        {
            // A second click while the run is stopping: the first close is already waiting for it.
            e.Cancel = true;
            return;
        }

        Task<bool> prepare = _viewModel.PrepareToCloseAsync();
        if (prepare.IsCompleted)
        {
            // Nothing to wait for: close now, or stay open because the user chose to keep the run going.
            _readyToClose = CloseAllowed(prepare);
            e.Cancel = !_readyToClose;
            return;
        }

        // The cancelled run or a preview render is still stopping: keep the window until it has, then close again.
        e.Cancel = true;
        _pendingClose = prepare;
        _ = CloseWhenReadyAsync(prepare);
    }

    private async Task CloseWhenReadyAsync(Task<bool> prepare)
    {
        // WhenAny never throws: a failed preparation is reported by CloseAllowed instead of escaping this handler.
        await Task.WhenAny(prepare);
        if (CloseAllowed(prepare))
        {
            _readyToClose = true;
            Close();
        }
    }

    /// <summary>The completed preparation's answer; a failed one allows the close after a message saying what went wrong.</summary>
    private bool CloseAllowed(Task<bool> prepare)
    {
        bool allowed = MainViewModel.MayClose(prepare, out string? notice);
        if (notice is not null)
        {
            System.Windows.MessageBox.Show(this, notice, TitleText, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        return allowed;
    }
}
