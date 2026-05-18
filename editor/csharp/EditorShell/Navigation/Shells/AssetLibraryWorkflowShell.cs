using Avalonia.Controls;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class AssetLibraryWorkflowShell : IWorkflowShell
{
    public WorkflowSection Section => WorkflowSection.AssetLibrary;

    public string Title => "Asset Library";

    public string Subtitle => "Generate, review, approve, and place project assets.";

    public string PrimaryCtaLabel => "Generate";

    public bool IsAvailable(WorkflowProjectContext context) => context.HasOpenProject;

    public Control CreateView() => new AssetLibraryShellView();

    public void OnActivated(WorkflowProjectContext context)
    {
    }

    public void OnDeactivated()
    {
    }
}
