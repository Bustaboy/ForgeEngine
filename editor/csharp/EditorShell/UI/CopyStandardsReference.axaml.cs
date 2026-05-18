using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Soul.Editor.EditorShell.UI;

public partial class CopyStandardsReference : UserControl
{
    public CopyStandardsReference()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
