using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Soul.Editor.EditorShell.UI;

/// <summary>
/// Blocking overlay for first launch: model readiness messaging and indeterminate progress.
/// </summary>
public partial class StartupOnboardingOverlay : UserControl
{
    public StartupOnboardingOverlay()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
