using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GameForge.Editor.EditorShell.UI;

namespace GameForge.Editor.EditorDiagnostics;

internal static class EditorCrashHandler
{
    private static int _globalHandlersRegistered;

    public static void RegisterGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _globalHandlersRegistered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void OnAvaloniaUiUnhandledException(DispatcherUnhandledExceptionEventArgs e)
    {
        EditorDiagnosticsLog.LogException("Avalonia UI thread unhandled exception.", e.Exception, isFatal: true);
        e.Handled = true;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await TryShowFatalWindowAsync(e.Exception);
            }
            finally
            {
                ShutdownWithExitCode(1);
            }
        });
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            EditorDiagnosticsLog.LogException(
                $"AppDomain unhandled exception (terminating={e.IsTerminating})",
                exception,
                isFatal: true);
        }
        else
        {
            EditorDiagnosticsLog.LogError(
                $"AppDomain unhandled exception (terminating={e.IsTerminating}) with non-Exception payload.");
        }

        if (e.ExceptionObject is Exception ex && e.IsTerminating)
        {
            TryNotifyFatalInteractively(ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        EditorDiagnosticsLog.LogException("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static void TryNotifyFatalInteractively(Exception exception)
    {
        if (Application.Current is null)
        {
            WriteFatalStderr();
            return;
        }

        try
        {
            using var shown = new ManualResetEventSlim(false);
            Dispatcher.UIThread.Post(
                () => _ = ShowFatalWindowAndSignalAsync(exception, shown),
                DispatcherPriority.Send);
            shown.Wait(TimeSpan.FromSeconds(45));
        }
        catch
        {
            // Best-effort only; exception is already logged.
        }
    }

    private static async Task ShowFatalWindowAndSignalAsync(Exception exception, ManualResetEventSlim signal)
    {
        try
        {
            await TryShowFatalWindowAsync(exception);
        }
        finally
        {
            signal.Set();
        }
    }

    private static async Task TryShowFatalWindowAsync(Exception exception)
    {
        string logPath;
        try
        {
            logPath = EditorDiagnosticsLog.CurrentLogPath;
        }
        catch
        {
            logPath = "(unable to resolve log path)";
        }

        WriteFatalStderr(logPath);

        if (Application.Current is null)
        {
            return;
        }

        var owner = Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        var summary = $"{exception.GetType().Name}: {exception.Message}";
        var win = new FatalErrorReportWindow(summary, exception.ToString(), logPath);
        if (owner is not null)
        {
            await win.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource<object?>();
            win.Closed += (_, _) => closed.TrySetResult(null);
            win.Show();
            await closed.Task;
        }
    }

    private static void WriteFatalStderr(string? logPath = null)
    {
        try
        {
            logPath ??= EditorDiagnosticsLog.CurrentLogPath;
            Console.Error.WriteLine(
                $"Fatal error. Details were written to the diagnostics log.{Environment.NewLine}{logPath}");
        }
        catch
        {
            // ignore
        }
    }

    private static void ShutdownWithExitCode(int exitCode)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
                return;
            }
        }
        catch
        {
            // fall through
        }

        Environment.Exit(exitCode);
    }
}
