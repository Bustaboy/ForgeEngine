using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using GameForge.Editor.EditorShell.Services;
using System.Collections.Specialized;

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
    private int? _trackedRuntimePid;
    private ViewportEntity? _selectedViewportEntity;
    private string _selectedEntitySummary = "No entity selected.";
    private string _selectedEntityType = "n/a";
    private float _selectedEntityX;
    private float _selectedEntityY;
    private readonly Stack<SceneHistoryEntry> _undoStack = new();
    private readonly Stack<SceneHistoryEntry> _redoStack = new();
    private DragSession? _activeDragSession;

    public MainWindowViewModel()
    {
        AddPlayerEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("player"));
        AddNpcEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("npc"));
        AddPropEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("prop"));
        DeleteSelectedEntityCommand = new AsyncRelayCommand(DeleteSelectedEntityAsync);
        UndoCommand = new AsyncRelayCommand(UndoAsync);
        RedoCommand = new AsyncRelayCommand(RedoAsync);
        ViewportEntities.CollectionChanged += OnViewportEntitiesCollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ViewportEntity> ViewportEntities { get; } = new();

    public ICommand AddPlayerEntityCommand { get; }

    public ICommand AddNpcEntityCommand { get; }

    public ICommand AddPropEntityCommand { get; }

    public ICommand DeleteSelectedEntityCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

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

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

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

    public ViewportEntity? SelectedViewportEntity
    {
        get => _selectedViewportEntity;
        set
        {
            if (!SetField(ref _selectedViewportEntity, value))
            {
                return;
            }

            UpdateSelectedEntityInspector();
        }
    }

    public string SelectedEntitySummary
    {
        get => _selectedEntitySummary;
        private set => SetField(ref _selectedEntitySummary, value);
    }

    public string SelectedEntityType
    {
        get => _selectedEntityType;
        private set => SetField(ref _selectedEntityType, value);
    }

    public float SelectedEntityX
    {
        get => _selectedEntityX;
        private set => SetField(ref _selectedEntityX, value);
    }

    public float SelectedEntityY
    {
        get => _selectedEntityY;
        private set => SetField(ref _selectedEntityY, value);
    }

    public Task NewPrototypeAsync(CancellationToken cancellationToken = default)
    {
        ChatPrompt = string.Empty;
        _lastBriefPath = null;
        RuntimePid = null;
        _trackedRuntimePid = null;
        RuntimeLaunchStatus = "Not launched";
        PipelineProgress = "Idle";
        PrototypeRoot = "(none)";
        RuntimePreviewSummary = "Generate & Play to populate runtime preview.";
        RuntimeEntityList = "No generated entities yet.";
        ViewportEntities.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        _activeDragSession = null;
        NotifyHistoryChanged();
        SelectedViewportEntity = null;
        SelectedEntitySummary = "No entity selected.";
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
        await StopPreviousRuntimeIfRunningAsync(cancellationToken);

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
        await StopPreviousRuntimeIfRunningAsync(cancellationToken);
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
        _trackedRuntimePid = launchResult.Pid;
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
        _trackedRuntimePid = RuntimePid;
        PrototypeRoot = response.Result?.PrototypeRoot ?? "(none)";

        RuntimePreviewSummary = RuntimePid is int pid
            ? $"Live in Vulkan (PID: {pid})"
            : $"Runtime status: {RuntimeLaunchStatus}";

        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);
        LoadViewportEntitiesFromScene(PrototypeRoot);
        _undoStack.Clear();
        _redoStack.Clear();
        _activeDragSession = null;
        NotifyHistoryChanged();

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

    private void LoadViewportEntitiesFromScene(string prototypeRoot)
    {
        ViewportEntities.Clear();
        SelectedViewportEntity = null;

        if (string.IsNullOrWhiteSpace(prototypeRoot) || prototypeRoot == "(none)")
        {
            return;
        }

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        if (!File.Exists(scenePath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(scenePath));
            var root = document.RootElement;

            if (root.TryGetProperty("player_spawn", out var playerSpawn))
            {
                var playerX = ReadCoordinate(playerSpawn, "x");
                var playerY = ReadCoordinate(playerSpawn, "y");
                ViewportEntities.Add(new ViewportEntity("player_spawn", "player", playerX, playerY));
            }

            if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var id = entity.TryGetProperty("id", out var idElement) ? idElement.GetString() : "entity";
                    var type = entity.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "entity";
                    var x = ReadCoordinate(entity, "x");
                    var y = ReadCoordinate(entity, "y");
                    ViewportEntities.Add(new ViewportEntity(id ?? "entity", type ?? "entity", x, y));
                }
            }

            if (root.TryGetProperty("npcs", out var npcs) && npcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var npc in npcs.EnumerateArray())
                {
                    var id = npc.TryGetProperty("id", out var idElement) ? idElement.GetString() : "npc";
                    var x = ReadCoordinate(npc, "spawn_x");
                    var y = ReadCoordinate(npc, "spawn_y");
                    ViewportEntities.Add(new ViewportEntity(id ?? "npc", "npc", x, y));
                }
            }

            if (ViewportEntities.Count > 0)
            {
                SelectedViewportEntity = ViewportEntities[0];
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Viewport entity load failed: {ex.Message}";
            ShowToast("Viewport entity load failed.");
        }
    }

    private async Task AddEntityAndRelaunchAsync(string entityKind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            StatusMessage = "Generate a prototype before adding entities.";
            ShowToast("Generate prototype first.");
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = $"Scene scaffold not found for prototype: {PrototypeRoot}";
            ShowToast("scene_scaffold.json missing.");
            return;
        }

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            using var document = JsonDocument.Parse(beforeContent);
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                document.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, object?>();

            var entities = new List<Dictionary<string, object?>>();
            if (document.RootElement.TryGetProperty("entities", out var existingEntities) && existingEntities.ValueKind == JsonValueKind.Array)
            {
                foreach (var existingEntity in existingEntities.EnumerateArray())
                {
                    entities.Add(JsonSerializer.Deserialize<Dictionary<string, object?>>(existingEntity.GetRawText()) ?? new Dictionary<string, object?>());
                }
            }

            var nextIndex = entities.Count + 1;
            entities.Add(new Dictionary<string, object?>
            {
                ["id"] = $"{entityKind}_{nextIndex:00}",
                ["type"] = entityKind,
                ["x"] = 0,
                ["y"] = 0,
                ["z"] = 0,
            });

            payload["entities"] = entities;
            var updatedScene = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, updatedScene, $"Entity added: {entityKind}_{nextIndex:00}", cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add entity failed: {ex.Message}";
            ShowToast("Add entity failed.");
        }
    }

    public bool BeginDragForEntity(string entityId)
    {
        var entity = ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return false;
        }

        SelectedViewportEntity = entity;
        _activeDragSession = new DragSession(entity.Id, entity.X, entity.Y);
        return true;
    }

    public bool PreviewDragPosition(string entityId, float nextX, float nextY)
    {
        if (_activeDragSession is null || !string.Equals(_activeDragSession.EntityId, entityId, StringComparison.Ordinal))
        {
            return false;
        }

        var entity = ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return false;
        }

        entity.SetPosition(nextX, nextY);
        if (SelectedViewportEntity?.Id == entity.Id)
        {
            UpdateSelectedEntityInspector();
        }

        return true;
    }

    public async Task CommitDragAsync(CancellationToken cancellationToken = default)
    {
        if (_activeDragSession is null)
        {
            return;
        }

        var dragSession = _activeDragSession;
        _activeDragSession = null;

        var entity = ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, dragSession.EntityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return;
        }

        var deltaX = entity.X - dragSession.StartX;
        var deltaY = entity.Y - dragSession.StartY;
        if (Math.Abs(deltaX) < 0.0001f && Math.Abs(deltaY) < 0.0001f)
        {
            return;
        }

        await MoveEntityAsync(entity, deltaX, deltaY, "Entity moved", cancellationToken);
    }

    private async Task MoveEntityAsync(ViewportEntity entity, float deltaX, float deltaY, string operationLabel, CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before moving entities.";
            ShowToast("Generate prototype first.");
            return;
        }

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                StatusMessage = "Scene scaffold parse failed.";
                ShowToast("Scene parse failed.");
                return;
            }

            if (!TryMoveEntityInScene(root, entity, deltaX, deltaY))
            {
                ShowToast("Selected entity not found in scene.");
                return;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, $"{operationLabel}: {entity.DisplayName}", cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Move entity failed: {ex.Message}";
            ShowToast("Entity move failed.");
        }
    }

    private void UpdateSelectedEntityInspector()
    {
        if (SelectedViewportEntity is null)
        {
            SelectedEntitySummary = "No entity selected.";
            SelectedEntityType = "n/a";
            SelectedEntityX = 0;
            SelectedEntityY = 0;
            return;
        }

        SelectedEntityType = SelectedViewportEntity.Type;
        SelectedEntityX = SelectedViewportEntity.X;
        SelectedEntityY = SelectedViewportEntity.Y;
        SelectedEntitySummary = $"{SelectedViewportEntity.DisplayName} ({SelectedViewportEntity.Type})";
    }

    private async Task DeleteSelectedEntityAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedViewportEntity is null)
        {
            ShowToast("Select an entity first.");
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before deleting entities.";
            ShowToast("Generate prototype first.");
            return;
        }

        try
        {
            var target = SelectedViewportEntity;
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                StatusMessage = "Scene scaffold parse failed.";
                ShowToast("Scene parse failed.");
                return;
            }

            if (!TryDeleteEntityInScene(root, target))
            {
                ShowToast("Selected entity not found in scene.");
                return;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, $"Entity deleted: {target.DisplayName}", cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete entity failed: {ex.Message}";
            ShowToast("Entity delete failed.");
        }
    }

    private async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_undoStack.Count == 0)
        {
            ShowToast("Nothing to undo.");
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            ShowToast("No scene scaffold available.");
            return;
        }

        var history = _undoStack.Pop();
        var currentContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
        _redoStack.Push(new SceneHistoryEntry(currentContent, history.Description));
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, history.Content, $"Undo: {history.Description}", cancellationToken);
    }

    private async Task RedoAsync(CancellationToken cancellationToken = default)
    {
        if (_redoStack.Count == 0)
        {
            ShowToast("Nothing to redo.");
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            ShowToast("No scene scaffold available.");
            return;
        }

        var history = _redoStack.Pop();
        var currentContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
        _undoStack.Push(new SceneHistoryEntry(currentContent, history.Description));
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, history.Content, $"Redo: {history.Description}", cancellationToken);
    }

    private string? GetScenePath()
    {
        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            return null;
        }

        var scenePath = Path.Combine(PrototypeRoot, "scene", "scene_scaffold.json");
        return File.Exists(scenePath) ? scenePath : null;
    }

    private async Task WriteSceneAndRelaunchAsync(string scenePath, string beforeContent, string afterContent, string operationDescription, CancellationToken cancellationToken)
    {
        if (string.Equals(beforeContent, afterContent, StringComparison.Ordinal))
        {
            return;
        }

        _undoStack.Push(new SceneHistoryEntry(beforeContent, operationDescription));
        _redoStack.Clear();
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, afterContent, operationDescription, cancellationToken);
    }

    private async Task ApplySceneContentAndRelaunchAsync(string scenePath, string content, string operationDescription, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(scenePath, content, cancellationToken);
        StatusMessage = $"{operationDescription}. Recompiling and relaunching runtime...";
        ShowToast(operationDescription);
        await StopPreviousRuntimeIfRunningAsync(cancellationToken);
        await RecompileAndRelaunchRuntimeAsync(cancellationToken);
        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);
        LoadViewportEntitiesFromScene(PrototypeRoot);
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static float ReadCoordinate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0f;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetSingle(out var number) ? number : 0f,
            _ => 0f,
        };
    }

    private static bool TryMoveEntityInScene(JsonObject root, ViewportEntity selected, float deltaX, float deltaY)
    {
        if (selected.Type == "player")
        {
            var playerSpawn = root["player_spawn"] as JsonObject;
            if (playerSpawn is null)
            {
                return false;
            }

            var currentX = ReadNodeFloat(playerSpawn["x"]);
            var currentY = ReadNodeFloat(playerSpawn["y"]);
            playerSpawn["x"] = currentX + deltaX;
            playerSpawn["y"] = currentY + deltaY;
            return true;
        }

        if (root["entities"] is JsonArray entities)
        {
            foreach (var node in entities)
            {
                if (node is not JsonObject entityObject)
                {
                    continue;
                }

                var id = entityObject["id"]?.GetValue<string>();
                if (!string.Equals(id, selected.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var currentX = ReadNodeFloat(entityObject["x"]);
                var currentY = ReadNodeFloat(entityObject["y"]);
                entityObject["x"] = currentX + deltaX;
                entityObject["y"] = currentY + deltaY;
                return true;
            }
        }

        if (root["npcs"] is JsonArray npcs)
        {
            foreach (var node in npcs)
            {
                if (node is not JsonObject npcObject)
                {
                    continue;
                }

                var id = npcObject["id"]?.GetValue<string>();
                if (!string.Equals(id, selected.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var currentX = ReadNodeFloat(npcObject["spawn_x"]);
                var currentY = ReadNodeFloat(npcObject["spawn_y"]);
                npcObject["spawn_x"] = currentX + deltaX;
                npcObject["spawn_y"] = currentY + deltaY;
                return true;
            }
        }

        return false;
    }

    private static bool TryDeleteEntityInScene(JsonObject root, ViewportEntity selected)
    {
        if (selected.Type == "player")
        {
            return false;
        }

        if (root["entities"] is JsonArray entities)
        {
            for (var index = 0; index < entities.Count; index++)
            {
                if (entities[index] is not JsonObject entityObject)
                {
                    continue;
                }

                var id = entityObject["id"]?.GetValue<string>();
                if (!string.Equals(id, selected.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                entities.RemoveAt(index);
                return true;
            }
        }

        if (root["npcs"] is JsonArray npcs)
        {
            for (var index = 0; index < npcs.Count; index++)
            {
                if (npcs[index] is not JsonObject npcObject)
                {
                    continue;
                }

                var id = npcObject["id"]?.GetValue<string>();
                if (!string.Equals(id, selected.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                npcs.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    private void OnViewportEntitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ViewportEntity>())
            {
                item.PropertyChanged += OnViewportEntityPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ViewportEntity>())
            {
                item.PropertyChanged -= OnViewportEntityPropertyChanged;
            }
        }
    }

    private void OnViewportEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ViewportEntity entity)
        {
            return;
        }

        if (SelectedViewportEntity?.Id == entity.Id && (e.PropertyName == nameof(ViewportEntity.X) || e.PropertyName == nameof(ViewportEntity.Y)))
        {
            UpdateSelectedEntityInspector();
        }
    }

    private static float ReadNodeFloat(JsonNode? node)
    {
        if (node is null)
        {
            return 0f;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<float>(out var floatValue))
            {
                return floatValue;
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return (float)doubleValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }
        }

        return 0f;
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

    private async Task StopPreviousRuntimeIfRunningAsync(CancellationToken cancellationToken)
    {
        var candidatePid = _trackedRuntimePid ?? RuntimePid;
        if (candidatePid is not int pid || pid <= 0)
        {
            return;
        }

        var stopResult = await TryStopRuntimeProcessAsync(pid, cancellationToken);
        if (stopResult.Stopped)
        {
            RuntimeLaunchStatus = "Previous runtime stopped";
            StatusMessage = $"Previous runtime stopped (PID: {pid}).";
            ShowToast("Previous runtime stopped.");
            RuntimePid = null;
            _trackedRuntimePid = null;
            RuntimePreviewSummary = "Runtime stopped. Relaunching generated runtime...";
            return;
        }

        RuntimeLaunchStatus = "Previous runtime cleanup failed";
        StatusMessage = $"Unable to stop previous runtime PID {pid}: {stopResult.ErrorMessage}";
        ShowToast("Previous runtime cleanup failed; continuing with relaunch.");
    }

    private async Task<RuntimeStopResult> TryStopRuntimeProcessAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                    {
                        return new RuntimeStopResult(true, string.Empty);
                    }

                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                    return new RuntimeStopResult(true, string.Empty);
                }
                catch (ArgumentException)
                {
                    return new RuntimeStopResult(true, string.Empty);
                }
            }

            var killResult = await RunProcessAsync("kill", $"-9 {pid}", workingDirectory: PrototypeRoot == "(none)" ? Environment.CurrentDirectory : PrototypeRoot, cancellationToken);
            if (killResult.ExitCode == 0)
            {
                return new RuntimeStopResult(true, string.Empty);
            }

            try
            {
                var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return new RuntimeStopResult(true, string.Empty);
                }

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
                return new RuntimeStopResult(true, string.Empty);
            }
            catch (ArgumentException)
            {
                return new RuntimeStopResult(true, string.Empty);
            }
            catch (Exception fallbackEx)
            {
                var fallbackMessage = string.IsNullOrWhiteSpace(killResult.Stderr)
                    ? fallbackEx.Message
                    : $"{killResult.Stderr.Trim()} | fallback: {fallbackEx.Message}";
                return new RuntimeStopResult(false, fallbackMessage);
            }

        }
        catch (Exception ex)
        {
            return new RuntimeStopResult(false, ex.Message);
        }
    }

    private readonly record struct RuntimeStopResult(bool Stopped, string ErrorMessage);

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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly record struct DragSession(string EntityId, float StartX, float StartY);

    private readonly record struct SceneHistoryEntry(string Content, string Description);

    public sealed class ViewportEntity : INotifyPropertyChanged
    {
        private float _x;
        private float _y;

        public ViewportEntity(string id, string type, float x, float y)
        {
            Id = id;
            Type = type;
            _x = x;
            _y = y;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string Type { get; }

        public float X
        {
            get => _x;
            private set
            {
                if (Math.Abs(_x - value) < 0.0001f)
                {
                    return;
                }

                _x = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(X)));
            }
        }

        public float Y
        {
            get => _y;
            private set
            {
                if (Math.Abs(_y - value) < 0.0001f)
                {
                    return;
                }

                _y = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Y)));
            }
        }

        public void SetPosition(float x, float y)
        {
            X = x;
            Y = y;
        }

        public string DisplayName => $"{Type}:{Id}";
    }

    private sealed class AsyncRelayCommand(Func<Task> executeAsync) : ICommand
    {
        private readonly Func<Task> _executeAsync = executeAsync;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter)
        {
            await _executeAsync();
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
