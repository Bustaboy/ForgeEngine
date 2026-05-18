using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class PrototypeWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.Prototype;

    public string Title => "Prototype";

    public string Subtitle => "Generate and refine your first playable build.";

    public string PrimaryCtaLabel => "Generate Playable Prototype";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasOpenProject;

    public Control CreateView() => new PrototypeShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
