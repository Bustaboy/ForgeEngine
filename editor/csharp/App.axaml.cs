using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GameForge.Editor.EditorDiagnostics;
using GameForge.Editor.EditorShell.UI;

namespace GameForge.Editor;

public partial class App : Application
{
    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
            EditorDiagnosticsLog.LogInfo("Avalonia application resources loaded.");
        }
        catch (Exception ex)
        {
            EditorDiagnosticsLog.LogException("Failed to load Avalonia application resources.", ex, isFatal: true);
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
        {
            EditorDiagnosticsLog.LogException("Avalonia UI thread unhandled exception.", eventArgs.Exception, isFatal: true);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = new MainWindow();
                EditorDiagnosticsLog.LogInfo("Main editor window initialized.");
            }
            catch (Exception ex)
            {
                EditorDiagnosticsLog.LogException("Failed to initialize main editor window.", ex, isFatal: true);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
