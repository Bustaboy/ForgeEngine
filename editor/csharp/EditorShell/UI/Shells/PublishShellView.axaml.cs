using Avalonia.Controls;

namespace Soul.Editor.EditorShell.UI.Shells;

public partial class PublishShellView : UserControl
{
    public PublishShellView()
    {
        InitializeComponent();
    }

    public ContentControl PublishHost => PublishPanelHost;
}
