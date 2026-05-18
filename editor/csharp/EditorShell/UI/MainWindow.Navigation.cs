using Avalonia.Controls;
using Soul.Editor.EditorShell.Navigation;
using Soul.Editor.EditorShell.UI.Shells;
using Soul.Editor.EditorShell.ViewModels;

namespace Soul.Editor.EditorShell.UI;

public partial class MainWindow
{
    private WorkflowNavigationBar? _workflowNavigationBar;
    private ProjectHomeShellView? _projectHomeShell;
    private AiInterviewShellView? _aiInterviewShell;
    private PrototypeShellView? _prototypeShell;
    private TestingShellView? _testingShell;
    private PublishShellView? _publishShell;
    private AssetLibraryShellView? _assetLibraryShell;
    private SettingsShellView? _settingsShell;

    private void InitializeWorkflowNavigation()
    {
        _workflowNavigationBar = this.FindControl<WorkflowNavigationBar>("WorkflowNavigationBar");
        _projectHomeShell = this.FindControl<ProjectHomeShellView>("ProjectHomeShell");
        _aiInterviewShell = this.FindControl<AiInterviewShellView>("AiInterviewShell");
        _prototypeShell = this.FindControl<PrototypeShellView>("PrototypeShell");
        _testingShell = this.FindControl<TestingShellView>("TestingShell");
        _publishShell = this.FindControl<PublishShellView>("PublishShell");
        _assetLibraryShell = this.FindControl<AssetLibraryShellView>("AssetLibraryShell");
        _settingsShell = this.FindControl<SettingsShellView>("SettingsShell");

        if (_workflowNavigationBar is null)
        {
            return;
        }

        RefreshWorkflowNavigationBar();
        _workflowNavigationBar.SectionSelected += OnWorkflowSectionSelected;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.IsCreatorModeEnabled)
                or nameof(MainWindowViewModel.ProjectRootPath)
                or nameof(MainWindowViewModel.PrototypeRoot))
            {
                RefreshWorkflowNavigationBar();
            }
        };

        WireShellActions();
        MountSharedPanels();
        _viewModel.NavigateWorkflowSection(WorkflowSection.ProjectHome);
    }

    private void RefreshWorkflowNavigationBar()
    {
        if (_workflowNavigationBar is null)
        {
            return;
        }

        var items = _viewModel.GetVisibleWorkflowNavItems();
        _workflowNavigationBar.BindSections(items, _viewModel.ActiveWorkflowSection);
    }

    private void OnWorkflowSectionSelected(object? sender, WorkflowSection section)
    {
        if (_viewModel.NavigateWorkflowSection(section))
        {
            _workflowNavigationBar?.SetActiveSection(section);
            UpdateWorkflowShellChrome(section);
        }
    }

    private void UpdateWorkflowShellChrome(WorkflowSection section)
    {
        var hideBottomInterview = section == WorkflowSection.AiInterview;
        var hideBottomAssets = section == WorkflowSection.AssetLibrary;

        if (_aiInterviewDockTab is not null)
        {
            _aiInterviewDockTab.IsVisible = !hideBottomInterview;
        }

        if (_assetsDockTab is not null)
        {
            _assetsDockTab.IsVisible = !hideBottomAssets;
        }
    }

    private void WireShellActions()
    {
        if (_projectHomeShell is not null)
        {
            _projectHomeShell.CreateNewGameButton.Click += (_, _) => OnNewSceneClick(this, new());
            _projectHomeShell.OpenGameProjectButton.Click += (_, _) => OnOpenProjectFolderClick(this, new());
        }

        if (_prototypeShell is not null)
        {
            _prototypeShell.GeneratePrototypeButton.Click += (_, _) => OnGenerateFromBriefClick(this, new());
            _prototypeShell.RegenerateSectionButton.Click += (_, _) => OnGenerateFromBriefClick(this, new());
        }

        if (_testingShell is not null)
        {
            _testingShell.RunPlaytestButton.Click += async (_, _) => await _viewModel.RunModelOnboardingAsync();
        }

        if (_settingsShell is not null)
        {
            _settingsShell.OpenSettingsButton.Click += (_, _) => OnOpenSettingsClick(this, new());
        }
    }

    private void MountSharedPanels()
    {
        if (_aiInterviewShell is not null && _aiInterviewPanelHost is not null)
        {
            if (_aiInterviewPanelHost.Parent is Panel parent)
            {
                parent.Children.Remove(_aiInterviewPanelHost);
            }

            _aiInterviewShell.InterviewHost.Content = _aiInterviewPanelHost;
        }

        if (_assetLibraryShell is not null && _assetsPanelHost is not null)
        {
            if (_assetsPanelHost.Parent is Panel assetsParent)
            {
                assetsParent.Children.Remove(_assetsPanelHost);
            }

            _assetLibraryShell.AssetHost.Content = _assetsPanelHost;
        }

        if (_publishShell is not null)
        {
            _publishShell.CheckReadinessButton.Click += (_, _) => OnOpenExportChecklistClick(this, new());
        }
    }
}
