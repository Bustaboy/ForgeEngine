using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class TestingWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.Testing;

    public string Title => "Testing";

    public string Subtitle => "Run AI playtests and flag human follow-up.";

    public string PrimaryCtaLabel => "Run AI Playtest";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasPrototype;

    public Control CreateView() => new TestingShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
