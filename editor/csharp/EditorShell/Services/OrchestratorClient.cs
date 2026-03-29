using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameForge.Editor.EditorShell.Services;

public sealed record PipelineExecutionEnvelope
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("brief_path")]
    public string BriefPath { get; init; } = string.Empty;

    [JsonPropertyName("prototype_root")]
    public string? PrototypeRoot { get; init; }

    [JsonPropertyName("runtime_launch_status")]
    public string RuntimeLaunchStatus { get; init; } = string.Empty;

    [JsonPropertyName("runtime_launch_pid")]
    public int? RuntimeLaunchPid { get; init; }

    [JsonPropertyName("runtime_launch_manifest_path")]
    public string? RuntimeLaunchManifestPath { get; init; }

    [JsonPropertyName("runtime_launch_executable_path")]
    public string? RuntimeLaunchExecutablePath { get; init; }

    [JsonPropertyName("dead_end_blockers")]
    public IReadOnlyList<string> DeadEndBlockers { get; init; } = [];
}

public sealed record PipelineRunResponse
{
    public required int ExitCode { get; init; }

    public required string Stdout { get; init; }

    public required string Stderr { get; init; }

    public PipelineExecutionEnvelope? Result { get; init; }
}

public sealed class OrchestratorClient
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<PipelineRunResponse> RunGenerationPipelineAsync(string briefPath, bool launchRuntime, CancellationToken cancellationToken = default)
    {
        var projectRoot = ResolveProjectRoot();
        var scriptPath = Path.Combine(projectRoot, "ai-orchestration", "python", "orchestrator.py");
        var outputPath = Path.Combine(projectRoot, "build", "generated-prototypes");

        Directory.CreateDirectory(outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(PythonEnvironment.GetRepositoryVirtualEnvironmentPythonExecutable(projectRoot))
                ? PythonEnvironment.GetRepositoryVirtualEnvironmentPythonExecutable(projectRoot)
                : PythonEnvironment.ResolvePythonExecutable(projectRoot),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--run-generation-pipeline");
        startInfo.ArgumentList.Add("--generate-prototype");
        startInfo.ArgumentList.Add(briefPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(launchRuntime ? "--launch-runtime" : "--no-launch-runtime");
        HuggingFaceTokenStore.ApplyToProcessStartInfo(startInfo, projectRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch orchestrator subprocess.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new PipelineRunResponse
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            Result = TryParsePipelineResult(stdout),
        };
    }

    public static string CreateBriefFromChatPrompt(string prompt)
    {
        var trimmed = prompt.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Brief prompt is required.", nameof(prompt));
        }

        var projectRoot = ResolveProjectRoot();
        var briefsDir = Path.Combine(projectRoot, "build", "editor-briefs");
        Directory.CreateDirectory(briefsDir);

        var slug = Slugify(trimmed);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var path = Path.Combine(briefsDir, $"{slug}-{timestamp}.json");

        var payload = new
        {
            concept = trimmed,
            mechanics = new
            {
                core_loop = "Gather -> Build -> Progress",
            },
            style = new
            {
                ui_direction = "Minimal readable HUD",
            },
            narrative = new
            {
                world_notes = "Generated from editor chat co-pilot.",
            },
            commercial = false,
            monetization = "none",
            commercial_policy_acknowledged = false,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Utf8WithoutBom);
        return path;
    }

    public static string CreateBriefFromTemplate(
        string templateId,
        string templateName,
        string templateCoreLoop,
        string projectName,
        string quickConcept)
    {
        var trimmedName = string.IsNullOrWhiteSpace(projectName) ? templateName : projectName.Trim();
        var trimmedConcept = quickConcept?.Trim() ?? string.Empty;
        var narrativeNotes = string.IsNullOrWhiteSpace(trimmedConcept)
            ? $"Generated from {templateName} template."
            : trimmedConcept;

        var projectRoot = ResolveProjectRoot();
        var briefsDir = Path.Combine(projectRoot, "build", "editor-briefs");
        Directory.CreateDirectory(briefsDir);

        var slug = Slugify(trimmedName);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var path = Path.Combine(briefsDir, $"{slug}-{timestamp}.json");

        var payload = new
        {
            concept = trimmedName,
            template = new
            {
                id = templateId,
                name = templateName,
                starter_scene_scaffold = true,
            },
            mechanics = new
            {
                core_loop = templateCoreLoop,
            },
            style = new
            {
                ui_direction = "Sleek icon-heavy editor HUD",
            },
            narrative = new
            {
                world_notes = narrativeNotes,
            },
            commercial = false,
            monetization = "none",
            commercial_policy_acknowledged = false,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Utf8WithoutBom);
        return path;
    }

    private static PipelineExecutionEnvelope? TryParsePipelineResult(string stdout)
    {
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        var jsonPayload = stdout[start..(end + 1)];
        try
        {
            return JsonSerializer.Deserialize<PipelineExecutionEnvelope>(jsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            return null;
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

    private static string Slugify(string value)
    {
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        var compact = normalized;
        while (compact.Contains("--", StringComparison.Ordinal))
        {
            compact = compact.Replace("--", "-", StringComparison.Ordinal);
        }

        compact = compact.Trim('-');
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "prototype";
        }

        return compact.Length <= 48 ? compact : compact[..48];
    }
}
