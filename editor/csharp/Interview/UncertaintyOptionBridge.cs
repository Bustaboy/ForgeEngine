using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameForge.Editor.Interview;

public sealed record SuggestionResponseEnvelope
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("source_input")]
    public string SourceInput { get; init; } = string.Empty;

    [JsonPropertyName("ambiguous")]
    public bool Ambiguous { get; init; }

    [JsonPropertyName("options")]
    public List<UncertaintyOption> Options { get; init; } = [];
}

public static class UncertaintyOptionBridge
{
    public static async Task<SuggestionResponseEnvelope> GenerateOptionsAsync(string userInput, string topic, CancellationToken cancellationToken = default)
    {
        var projectRoot = ResolveProjectRoot();
        var scriptPath = Path.Combine(projectRoot, "ai-orchestration", "python", "orchestrator.py");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Could not find orchestrator.py at '{scriptPath}'.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePythonExecutable(),
            ArgumentList =
            {
                scriptPath,
                "--suggest-uncertain",
                userInput,
                "--topic",
                topic,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Python uncertainty option bridge.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python uncertainty option bridge failed: {stderr}");
        }

        var response = JsonSerializer.Deserialize<SuggestionResponseEnvelope>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return response ?? throw new InvalidOperationException("Python uncertainty option bridge returned invalid JSON.");
    }

    private static string ResolveProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "ai-orchestration", "python", "orchestrator.py");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not resolve GameForge repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string ResolvePythonExecutable()
    {
        var pinned = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(pinned))
        {
            return pinned;
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }
}
