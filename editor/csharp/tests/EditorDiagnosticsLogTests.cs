using GameForge.Editor.EditorDiagnostics;

namespace GameForge.Editor.Tests;

public sealed class EditorDiagnosticsLogTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "soulloom-editor-log-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void InitializeSession_CreatesAndAppendsDiagnosticsLog()
    {
        Directory.CreateDirectory(_logDirectory);
        EditorDiagnosticsLog.SetLogDirectoryOverride(_logDirectory);
        EditorDiagnosticsLog.InitializeSession(["--editor-ui"]);

        EditorDiagnosticsLog.LogWarning("Smoke warning entry");

        var logPath = EditorDiagnosticsLog.CurrentLogPath;
        Assert.StartsWith(_logDirectory, logPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(logPath));

        var content = File.ReadAllText(logPath);
        Assert.Contains("Editor session started", content);
        Assert.Contains("session=", content);
        Assert.Contains("Smoke warning entry", content);
        Assert.Contains("[tid=", content);
    }

    [Fact]
    public void EditorDiagnosticsTraceListener_ForwardsTraceLinesToLog()
    {
        Directory.CreateDirectory(_logDirectory);
        EditorDiagnosticsLog.SetLogDirectoryOverride(_logDirectory);
        EditorDiagnosticsLog.InitializeSession([]);

        using (var listener = new EditorDiagnosticsTraceListener())
        {
            listener.WriteLine("trace-test-line");
        }

        var logPath = EditorDiagnosticsLog.CurrentLogPath;
        var content = File.ReadAllText(logPath);
        Assert.Contains("[avalonia-trace] trace-test-line", content);
    }

    public void Dispose()
    {
        EditorDiagnosticsLog.ResetForTests();
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }
}
