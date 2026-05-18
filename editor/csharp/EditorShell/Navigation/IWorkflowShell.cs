using Avalonia.Controls;

namespace Soul.Editor.EditorShell.Navigation;

public interface IWorkflowShell
{
    WorkflowSection Section { get; }

    string Title { get; }

    string Subtitle { get; }

    string PrimaryCtaLabel { get; }

    bool IsAvailable(WorkflowProjectContext context);

    Control CreateView();

    void OnActivated(WorkflowProjectContext context);

    void OnDeactivated();
}
