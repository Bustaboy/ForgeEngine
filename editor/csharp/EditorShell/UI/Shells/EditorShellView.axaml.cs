using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Soul.Editor.EditorShell.UI.Shells;

public partial class EditorShellView : UserControl
{
    public EditorShellView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public ContentControl WorkspaceHost => this.FindControl<ContentControl>("EditorWorkspaceHost")!;
}
