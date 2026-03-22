using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameForge.Editor.EditorShell;

public sealed record BenchmarkHardwareEnvelope
{
    [JsonPropertyName("gpu_name")]
    public string GpuName { get; init; } = string.Empty;

    [JsonPropertyName("gpu_vram_gb")]
    public int GpuVramGb { get; init; }

    [JsonPropertyName("cpu_cores")]
    public int CpuCores { get; init; }
}

public sealed record BenchmarkRecommendationEnvelope
{
    [JsonPropertyName("model_id")]
    public string ModelId { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("recommended")]
    public bool Recommended { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record BenchmarkResultEnvelope
{
    [JsonPropertyName("benchmark_schema")]
    public string BenchmarkSchema { get; init; } = string.Empty;

    [JsonPropertyName("is_first_run")]
    public bool IsFirstRun { get; init; }

    [JsonPropertyName("prepare_models_invoked")]
    public bool PrepareModelsInvoked { get; init; }

    [JsonPropertyName("hardware")]
    public BenchmarkHardwareEnvelope Hardware { get; init; } = new();

    [JsonPropertyName("recommendations")]
    public List<BenchmarkRecommendationEnvelope> Recommendations { get; init; } = [];
}

public static class FirstRunBenchmarkExample
{
    public static async Task<BenchmarkResultEnvelope> RunAsync(CancellationToken cancellationToken = default)
    {
        var projectRoot = ResolveProjectRoot();
        var scriptPath = Path.Combine(projectRoot, "ai-orchestration", "python", "orchestrator.py");

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePythonExecutable(),
            ArgumentList = { scriptPath, "--benchmark" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch Python benchmark subprocess.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Benchmark subprocess failed: {stderr}");
        }

        var response = JsonSerializer.Deserialize<BenchmarkResultEnvelope>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return response ?? throw new InvalidOperationException("Benchmark subprocess returned invalid JSON.");
    }

    public static void RenderConsoleFirstRunModal(BenchmarkResultEnvelope benchmark)
    {
        Console.WriteLine("=== First-Run Hardware Wizard ===");
        Console.WriteLine($"First run: {benchmark.IsFirstRun}");
        Console.WriteLine($"GPU: {benchmark.Hardware.GpuName} ({benchmark.Hardware.GpuVramGb} GB VRAM)");
        Console.WriteLine($"CPU Cores: {benchmark.Hardware.CpuCores}");
        Console.WriteLine($"Models prepared now: {benchmark.PrepareModelsInvoked}");

        var recommended = benchmark.Recommendations.Where(item => item.Recommended).ToList();
        Console.WriteLine("Recommended models:");
        foreach (var entry in recommended)
        {
            Console.WriteLine($"- {entry.ModelId} [{entry.Role}] -> {entry.Reason}");
        }

        if (recommended.Count == 0)
        {
            Console.WriteLine("- No GPU-qualified models detected; fallback to CPU-safe retrieval model.");
        }
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

        throw new DirectoryNotFoundException($"Could not resolve repository root from '{AppContext.BaseDirectory}'.");
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
