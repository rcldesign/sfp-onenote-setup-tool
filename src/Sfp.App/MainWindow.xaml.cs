using Sfp.App.ViewModels;

namespace Sfp.App;

/// <summary>Single window with a Generate tab and a Refresh tab. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : System.Windows.Window
{
    public const string TitleText = MainViewModel.WindowTitle;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
