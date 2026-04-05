using System.Text;

namespace GameForge.Editor.EditorDiagnostics;

internal static class EditorDiagnosticsLog
{
    private static readonly object SyncRoot = new();
    private static string? _currentLogPath;
    private static string? _logDirectoryOverride;
    private static bool _sessionInitialized;
    private static string? _sessionId;

    internal static string CurrentLogPath
    {
        get
        {
            lock (SyncRoot)
            {
                return EnsureLogPath();
            }
        }
    }

    /// <summary>Session id written at startup; null before first successful <see cref="InitializeSession"/>.</summary>
    internal static string? SessionId
    {
        get
        {
            lock (SyncRoot)
            {
                return _sessionId;
            }
        }
    }

    internal static void SetLogDirectoryOverride(string? directoryPath)
    {
        lock (SyncRoot)
        {
            _logDirectoryOverride = directoryPath;
            _currentLogPath = null;
            _sessionInitialized = false;
            _sessionId = null;
        }
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _currentLogPath = null;
            _logDirectoryOverride = null;
            _sessionInitialized = false;
            _sessionId = null;
        }
    }

    public static void InitializeSession(IReadOnlyList<string> args)
    {
        lock (SyncRoot)
        {
            var logPath = EnsureLogPath();
            if (_sessionInitialized)
            {
                return;
            }

            _sessionInitialized = true;
            _sessionId = Guid.NewGuid().ToString("D");
            WriteEntryUnsafe(
                "INFO",
                $"Editor session started | session={_sessionId} | pid={Environment.ProcessId} | args={(args.Count == 0 ? "(none)" : string.Join(" ", args))}",
                null);
            WriteEntryUnsafe("INFO", $"Diagnostics log path: {logPath}", null);
        }
    }

    public static void LogInfo(string message) => WriteEntry("INFO", message, null);

    public static void LogWarning(string message) => WriteEntry("WARN", message, null);

    public static void LogError(string message) => WriteEntry("ERROR", message, null);

    public static void LogException(string context, Exception exception, bool isFatal = false)
    {
        WriteEntry(isFatal ? "FATAL" : "ERROR", context, exception);
    }

    private static void WriteEntry(string level, string message, Exception? exception)
    {
        lock (SyncRoot)
        {
            EnsureLogPath();
            WriteEntryUnsafe(level, message, exception);
        }
    }

    private static void WriteEntryUnsafe(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append("] [")
            .Append(level)
            .Append("] [tid=")
            .Append(Environment.CurrentManagedThreadId)
            .Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        builder.AppendLine();
        File.AppendAllText(EnsureLogPath(), builder.ToString(), Encoding.UTF8);
    }

    private static string EnsureLogPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentLogPath))
        {
            return _currentLogPath;
        }

        List<string> errors = [];
        foreach (var logDirectory in ResolveLogDirectoryCandidates())
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                var candidatePath = Path.Combine(logDirectory, $"editor-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(candidatePath, string.Empty, Encoding.UTF8);
                _currentLogPath = candidatePath;
                return _currentLogPath;
            }
            catch (Exception ex)
            {
                errors.Add($"{logDirectory}: {ex.Message}");
            }
        }

        throw new IOException($"Unable to initialize diagnostics log path. {string.Join(" | ", errors)}");
    }

    private static IEnumerable<string> ResolveLogDirectoryCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_logDirectoryOverride))
        {
            yield return _logDirectoryOverride;
            yield break;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Soul Loom LLC", "Soul Loom", "logs");
            yield return Path.Combine(localAppData, "GameForge", "logs");
        }

        yield return Path.Combine(Environment.CurrentDirectory, ".soulloom", "logs");
        yield return Path.Combine(Environment.CurrentDirectory, ".forgeengine", "logs");

        var tempPath = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(tempPath))
        {
            yield return Path.Combine(tempPath, "SoulLoom", "logs");
            yield return Path.Combine(tempPath, "GameForge", "logs");
        }
    }
}
