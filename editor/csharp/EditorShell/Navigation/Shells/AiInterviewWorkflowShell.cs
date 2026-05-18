using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class AiInterviewWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.AiInterview;

    public string Title => "AI Interview";

    public string Subtitle => "Shape your game through guided planning chat.";

    public string PrimaryCtaLabel => "Start Planning Chat";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasOpenProject;

    public Control CreateView() => new AiInterviewShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
