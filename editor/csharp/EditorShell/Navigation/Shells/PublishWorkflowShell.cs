using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class PublishWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.Publish;

    public string Title => "Publish";

    public string Subtitle => "Check readiness and run local release packaging.";

    public string PrimaryCtaLabel => "Check Publish Readiness";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasOpenProject;

    public Control CreateView() => new PublishShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
