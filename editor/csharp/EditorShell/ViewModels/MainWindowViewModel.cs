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

        var executableName = OperatingSystem.IsWindows() ? "prototype_runtime.exe" : "prototype_runtime";
        var executablePath = Path.Combine(PrototypeRoot, "runtime", executableName);
        var compileArguments = OperatingSystem.IsWindows()
            ? "-std=c++17 runtime/main.cpp -o runtime/prototype_runtime.exe"
            : "-std=c++17 runtime/main.cpp -o runtime/prototype_runtime";

        var compileInfo = new ProcessStartInfo
        {
            FileName = "g++",
            Arguments = compileArguments,
            WorkingDirectory = PrototypeRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        PipelineProgress = "Recompiling runtime";
        RuntimeLaunchStatus = "Recompiling";

        using var compileProcess = Process.Start(compileInfo);
        if (compileProcess is null)
        {
            StatusMessage = "Unable to start g++ compile process.";
            RuntimeLaunchStatus = "Compile failed";
            PipelineProgress = "Compile failed";
            ShowToast("Compile process did not start.");
            return;
        }

        var compileStdoutTask = compileProcess.StandardOutput.ReadToEndAsync(cancellationToken);
        var compileStderrTask = compileProcess.StandardError.ReadToEndAsync(cancellationToken);
        await compileProcess.WaitForExitAsync(cancellationToken);

        var compileStdout = await compileStdoutTask;
        var compileStderr = await compileStderrTask;
        if (compileProcess.ExitCode != 0)
        {
            RuntimeLaunchStatus = "Compile failed";
            PipelineProgress = "Compile failed";
            StatusMessage = BuildCompileFailureMessage(compileStdout, compileStderr);
            ShowToast("Compile failed. See status panel.");
            return;
        }

        var launchInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = PrototypeRoot,
            UseShellExecute = false,
        };

        var runtimeProcess = Process.Start(launchInfo);
        if (runtimeProcess is null)
        {
            RuntimeLaunchStatus = "Launch failed";
            PipelineProgress = "Launch failed";
            StatusMessage = $"Compile succeeded, but failed to launch {executableName}.";
            ShowToast("Runtime launch failed after compile.");
            return;
        }

        RuntimePid = runtimeProcess.Id;
        RuntimeLaunchStatus = "Running";
        RuntimePreviewSummary = $"Live in Vulkan (PID: {runtimeProcess.Id})";
        PipelineProgress = "Runtime relaunched from Code Mode";
        StatusMessage = $"Recompiled and relaunched runtime (PID: {runtimeProcess.Id}).";
        ShowToast("Save & Recompile completed.");
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

    private static string BuildCompileFailureMessage(string compileStdout, string compileStderr)
    {
        var stderr = string.IsNullOrWhiteSpace(compileStderr) ? "(none)" : compileStderr.Trim();
        var stdout = string.IsNullOrWhiteSpace(compileStdout) ? "(none)" : compileStdout.Trim();
        return $"Compile failed. stderr: {stderr}{Environment.NewLine}stdout: {stdout}";
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

            if (root.TryGetProperty("npcs", out var npcs) && npcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var npc in npcs.EnumerateArray())
                {
                    var id = npc.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    lines.Add($"• npc:{id ?? "unknown"}");
                }
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
