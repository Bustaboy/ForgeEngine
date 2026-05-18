using Soul.Editor.EditorShell.Navigation;

namespace Soul.Editor.EditorShell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly WorkflowNavigationService _workflowNavigation = WorkflowShellRegistry.CreateDefault();
    private WorkflowSection _activeWorkflowSection = WorkflowSection.ProjectHome;

    public WorkflowSection ActiveWorkflowSection
    {
        get => _activeWorkflowSection;
        private set
        {
            if (!SetField(ref _activeWorkflowSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsProjectHomeSectionVisible));
            OnPropertyChanged(nameof(IsAiInterviewSectionVisible));
            OnPropertyChanged(nameof(IsPrototypeSectionVisible));
            OnPropertyChanged(nameof(IsEditorSectionVisible));
            OnPropertyChanged(nameof(IsTestingSectionVisible));
            OnPropertyChanged(nameof(IsPublishSectionVisible));
            OnPropertyChanged(nameof(IsAssetLibrarySectionVisible));
            OnPropertyChanged(nameof(IsSettingsSectionVisible));
            OnPropertyChanged(nameof(ActiveWorkflowSectionTitle));
        }
    }

    public string ActiveWorkflowSectionTitle =>
        _workflowNavigation.GetShell(ActiveWorkflowSection).Title;

    public bool IsProjectHomeSectionVisible => ActiveWorkflowSection == WorkflowSection.ProjectHome;

    public bool IsAiInterviewSectionVisible => ActiveWorkflowSection == WorkflowSection.AiInterview;

    public bool IsPrototypeSectionVisible => ActiveWorkflowSection == WorkflowSection.Prototype;

    public bool IsEditorSectionVisible => ActiveWorkflowSection == WorkflowSection.Editor;

    public bool IsTestingSectionVisible => ActiveWorkflowSection == WorkflowSection.Testing;

    public bool IsPublishSectionVisible => ActiveWorkflowSection == WorkflowSection.Publish;

    public bool IsAssetLibrarySectionVisible => ActiveWorkflowSection == WorkflowSection.AssetLibrary;

    public bool IsSettingsSectionVisible => ActiveWorkflowSection == WorkflowSection.Settings;

    public WorkflowNavigationService WorkflowNavigation => _workflowNavigation;

    public bool NavigateWorkflowSection(WorkflowSection section)
    {
        var context = BuildWorkflowProjectContext();
        if (!_workflowNavigation.Navigate(section, context))
        {
            return false;
        }

        ActiveWorkflowSection = _workflowNavigation.ActiveSection;
        return true;
    }

    public IReadOnlyList<(WorkflowSection Section, string Label)> GetVisibleWorkflowNavItems()
    {
        var context = BuildWorkflowProjectContext();
        return _workflowNavigation
            .GetVisibleSections(context, IsCreatorModeEnabled)
            .Select(section => (section, _workflowNavigation.GetShell(section).Title))
            .ToList();
    }

    private WorkflowProjectContext BuildWorkflowProjectContext() =>
        new(
            HasOpenProject: !string.IsNullOrWhiteSpace(ProjectRootPath) && ProjectRootPath != "(none)",
            HasPrototype: !string.IsNullOrWhiteSpace(PrototypeRoot) && PrototypeRoot != "(none)",
            IsCreatorModeEnabled: IsCreatorModeEnabled,
            ProjectRootPath: ProjectRootPath);

    private bool HasOpenProject => BuildWorkflowProjectContext().HasOpenProject;
}
