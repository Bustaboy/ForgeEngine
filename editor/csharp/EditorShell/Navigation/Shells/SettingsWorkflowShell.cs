using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class SettingsWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.Settings;

    public string Title => "Settings";

    public string Subtitle => "Preferences, models, and playtest options.";

    public string PrimaryCtaLabel => "Open Settings";

    public bool IsAvailable(WorkflowProjectContext context) => true;

    public Control CreateView() => new SettingsShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
