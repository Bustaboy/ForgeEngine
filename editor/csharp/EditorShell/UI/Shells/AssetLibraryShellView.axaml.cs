using Avalonia.Controls;

namespace Soul.Editor.EditorShell.UI.Shells;

public partial class AssetLibraryShellView : UserControl
{
    public AssetLibraryShellView()
    {
        InitializeComponent();
    }

    public ContentControl AssetHost => AssetLibraryHost;
}
