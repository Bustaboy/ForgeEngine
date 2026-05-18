using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class EditorWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.Editor;

    public string Title => "Editor";

    public string Subtitle => "Viewport-first editing with simple inspector defaults.";

    public string PrimaryCtaLabel => "Lock Selection";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasOpenProject;

    public Control CreateView() => new EditorShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
