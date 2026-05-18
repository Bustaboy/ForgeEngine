using Avalonia.Controls;

namespace Soul.Editor.EditorShell.UI.Shells;

public partial class AiInterviewShellView : UserControl
{
    public AiInterviewShellView()
    {
        InitializeComponent();
    }

    public ContentControl InterviewHost => InterviewPanelHost;
}
