using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GameForge.Editor.EditorShell.Services;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly OrchestratorClient _orchestratorClient = new();

    private string _chatPrompt = string.Empty;
    private string _statusMessage = "Ready: describe your game, then click Generate & Play.";
    private bool _isBusy;
    private string? _lastBriefPath;
    private bool _isCodeMode;
    private bool _isAdvancedInspectorEnabled;
    private bool _isDebugConsoleEnabled;
    private string _statusToastMessage = string.Empty;
    private bool _isStatusToastVisible;
    private string _pipelineProgress = "Idle";
    private int? _runtimePid;
    private string _runtimeLaunchStatus = "Not launched";
    private string _prototypeRoot = "(none)";
    private string _monacoEditorContent = "// Generate a prototype to load editable C++ runtime code.";
    private string _runtimePreviewSummary = "Generate & Play to populate runtime preview.";
    private string _runtimeEntityList = "No generated entities yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ChatPrompt
    {
        get => _chatPrompt;
        set
        {
            SetField(ref _chatPrompt, value);
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetField(ref _isBusy, value);
            OnPropertyChanged(nameof(CanGenerate));
            OnPropertyChanged(nameof(GenerateButtonLabel));
        }
    }

    public bool IsCodeMode
    {
        get => _isCodeMode;
        set
        {
            var enteringCodeMode = value && !_isCodeMode;
            SetField(ref _isCodeMode, value);
            OnPropertyChanged(nameof(IsViewportMode));

            if (enteringCodeMode)
            {
                LoadGeneratedCodeForEditor();
            }
        }
    }

    public bool IsViewportMode => !IsCodeMode;

    public bool IsAdvancedInspectorEnabled
    {
        get => _isAdvancedInspectorEnabled;
        set => SetField(ref _isAdvancedInspectorEnabled, value);
    }

    public bool IsDebugConsoleEnabled
    {
        get => _isDebugConsoleEnabled;
        set => SetField(ref _isDebugConsoleEnabled, value);
    }

    public string StatusToastMessage
    {
        get => _statusToastMessage;
        private set => SetField(ref _statusToastMessage, value);
    }

    public bool IsStatusToastVisible
    {
        get => _isStatusToastVisible;
        private set => SetField(ref _isStatusToastVisible, value);
    }

    public bool CanGenerate => !IsBusy && !string.IsNullOrWhiteSpace(ChatPrompt);

    public string GenerateButtonLabel => IsBusy ? "Generating..." : "Generate & Play";

    public string PipelineProgress
    {
        get => _pipelineProgress;
        private set => SetField(ref _pipelineProgress, value);
    }

    public string RuntimeLaunchStatus
    {
        get => _runtimeLaunchStatus;
        private set => SetField(ref _runtimeLaunchStatus, value);
    }

    public int? RuntimePid
    {
        get => _runtimePid;
        private set
        {
            SetField(ref _runtimePid, value);
            OnPropertyChanged(nameof(RuntimePidLabel));
            OnPropertyChanged(nameof(IsRuntimeLive));
        }
    }

    public string RuntimePidLabel => RuntimePid is int pid ? pid.ToString() : "n/a";

    public bool IsRuntimeLive => RuntimePid is int;

    public string PrototypeRoot
    {
        get => _prototypeRoot;
        private set => SetField(ref _prototypeRoot, value);
    }

    public string MonacoEditorContent
    {
        get => _monacoEditorContent;
        set => SetField(ref _monacoEditorContent, value);
    }

    public string RuntimePreviewSummary
    {
        get => _runtimePreviewSummary;
        private set => SetField(ref _runtimePreviewSummary, value);
    }

    public string RuntimeEntityList
    {
        get => _runtimeEntityList;
        private set => SetField(ref _runtimeEntityList, value);
    }

    public Task NewPrototypeAsync(CancellationToken cancellationToken = default)
    {
        ChatPrompt = string.Empty;
        _lastBriefPath = null;
        RuntimePid = null;
        RuntimeLaunchStatus = "Not launched";
        PipelineProgress = "Idle";
        PrototypeRoot = "(none)";
        RuntimePreviewSummary = "Generate & Play to populate runtime preview.";
        RuntimeEntityList = "No generated entities yet.";
        MonacoEditorContent = "// New prototype ready.\n// Generate content to load editable C++ runtime code.";
        StatusMessage = "New prototype started. Describe your game to continue.";
        ShowToast("New prototype ready.");
        return Task.CompletedTask;
    }

    public async Task GenerateFromBriefAsync(bool launchRuntime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ChatPrompt))
        {
            StatusMessage = "Describe your game first to generate a prototype.";
            ShowToast("Brief required before generation.");
            return;
        }

        _lastBriefPath = OrchestratorClient.CreateBriefFromChatPrompt(ChatPrompt);
        await RunPipelineForBriefAsync(_lastBriefPath, launchRuntime, cancellationToken);
    }

    public async Task PlayRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(PrototypeRoot) && PrototypeRoot != "(none)")
        {
            await RelaunchGeneratedRuntimeAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastBriefPath))
        {
            StatusMessage = "No generated brief yet. Click Generate & Play first.";
            ShowToast("Generate once before launching runtime.");
            return;
        }

        await RunPipelineForBriefAsync(_lastBriefPath, launchRuntime: true, cancellationToken);
    }

    public async Task SaveCodeEditsAsync(CancellationToken cancellationToken = default)
    {
        if (!TryResolveEditableCodePath(out var codePath, out var errorMessage))
        {
            StatusMessage = errorMessage;
            ShowToast(errorMessage);
            return;
        }

        await File.WriteAllTextAsync(codePath, MonacoEditorContent, cancellationToken);
        StatusMessage = $"Saved runtime code: {codePath}";
        ShowToast("Code saved. Recompiling runtime...");
        await RecompileAndRelaunchRuntimeAsync(cancellationToken);
    }

    private async Task RecompileAndRelaunchRuntimeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            StatusMessage = "No generated prototype to recompile.";
            ShowToast("Generate once before recompiling runtime.");
            return;
        }

        var generatedRoot = Path.Combine(PrototypeRoot, "generated");
        if (!Directory.Exists(generatedRoot))
        {
            RuntimeLaunchStatus = "Recompile blocked";
            PipelineProgress = "Recompile blocked";
            StatusMessage = $"Generated runtime folder missing: {generatedRoot}";
            ShowToast("Generated runtime folder missing.");
            return;
        }

        RuntimeLaunchStatus = "Recompiling generated runtime";
        PipelineProgress = "Code Mode: CMake configure";
        StatusMessage = "Save complete. Running CMake configure for generated runtime...";
        ShowToast("CMake configure started...");

        var generatedBuildRoot = Path.Combine(generatedRoot, "build");
        var configureResult = await RunProcessAsync(
            "cmake",
            $"-S \"{generatedRoot}\" -B \"{generatedBuildRoot}\"",
            PrototypeRoot,
            cancellationToken);
        if (configureResult.ExitCode != 0)
        {
            RuntimeLaunchStatus = "Compile failed";
            PipelineProgress = "Compile failed";
            StatusMessage = BuildCompileFailureMessage("CMake configure", configureResult.Stdout, configureResult.Stderr);
            ShowToast("CMake configure failed. See status details.");
            return;
        }

        PipelineProgress = "Code Mode: CMake build";
        RuntimeLaunchStatus = "Building generated runtime";
        StatusMessage = "CMake configure passed. Building generated_gameplay_runner...";
        ShowToast("CMake build started...");

        var buildResult = await RunProcessAsync(
            "cmake",
            $"--build \"{generatedBuildRoot}\"",
            PrototypeRoot,
            cancellationToken);
        if (buildResult.ExitCode != 0)
        {
            RuntimeLaunchStatus = "Build failed";
            PipelineProgress = "Build failed";
            StatusMessage = BuildCompileFailureMessage("CMake build", buildResult.Stdout, buildResult.Stderr);
            ShowToast("CMake build failed. See status details.");
            return;
        }

        PipelineProgress = "Code Mode: launching generated runner";
        StatusMessage = "Build passed. Relaunching generated runtime runner...";
        ShowToast("Launching generated runner...");
        var launchResult = LaunchGeneratedRunner(generatedBuildRoot);
        if (!launchResult.Success)
        {
            RuntimeLaunchStatus = "Launch failed";
            PipelineProgress = "Launch failed";
            StatusMessage = launchResult.ErrorMessage;
            ShowToast("Generated runtime launch failed.");
            return;
        }

        RuntimePid = launchResult.Pid;
        RuntimeLaunchStatus = "Running";
        RuntimePreviewSummary = $"Live in Vulkan (PID: {launchResult.Pid})";
        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);
        PipelineProgress = "Runtime relaunched from Code Mode";
        StatusMessage = $"Save & Recompile complete. generated_gameplay_runner launched (PID: {launchResult.Pid}).";
        ShowToast("Save & Recompile completed.");
    }

    private async Task RelaunchGeneratedRuntimeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            StatusMessage = "No generated prototype available for relaunch.";
            ShowToast("Generate once before runtime relaunch.");
            return;
        }

        RuntimeLaunchStatus = "Relaunching";
        PipelineProgress = "Runtime relaunch requested";
        StatusMessage = "Relaunching generated runtime from latest build...";
        ShowToast("Relaunch runtime requested...");
        await RecompileAndRelaunchRuntimeAsync(cancellationToken);
    }

    public void SetStatusMessage(string value)
    {
        StatusMessage = value;
        ShowToast(value);
    }

    private async Task RunPipelineForBriefAsync(string briefPath, bool launchRuntime, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        PipelineProgress = launchRuntime ? "Generate + launch runtime" : "Generate prototype only";
        StatusMessage = "Running generation pipeline...";
        ShowToast($"Pipeline: {PipelineProgress}");

        try
        {
            var response = await _orchestratorClient.RunGenerationPipelineAsync(briefPath, launchRuntime, cancellationToken);
            StatusMessage = BuildStatusMessage(response);
            ShowToast(BuildToastMessage(response));
            ApplyRuntimePreview(response);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pipeline failed: {ex.Message}";
            PipelineProgress = "Failed";
            ShowToast("Generation failed. See status panel.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyRuntimePreview(PipelineRunResponse response)
    {
        PipelineProgress = response.Result?.Status ?? (response.ExitCode == 0 ? "Completed" : "Failed");
        RuntimeLaunchStatus = response.Result?.RuntimeLaunchStatus ?? "Unknown";
        RuntimePid = response.Result?.RuntimeLaunchPid;
        PrototypeRoot = response.Result?.PrototypeRoot ?? "(none)";

        RuntimePreviewSummary = RuntimePid is int pid
            ? $"Live in Vulkan (PID: {pid})"
            : $"Runtime status: {RuntimeLaunchStatus}";

        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);

        var loadedCode = TryLoadGeneratedRuntimeCpp(PrototypeRoot);
        if (!string.IsNullOrWhiteSpace(loadedCode))
        {
            MonacoEditorContent = loadedCode;
        }
    }

    private static string TryLoadGeneratedRuntimeCpp(string prototypeRoot)
    {
        if (string.IsNullOrWhiteSpace(prototypeRoot) || prototypeRoot == "(none)")
        {
            return string.Empty;
        }

        var candidatePaths = new[]
        {
            Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"),
            Path.Combine(prototypeRoot, "generated", "cpp", "player_controller.cpp"),
            Path.Combine(prototypeRoot, "generated", "cpp", "basic_npc.cpp"),
            Path.Combine(prototypeRoot, "runtime", "main.cpp"),
            Path.Combine(prototypeRoot, "runtime", "scene.cpp"),
            Path.Combine(prototypeRoot, "scene.cpp"),
            Path.Combine(prototypeRoot, "scene", "scene.cpp"),
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return string.Empty;
    }

    private void LoadGeneratedCodeForEditor()
    {
        var loadedCode = TryLoadGeneratedRuntimeCpp(PrototypeRoot);
        if (!string.IsNullOrWhiteSpace(loadedCode))
        {
            MonacoEditorContent = loadedCode;
            StatusMessage = "Code Mode loaded generated C++ source.";
            ShowToast("Loaded generated C++ into editor.");
            return;
        }

        StatusMessage = "Code Mode active. Generate a prototype to load runtime C++ files.";
        ShowToast("No generated runtime C++ found yet.");
    }

    private bool TryResolveEditableCodePath(out string codePath, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            codePath = string.Empty;
            errorMessage = "No generated prototype to save code into.";
            return false;
        }

        var candidatePaths = new[]
        {
            Path.Combine(PrototypeRoot, "generated", "cpp", "scene.cpp"),
            Path.Combine(PrototypeRoot, "generated", "cpp", "player_controller.cpp"),
            Path.Combine(PrototypeRoot, "generated", "cpp", "basic_npc.cpp"),
            Path.Combine(PrototypeRoot, "runtime", "main.cpp"),
            Path.Combine(PrototypeRoot, "runtime", "scene.cpp"),
            Path.Combine(PrototypeRoot, "scene.cpp"),
            Path.Combine(PrototypeRoot, "scene", "scene.cpp"),
        };

        codePath = candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
        Directory.CreateDirectory(Path.GetDirectoryName(codePath) ?? PrototypeRoot);
        errorMessage = string.Empty;
        return true;
    }

    private static string BuildCompileFailureMessage(string stage, string compileStdout, string compileStderr)
    {
        var stderr = string.IsNullOrWhiteSpace(compileStderr) ? "(none)" : compileStderr.Trim();
        var stdout = string.IsNullOrWhiteSpace(compileStdout) ? "(none)" : compileStdout.Trim();
        return $"{stage} failed. stderr: {stderr}{Environment.NewLine}stdout: {stdout}";
    }

    private static string BuildGeneratedEntityList(string prototypeRoot)
    {
        if (string.IsNullOrWhiteSpace(prototypeRoot) || prototypeRoot == "(none)")
        {
            return "No generated entities yet.";
        }

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        if (!File.Exists(scenePath))
        {
            return "scene/scene_scaffold.json not found.";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(scenePath));
            var root = document.RootElement;
            var lines = new List<string>();

            if (root.TryGetProperty("player_spawn", out _))
            {
                lines.Add("• player");
            }

            if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var id = entity.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    var type = entity.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "entity";
                    lines.Add($"• {type}:{id ?? "unknown"}");
                }
            }

            if (root.TryGetProperty("npcs", out var npcs) && npcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var npc in npcs.EnumerateArray())
                {
                    var id = npc.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    lines.Add($"• npc:{id ?? "unknown"}");
                }
            }

            if (root.TryGetProperty("camera", out _))
            {
                lines.Add("• camera");
            }

            if (lines.Count == 0)
            {
                return "No entities detected in scene scaffold.";
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Failed to parse entity preview: {ex.Message}";
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(processInfo);
        if (process is null)
        {
            return new ProcessResult(-1, string.Empty, $"{fileName} process failed to start.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static LaunchResult LaunchGeneratedRunner(string generatedBuildRoot)
    {
        var executablePath = ResolveGeneratedRunnerPath(generatedBuildRoot);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new LaunchResult(false, null, $"generated_gameplay_runner not found under {generatedBuildRoot}");
        }

        var launchInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(generatedBuildRoot) ?? generatedBuildRoot,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        var manifestPath = Path.Combine(Path.GetDirectoryName(generatedBuildRoot) ?? generatedBuildRoot, "..", "pipeline", "07_export_manifest.v1.json");
        var normalizedManifestPath = Path.GetFullPath(manifestPath);
        if (File.Exists(normalizedManifestPath))
        {
            launchInfo.ArgumentList.Add("--manifest");
            launchInfo.ArgumentList.Add(normalizedManifestPath);
        }

        try
        {
            var runtimeProcess = Process.Start(launchInfo);
            if (runtimeProcess is null)
            {
                return new LaunchResult(false, null, "generated_gameplay_runner process returned null.");
            }

            return new LaunchResult(true, runtimeProcess.Id, string.Empty);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, null, $"Failed to launch generated_gameplay_runner: {ex.Message}");
        }
    }

    private static string ResolveGeneratedRunnerPath(string generatedBuildRoot)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(generatedBuildRoot, "Debug", "generated_gameplay_runner.exe"),
                Path.Combine(generatedBuildRoot, "Release", "generated_gameplay_runner.exe"),
                Path.Combine(generatedBuildRoot, "generated_gameplay_runner.exe"),
            }
            : new[]
            {
                Path.Combine(generatedBuildRoot, "generated_gameplay_runner"),
            };

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

    private readonly record struct LaunchResult(bool Success, int? Pid, string ErrorMessage);

    private static string BuildStatusMessage(PipelineRunResponse response)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Pipeline exit code: {response.ExitCode}");

        if (response.Result is null)
        {
            builder.AppendLine("Unable to parse pipeline JSON output.");
            if (!string.IsNullOrWhiteSpace(response.Stderr))
            {
                builder.AppendLine(response.Stderr.Trim());
            }

            return builder.ToString().Trim();
        }

        builder.AppendLine($"Pipeline status: {response.Result.Status}");
        builder.AppendLine($"Prototype root: {response.Result.PrototypeRoot ?? "(none)"}");
        builder.AppendLine($"Runtime launch: {response.Result.RuntimeLaunchStatus}");

        if (response.Result.RuntimeLaunchPid is int pid)
        {
            builder.AppendLine($"Runtime PID: {pid}");
        }

        if (response.Result.DeadEndBlockers.Count > 0)
        {
            builder.AppendLine("Dead-end blockers:");
            foreach (var blocker in response.Result.DeadEndBlockers)
            {
                builder.AppendLine($"- {blocker}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response.Stderr))
        {
            builder.AppendLine("stderr:");
            builder.AppendLine(response.Stderr.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string BuildToastMessage(PipelineRunResponse response)
    {
        if (response.Result?.RuntimeLaunchPid is int pid)
        {
            return $"Runtime launched successfully — PID {pid}";
        }

        if (response.Result is not null)
        {
            return $"Pipeline {response.Result.Status}; runtime: {response.Result.RuntimeLaunchStatus}";
        }

        return response.ExitCode == 0
            ? "Pipeline completed."
            : "Pipeline failed. Open status details.";
    }

    private void ShowToast(string message)
    {
        StatusToastMessage = message;
        IsStatusToastVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
