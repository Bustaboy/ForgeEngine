using System.Diagnostics;
using System.Text.Json;

namespace GameForge.Editor.Interview;

public sealed record SuggestionResponseEnvelope
{
    public string Topic { get; init; } = string.Empty;
    public string SourceInput { get; init; } = string.Empty;
    public bool Ambiguous { get; init; }
    public List<UncertaintyOption> Options { get; init; } = [];
}

public static class UncertaintyOptionBridge
{
    public static async Task<SuggestionResponseEnvelope> GenerateOptionsAsync(string userInput, string topic, CancellationToken cancellationToken = default)
    {
        var projectRoot = ResolveProjectRoot();
        var scriptPath = Path.Combine(projectRoot, "ai-orchestration", "python", "orchestrator.py");

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
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
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
