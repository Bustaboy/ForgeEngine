using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class ProjectHomeWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.ProjectHome;

    public string Title => "Project Home";

    public string Subtitle => "Create, open, and track your game projects.";

    public string PrimaryCtaLabel => "Create New Game";

    public bool IsAvailable(WorkflowProjectContext context) => true;

    public Control CreateView() => new ProjectHomeShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
