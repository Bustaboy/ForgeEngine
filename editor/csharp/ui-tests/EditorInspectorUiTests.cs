using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Soul.Editor;
using Soul.Editor.EditorShell.UI;
using Xunit;

namespace Soul.Editor.UiTests;

public sealed class EditorInspectorUiTests
{
    [AvaloniaFact]
    public void MainWindow_AdvancedInspectorRegions_HiddenByDefault()
    {
        var app = new App();
        app.Initialize();

        var window = new MainWindow();
        var viewModel = window.DataContext as EditorShell.ViewModels.MainWindowViewModel
            ?? throw new InvalidOperationException("MainWindow missing view model.");

        Assert.False(viewModel.IsAdvancedInspectorEnabled);

        var exportBorder = window.FindControl<Border>("ExportPublishInspectorBorder");
        Assert.NotNull(exportBorder);
        Assert.False(exportBorder!.IsVisible);
    }

    [AvaloniaFact]
    public void MainWindow_AdvancedInspectorRegions_VisibleWhenAdvancedEnabled()
    {
        var app = new App();
        app.Initialize();

        var window = new MainWindow();
        var viewModel = (EditorShell.ViewModels.MainWindowViewModel)window.DataContext!;

        viewModel.IsAdvancedInspectorEnabled = true;

        var exportBorder = window.FindControl<Border>("ExportPublishInspectorBorder");
        Assert.NotNull(exportBorder);
        Assert.True(exportBorder!.IsVisible);
    }
}
