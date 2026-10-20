using Sfp.App.ViewModels.Generate;

namespace Sfp.App.Views;

/// <summary>Progress bar, Cancel, and the success, cancelled and failure views. Bound to GenerateRunViewModel.</summary>
public partial class GenerateRunView : System.Windows.Controls.UserControl
{
    /// <summary>The OneDrive move paragraph, exposed for the XAML static binding.</summary>
    public const string OneDriveInstruction = GenerateRunViewModel.OneDriveInstruction;

    public GenerateRunView()
    {
        InitializeComponent();
    }
}
