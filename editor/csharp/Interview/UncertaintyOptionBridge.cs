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

public sealed record DirectionProposalEnvelope
{
    [JsonPropertyName("direction_id")]
    public string DirectionId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("elevator_pitch")]
    public string ElevatorPitch { get; init; } = string.Empty;

    [JsonPropertyName("gameplay_pillars")]
    public List<string> GameplayPillars { get; init; } = [];

    [JsonPropertyName("prototype_seed")]
    public Dictionary<string, JsonElement> PrototypeSeed { get; init; } = [];

    [JsonPropertyName("tradeoff")]
    public string Tradeoff { get; init; } = string.Empty;
}

public sealed record ThinkForMeResponseEnvelope
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("source_input")]
    public string SourceInput { get; init; } = string.Empty;

    [JsonPropertyName("triggered")]
    public bool Triggered { get; init; }

    [JsonPropertyName("confirmation_required")]
    public bool ConfirmationRequired { get; init; }

    [JsonPropertyName("proposals")]
    public List<DirectionProposalEnvelope> Proposals { get; init; } = [];

    [JsonPropertyName("human_summary_markdown")]
    public string HumanSummaryMarkdown { get; init; } = string.Empty;
}

public static class UncertaintyOptionBridge
{
    public static async Task<SuggestionResponseEnvelope> GenerateOptionsAsync(string userInput, string topic, CancellationToken cancellationToken = default)
    {
        var stdout = await RunOrchestratorAsync(
            "--suggest-uncertain",
            userInput,
            "--topic",
            topic,
            cancellationToken);

        var response = JsonSerializer.Deserialize<SuggestionResponseEnvelope>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return response ?? throw new InvalidOperationException("Python uncertainty option bridge returned invalid JSON.");
    }

    public static async Task<ThinkForMeResponseEnvelope> GenerateThinkForMeDirectionsAsync(string userInput, string topic, CancellationToken cancellationToken = default)
    {
        var stdout = await RunOrchestratorAsync(
            "--think-for-me",
            userInput,
            "--topic",
            topic,
            cancellationToken);

        var response = JsonSerializer.Deserialize<ThinkForMeResponseEnvelope>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return response ?? throw new InvalidOperationException("Python think-for-me bridge returned invalid JSON.");
    }

    private static async Task<string> RunOrchestratorAsync(string firstFlag, string firstValue, string secondFlag, string secondValue, CancellationToken cancellationToken)
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
                firstFlag,
                firstValue,
                secondFlag,
                secondValue,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Python orchestration bridge.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python orchestration bridge failed: {stderr}");
        }

        return stdout;
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
