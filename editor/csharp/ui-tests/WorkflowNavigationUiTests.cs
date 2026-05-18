using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Soul.Editor;
using Xunit;
using Soul.Editor.EditorShell.Navigation;
using Soul.Editor.EditorShell.UI;
using Soul.Editor.EditorShell.UI.Shells;

namespace Soul.Editor.UiTests;

public sealed class WorkflowNavigationUiTests
{
    [AvaloniaFact]
    public void MainWindow_NavigationBar_SwitchesShellVisibility()
    {
        var app = new App();
        app.Initialize();

        var window = new MainWindow();
        var viewModel = (EditorShell.ViewModels.MainWindowViewModel)window.DataContext!;

        Assert.True(viewModel.NavigateWorkflowSection(WorkflowSection.ProjectHome));
        var projectHome = window.FindControl<ProjectHomeShellView>("ProjectHomeShell");
        Assert.NotNull(projectHome);
        Assert.True(projectHome!.IsVisible);

        Assert.True(viewModel.NavigateWorkflowSection(WorkflowSection.Settings));
        var settings = window.FindControl<SettingsShellView>("SettingsShell");
        Assert.NotNull(settings);
        Assert.True(settings!.IsVisible);
    }

    [AvaloniaFact]
    public void ShellViews_ExposeWiredNamedButtons()
    {
        var projectHome = new ProjectHomeShellView();
        var prototype = new PrototypeShellView();
        var testing = new TestingShellView();
        var settings = new SettingsShellView();
        var publish = new PublishShellView();

        Assert.NotNull(projectHome.CreateNewGameButton);
        Assert.NotNull(projectHome.OpenGameProjectButton);
        Assert.NotNull(prototype.GeneratePrototypeButton);
        Assert.NotNull(prototype.RegenerateSectionButton);
        Assert.NotNull(testing.RunPlaytestButton);
        Assert.NotNull(settings.OpenSettingsButton);
        Assert.NotNull(publish.CheckReadinessButton);
    }
}
