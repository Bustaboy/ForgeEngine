using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using GameForge.Editor.EditorShell.Services;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IOrchestratorGateway _orchestratorGateway;
    private readonly IRuntimeSupervisor _runtimeSupervisor;

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
    private readonly ObservableCollection<ViewportEntity> _selectedViewportEntities = new();
    private readonly ReadOnlyObservableCollection<ViewportEntity> _readonlySelectedViewportEntities;
    private string _selectedEntitySummary = "No entity selected.";
    private string _selectedEntityType = "n/a";
    private float _selectedEntityX;
    private float _selectedEntityY;
    private string _selectedEntityNameEditor = string.Empty;
    private string _selectedEntityPositionXEditor = "0";
    private string _selectedEntityPositionYEditor = "0";
    private string _selectedEntityScaleEditor = "1";
    private string _selectedEntityColorEditor = "#4AA3FF";
    private string _runtimeSelectedEntityPreview = "No active selection.";
    private bool _isRefreshingSelectionEditors;
    private readonly Stack<SceneHistoryEntry> _undoStack = new();
    private readonly Stack<SceneHistoryEntry> _redoStack = new();
    private SceneHistoryEntry? _currentSceneHistoryEntry;
    private int _nextHistoryRevision = 1;
    private readonly ObservableCollection<HistoryTimelineEntry> _historyTimeline = new();
    private readonly ReadOnlyObservableCollection<HistoryTimelineEntry> _readonlyHistoryTimeline;
    private readonly ObservableCollection<HierarchyNode> _hierarchyRoots = new();
    private readonly ReadOnlyObservableCollection<HierarchyNode> _readonlyHierarchyRoots;
    private HierarchyNode? _selectedHierarchyNode;
    private DragSession? _activeDragSession;
    private string _selectedEntityAssetName = "No asset linked.";
    private string _selectedEntityAssetPreviewPath = string.Empty;
    private string _selectedEntityAssetKind = "n/a";
    private ImportedAsset? _selectedImportedAsset;

    public MainWindowViewModel()
        : this(new OrchestratorGateway(), new RuntimeSupervisor())
    {
    }

    internal MainWindowViewModel(IOrchestratorGateway orchestratorGateway, IRuntimeSupervisor runtimeSupervisor)
    {
        _orchestratorGateway = orchestratorGateway;
        _runtimeSupervisor = runtimeSupervisor;
        _readonlySelectedViewportEntities = new ReadOnlyObservableCollection<ViewportEntity>(_selectedViewportEntities);
        _readonlyHistoryTimeline = new ReadOnlyObservableCollection<HistoryTimelineEntry>(_historyTimeline);
        _readonlyHierarchyRoots = new ReadOnlyObservableCollection<HierarchyNode>(_hierarchyRoots);
        AddPlayerEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("player"));
        AddNpcEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("npc"));
        AddPropEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("prop"));
        DeleteSelectedEntityCommand = new AsyncRelayCommand(() => DeleteSelectedEntityAsync());
        ApplySelectionPropertiesCommand = new AsyncRelayCommand(() => ApplySelectionPropertiesAsync());
        UndoCommand = new AsyncRelayCommand(() => UndoAsync());
        RedoCommand = new AsyncRelayCommand(() => RedoAsync());
        JumpToHistoryCommand = new AsyncRelayCommand<object?>(JumpToHistoryAsync);
        ViewportEntities.CollectionChanged += OnViewportEntitiesCollectionChanged;
        _selectedViewportEntities.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSingleSelection));
            OnPropertyChanged(nameof(SelectedEntitiesCount));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ViewportEntity> ViewportEntities { get; } = new();

    public ObservableCollection<ImportedAsset> ImportedAssets { get; } = new();

    public ReadOnlyObservableCollection<HierarchyNode> HierarchyRoots => _readonlyHierarchyRoots;

    public ICommand AddPlayerEntityCommand { get; }

    public ICommand AddNpcEntityCommand { get; }

    public ICommand AddPropEntityCommand { get; }

    public ICommand DeleteSelectedEntityCommand { get; }

    public ICommand ApplySelectionPropertiesCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand JumpToHistoryCommand { get; }

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

    public ReadOnlyObservableCollection<HistoryTimelineEntry> HistoryTimeline => _readonlyHistoryTimeline;

    public HierarchyNode? SelectedHierarchyNode
    {
        get => _selectedHierarchyNode;
        set
        {
            if (!SetField(ref _selectedHierarchyNode, value) || value is null || !value.IsEntityNode || value.EntityId is null)
            {
                return;
            }

            SelectSingleEntity(value.EntityId);
        }
    }

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

    public ReadOnlyObservableCollection<ViewportEntity> SelectedViewportEntities => _readonlySelectedViewportEntities;

    public bool HasSelection => _selectedViewportEntities.Count > 0;

    public bool IsSingleSelection => _selectedViewportEntities.Count == 1;

    public int SelectedEntitiesCount => _selectedViewportEntities.Count;

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

    public string SelectedEntityNameEditor
    {
        get => _selectedEntityNameEditor;
        set
        {
            if (!SetField(ref _selectedEntityNameEditor, value) || _isRefreshingSelectionEditors)
            {
                return;
            }

            foreach (var entity in _selectedViewportEntities)
            {
                entity.SetName(value);
            }

            RefreshInspectorDerivedState();
        }
    }

    public string SelectedEntityPositionXEditor
    {
        get => _selectedEntityPositionXEditor;
        set
        {
            if (!SetField(ref _selectedEntityPositionXEditor, value) || _isRefreshingSelectionEditors)
            {
                return;
            }

            if (TryParseFloat(value, out var parsed))
            {
                foreach (var entity in _selectedViewportEntities)
                {
                    entity.SetPosition(parsed, entity.Y);
                }

                RefreshInspectorDerivedState();
            }
        }
    }

    public string SelectedEntityPositionYEditor
    {
        get => _selectedEntityPositionYEditor;
        set
        {
            if (!SetField(ref _selectedEntityPositionYEditor, value) || _isRefreshingSelectionEditors)
            {
                return;
            }

            if (TryParseFloat(value, out var parsed))
            {
                foreach (var entity in _selectedViewportEntities)
                {
                    entity.SetPosition(entity.X, parsed);
                }

                RefreshInspectorDerivedState();
            }
        }
    }

    public string SelectedEntityScaleEditor
    {
        get => _selectedEntityScaleEditor;
        set
        {
            if (!SetField(ref _selectedEntityScaleEditor, value) || _isRefreshingSelectionEditors)
            {
                return;
            }

            if (TryParseFloat(value, out var parsed))
            {
                var clamped = Math.Max(0.1f, parsed);
                foreach (var entity in _selectedViewportEntities)
                {
                    entity.SetScale(clamped);
                }

                RefreshInspectorDerivedState();
            }
        }
    }

    public string SelectedEntityColorEditor
    {
        get => _selectedEntityColorEditor;
        set
        {
            if (!SetField(ref _selectedEntityColorEditor, value) || _isRefreshingSelectionEditors)
            {
                return;
            }

            var sanitized = SanitizeColorHex(value);
            if (sanitized is null)
            {
                return;
            }

            foreach (var entity in _selectedViewportEntities)
            {
                entity.SetColorHex(sanitized);
            }

            RefreshInspectorDerivedState();
        }
    }

    public string RuntimeSelectedEntityPreview
    {
        get => _runtimeSelectedEntityPreview;
        private set => SetField(ref _runtimeSelectedEntityPreview, value);
    }

    public ImportedAsset? SelectedImportedAsset
    {
        get => _selectedImportedAsset;
        set
        {
            if (!SetField(ref _selectedImportedAsset, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedImportedAssetName));
            OnPropertyChanged(nameof(SelectedImportedAssetPreviewPath));
            OnPropertyChanged(nameof(SelectedImportedAssetKind));
        }
    }

    public string SelectedImportedAssetName => SelectedImportedAsset?.DisplayName ?? "Nothing selected";

    public string SelectedImportedAssetPreviewPath => SelectedImportedAsset?.PreviewPath ?? string.Empty;

    public string SelectedImportedAssetKind => SelectedImportedAsset?.Kind ?? "n/a";

    public string SelectedEntityAssetName
    {
        get => _selectedEntityAssetName;
        private set => SetField(ref _selectedEntityAssetName, value);
    }

    public string SelectedEntityAssetPreviewPath
    {
        get => _selectedEntityAssetPreviewPath;
        private set => SetField(ref _selectedEntityAssetPreviewPath, value);
    }

    public string SelectedEntityAssetKind
    {
        get => _selectedEntityAssetKind;
        private set => SetField(ref _selectedEntityAssetKind, value);
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
        _hierarchyRoots.Clear();
        ImportedAssets.Clear();
        _selectedViewportEntities.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        _currentSceneHistoryEntry = null;
        _nextHistoryRevision = 1;
        _activeDragSession = null;
        NotifyHistoryChanged();
        SelectedViewportEntity = null;
        SelectedHierarchyNode = null;
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

        _lastBriefPath = _orchestratorGateway.CreateBriefFromChatPrompt(ChatPrompt);
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
        var configureResult = await _runtimeSupervisor.RunProcessAsync(
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

        var buildResult = await _runtimeSupervisor.RunProcessAsync(
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
        var launchResult = _runtimeSupervisor.LaunchGeneratedRunner(generatedBuildRoot);
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
            var response = await _orchestratorGateway.RunGenerationPipelineAsync(briefPath, launchRuntime, cancellationToken);
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
        var scenePath = GetScenePath();
        _currentSceneHistoryEntry = scenePath is not null && File.Exists(scenePath)
            ? new SceneHistoryEntry(File.ReadAllText(scenePath), "Generated scene loaded", _nextHistoryRevision++)
            : null;
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
        ImportedAssets.Clear();
        _selectedViewportEntities.Clear();
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
            LoadImportedAssets(root);

            if (root.TryGetProperty("player_spawn", out var playerSpawn))
            {
                var playerX = ReadCoordinate(playerSpawn, "x");
                var playerY = ReadCoordinate(playerSpawn, "y");
                ViewportEntities.Add(new ViewportEntity("player_spawn", "player", "Player Spawn", playerX, playerY, 1f, "#4AA3FF"));
            }

            if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var id = entity.TryGetProperty("id", out var idElement) ? idElement.GetString() : "entity";
                    var type = entity.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "entity";
                    var x = ReadCoordinate(entity, "x");
                    var y = ReadCoordinate(entity, "y");
                    var name = entity.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    var scale = ReadCoordinate(entity, "scale");
                    var color = entity.TryGetProperty("color", out var colorElement) ? colorElement.GetString() : null;
                    ViewportEntities.Add(new ViewportEntity(
                        id ?? "entity",
                        type ?? "entity",
                        name ?? $"{type ?? "entity"}:{id ?? "entity"}",
                        x,
                        y,
                        scale <= 0f ? 1f : scale,
                        SanitizeColorHex(color ?? string.Empty) ?? DefaultColorForType(type ?? "entity"),
                        entity.TryGetProperty("parent_id", out var parentId) ? parentId.GetString() : null,
                        entity.TryGetProperty("asset_id", out var assetId) ? assetId.GetString() : null,
                        entity.TryGetProperty("asset_path", out var assetPath) ? assetPath.GetString() : null,
                        entity.TryGetProperty("asset_kind", out var assetKind) ? assetKind.GetString() : null));
                }
            }

            if (root.TryGetProperty("npcs", out var npcs) && npcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var npc in npcs.EnumerateArray())
                {
                    var id = npc.TryGetProperty("id", out var idElement) ? idElement.GetString() : "npc";
                    var x = ReadCoordinate(npc, "spawn_x");
                    var y = ReadCoordinate(npc, "spawn_y");
                    var name = npc.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    var scale = ReadCoordinate(npc, "scale");
                    var color = npc.TryGetProperty("color", out var colorElement) ? colorElement.GetString() : null;
                    ViewportEntities.Add(new ViewportEntity(
                        id ?? "npc",
                        "npc",
                        name ?? $"npc:{id ?? "npc"}",
                        x,
                        y,
                        scale <= 0f ? 1f : scale,
                        SanitizeColorHex(color ?? string.Empty) ?? DefaultColorForType("npc"),
                        npc.TryGetProperty("parent_id", out var parentId) ? parentId.GetString() : null));
                }
            }

            RebuildHierarchyTree();
            if (ViewportEntities.Count > 0)
            {
                ReplaceSelection(new[] { ViewportEntities[0] });
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
                ["name"] = $"{entityKind}_{nextIndex:00}",
                ["x"] = 0,
                ["y"] = 0,
                ["z"] = 0,
                ["scale"] = 1,
                ["color"] = DefaultColorForType(entityKind),
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

        if (!_selectedViewportEntities.Any(item => item.Id == entity.Id))
        {
            ReplaceSelection(new[] { entity });
        }

        SelectedViewportEntity = _selectedViewportEntities[0];
        _activeDragSession = new DragSession(
            entity.Id,
            _selectedViewportEntities
                .Select(item => new DragEntitySnapshot(item.Id, item.X, item.Y))
                .ToList());
        return true;
    }

    public bool SelectSingleEntity(string entityId)
    {
        var entity = ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return false;
        }

        ReplaceSelection(new[] { entity });
        return true;
    }

    public bool ToggleEntitySelection(string entityId)
    {
        var entity = ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return false;
        }

        if (_selectedViewportEntities.Any(item => item.Id == entity.Id))
        {
            RemoveSelection(entity);
            return true;
        }

        AddSelection(entity);
        return true;
    }

    public void SelectEntitiesByViewportRect(float minX, float minY, float maxX, float maxY, bool appendToSelection)
    {
        var hits = ViewportEntities
            .Where(entity => entity.X >= minX && entity.X <= maxX && entity.Y >= minY && entity.Y <= maxY)
            .ToList();

        if (!appendToSelection)
        {
            ReplaceSelection(hits);
            return;
        }

        foreach (var hit in hits)
        {
            if (_selectedViewportEntities.Any(entity => entity.Id == hit.Id))
            {
                continue;
            }

            AddSelection(hit);
        }
    }

    public void ClearSelection()
    {
        ReplaceSelection(Array.Empty<ViewportEntity>());
    }

    public async Task<bool> ImportAssetAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            ShowToast("Selected asset file does not exist.");
            return false;
        }

        if (!TryGetSupportedAssetKind(sourcePath, out var assetKind))
        {
            StatusMessage = "Only PNG textures and OBJ models are supported in V1.";
            ShowToast("Unsupported file format.");
            return false;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before importing assets.";
            ShowToast("Generate prototype first.");
            return false;
        }

        var normalizedPath = Path.GetFullPath(sourcePath);

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                ShowToast("Scene parse failed.");
                return false;
            }

            var importedAssets = root["imported_assets"] as JsonArray ?? new JsonArray();
            root["imported_assets"] = importedAssets;
            var existing = importedAssets
                .OfType<JsonObject>()
                .FirstOrDefault(node => string.Equals(node["path"]?.GetValue<string>(), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var nextId = BuildNextImportedAssetId(importedAssets);
                importedAssets.Add(new JsonObject
                {
                    ["id"] = nextId,
                    ["name"] = Path.GetFileNameWithoutExtension(normalizedPath),
                    ["kind"] = assetKind,
                    ["path"] = normalizedPath,
                    ["color"] = assetKind == ImportedAssetKind.Texture ? "#7FD1FF" : "#F2C56E",
                });
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, $"Imported asset: {Path.GetFileName(normalizedPath)}", cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Asset import failed: {ex.Message}";
            ShowToast("Asset import failed.");
            return false;
        }
    }

    public async Task<bool> PlaceImportedAssetInSceneAsync(string assetId, float x, float y, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return false;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before placing imported assets.";
            ShowToast("Generate prototype first.");
            return false;
        }

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                ShowToast("Scene parse failed.");
                return false;
            }

            var importedAssetNode = (root["imported_assets"] as JsonArray)?
                .OfType<JsonObject>()
                .FirstOrDefault(node => string.Equals(node["id"]?.GetValue<string>(), assetId, StringComparison.Ordinal));
            if (importedAssetNode is null)
            {
                ShowToast("Imported asset not found.");
                return false;
            }

            var entities = root["entities"] as JsonArray ?? new JsonArray();
            root["entities"] = entities;
            var entityId = BuildNextEntityIdFromAssets(entities, assetId);
            var assetKind = importedAssetNode["kind"]?.GetValue<string>() ?? ImportedAssetKind.Texture;
            var colorHex = SanitizeColorHex(importedAssetNode["color"]?.GetValue<string>() ?? string.Empty) ?? DefaultColorForType("prop");
            entities.Add(new JsonObject
            {
                ["id"] = entityId,
                ["type"] = "prop",
                ["name"] = $"asset:{importedAssetNode["name"]?.GetValue<string>() ?? assetId}",
                ["x"] = x,
                ["y"] = y,
                ["z"] = 0,
                ["scale"] = 1,
                ["color"] = colorHex,
                ["asset_id"] = assetId,
                ["asset_kind"] = assetKind,
                ["asset_path"] = importedAssetNode["path"]?.GetValue<string>() ?? string.Empty,
            });

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, $"Placed imported asset: {importedAssetNode["name"]?.GetValue<string>() ?? assetId}", cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Asset placement failed: {ex.Message}";
            ShowToast("Asset placement failed.");
            return false;
        }
    }

    public bool PreviewDragPosition(string entityId, float nextX, float nextY)
    {
        if (_activeDragSession is null)
        {
            return false;
        }

        var activeDrag = _activeDragSession.Value;
        if (!string.Equals(activeDrag.EntityId, entityId, StringComparison.Ordinal))
        {
            return false;
        }

        var entity = _selectedViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return false;
        }

        var anchor = activeDrag.Entities.FirstOrDefault(item => item.EntityId == entityId);
        var deltaX = nextX - anchor.StartX;
        var deltaY = nextY - anchor.StartY;

        foreach (var dragged in _selectedViewportEntities)
        {
            var origin = activeDrag.Entities.FirstOrDefault(item => item.EntityId == dragged.Id);
            dragged.SetPosition(origin.StartX + deltaX, origin.StartY + deltaY);
        }

        RefreshInspectorDerivedState();
        return true;
    }

    public async Task CommitDragAsync(CancellationToken cancellationToken = default)
    {
        if (_activeDragSession is null)
        {
            return;
        }

        var dragSession = _activeDragSession.Value;
        _activeDragSession = null;

        var entity = _selectedViewportEntities.FirstOrDefault(item => string.Equals(item.Id, dragSession.EntityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return;
        }

        var anchor = dragSession.Entities.FirstOrDefault(item => item.EntityId == dragSession.EntityId);
        var deltaX = entity.X - anchor.StartX;
        var deltaY = entity.Y - anchor.StartY;
        if (Math.Abs(deltaX) < 0.0001f && Math.Abs(deltaY) < 0.0001f)
        {
            return;
        }

        await MoveEntitiesAsync(_selectedViewportEntities.ToList(), deltaX, deltaY, "Entity moved", cancellationToken);
    }

    private async Task MoveEntitiesAsync(IReadOnlyList<ViewportEntity> entities, float deltaX, float deltaY, string operationLabel, CancellationToken cancellationToken = default)
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

            var movedAny = false;
            foreach (var entity in entities)
            {
                movedAny = TryMoveEntityInScene(root, entity, deltaX, deltaY) || movedAny;
            }

            if (!movedAny)
            {
                ShowToast("Selected entity not found in scene.");
                return;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var operationTarget = entities.Count == 1 ? entities[0].DisplayName : $"{entities.Count} entities";
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, $"{operationLabel}: {operationTarget}", cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Move entity failed: {ex.Message}";
            ShowToast("Entity move failed.");
        }
    }

    public async Task<bool> ReparentEntityAsync(string sourceEntityId, string? targetEntityId, CancellationToken cancellationToken = default)
    {
        var source = ViewportEntities.FirstOrDefault(entity => string.Equals(entity.Id, sourceEntityId, StringComparison.Ordinal));
        if (source is null || source.Type == "player")
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(targetEntityId))
        {
            var target = ViewportEntities.FirstOrDefault(entity => string.Equals(entity.Id, targetEntityId, StringComparison.Ordinal));
            if (target is null || target.Id == source.Id || target.Type == "player")
            {
                return false;
            }

            if (IsDescendantOf(target.Id, source.Id))
            {
                ShowToast("Reparent blocked: cyclic hierarchy.");
                return false;
            }
        }

        if (string.Equals(source.ParentId ?? string.Empty, targetEntityId ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            ShowToast("Generate prototype first.");
            return false;
        }

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                ShowToast("Scene parse failed.");
                return false;
            }

            if (!TrySetEntityParentInScene(root, sourceEntityId, targetEntityId))
            {
                ShowToast("Hierarchy update failed.");
                return false;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var label = string.IsNullOrWhiteSpace(targetEntityId)
                ? $"Hierarchy updated: {source.DisplayName} moved to root"
                : $"Hierarchy updated: {source.DisplayName} parent → {targetEntityId}";
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, label, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hierarchy reparent failed: {ex.Message}";
            ShowToast("Hierarchy reparent failed.");
            return false;
        }
    }

    private void UpdateSelectedEntityInspector()
    {
        RefreshInspectorDerivedState();
    }

    private bool IsDescendantOf(string candidateId, string ancestorId)
    {
        var current = ViewportEntities.FirstOrDefault(entity => string.Equals(entity.Id, candidateId, StringComparison.Ordinal));
        var guard = 0;
        while (current is not null && !string.IsNullOrWhiteSpace(current.ParentId) && guard++ < 256)
        {
            if (string.Equals(current.ParentId, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            current = ViewportEntities.FirstOrDefault(entity => string.Equals(entity.Id, current.ParentId, StringComparison.Ordinal));
        }

        return false;
    }

    private static bool TrySetEntityParentInScene(JsonObject root, string sourceEntityId, string? targetEntityId)
    {
        if (root["entities"] is JsonArray entities)
        {
            foreach (var node in entities.OfType<JsonObject>())
            {
                if (!string.Equals(node["id"]?.GetValue<string>(), sourceEntityId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(targetEntityId))
                {
                    node.Remove("parent_id");
                }
                else
                {
                    node["parent_id"] = targetEntityId;
                }

                return true;
            }
        }

        if (root["npcs"] is JsonArray npcs)
        {
            foreach (var node in npcs.OfType<JsonObject>())
            {
                if (!string.Equals(node["id"]?.GetValue<string>(), sourceEntityId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(targetEntityId))
                {
                    node.Remove("parent_id");
                }
                else
                {
                    node["parent_id"] = targetEntityId;
                }

                return true;
            }
        }

        return false;
    }

    private void RebuildHierarchyTree()
    {
        _hierarchyRoots.Clear();

        var sceneRoot = new HierarchyNode("scene_root", "🧭 Scene", "🧭", null, false);
        var npcGroup = new HierarchyNode("group_npcs", "🧍 NPCs", "🧍", null, false);
        var propGroup = new HierarchyNode("group_props", "📦 Props", "📦", null, false);
        var miscGroup = new HierarchyNode("group_misc", "🧩 Groups", "🧩", null, false);
        sceneRoot.Children.Add(new HierarchyNode("player_root", "👤 Player", "👤", "player_spawn", true));
        sceneRoot.Children.Add(npcGroup);
        sceneRoot.Children.Add(propGroup);
        sceneRoot.Children.Add(miscGroup);

        var lookup = ViewportEntities.ToDictionary(entity => entity.Id, entity => entity, StringComparer.Ordinal);
        foreach (var entity in ViewportEntities.Where(item => item.Type != "player"))
        {
            if (string.IsNullOrWhiteSpace(entity.ParentId))
            {
                var parentGroup = entity.Type switch
                {
                    "npc" => npcGroup,
                    "prop" => propGroup,
                    _ => miscGroup,
                };
                parentGroup.Children.Add(HierarchyNode.FromEntity(entity));
            }
        }

        var attached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parent in ViewportEntities.Where(entity => entity.Type != "player"))
        {
            var parentNode = FindNodeByEntity(sceneRoot, parent.Id);
            if (parentNode is null)
            {
                continue;
            }

            foreach (var child in ViewportEntities.Where(entity => string.Equals(entity.ParentId, parent.Id, StringComparison.Ordinal)))
            {
                parentNode.Children.Add(HierarchyNode.FromEntity(child));
                attached.Add(child.Id);
            }
        }

        foreach (var loose in ViewportEntities.Where(entity => entity.Type != "player" && !string.IsNullOrWhiteSpace(entity.ParentId) && !attached.Contains(entity.Id)))
        {
            if (!lookup.ContainsKey(loose.ParentId!))
            {
                propGroup.Children.Add(HierarchyNode.FromEntity(loose));
            }
        }

        _hierarchyRoots.Add(sceneRoot);
        SyncHierarchySelectionFromViewport();
        OnPropertyChanged(nameof(HierarchyRoots));
    }

    private void SyncHierarchySelectionFromViewport()
    {
        var selected = _selectedViewportEntities.FirstOrDefault();
        if (selected is null)
        {
            SelectedHierarchyNode = null;
            return;
        }

        var hierarchyNode = _hierarchyRoots
            .Select(root => FindNodeByEntity(root, selected.Id))
            .FirstOrDefault(node => node is not null);

        if (hierarchyNode is not null && !ReferenceEquals(_selectedHierarchyNode, hierarchyNode))
        {
            _selectedHierarchyNode = hierarchyNode;
            OnPropertyChanged(nameof(SelectedHierarchyNode));
        }
    }

    private static HierarchyNode? FindNodeByEntity(HierarchyNode node, string entityId)
    {
        if (node.IsEntityNode && string.Equals(node.EntityId, entityId, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeByEntity(child, entityId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void RefreshInspectorDerivedState()
    {
        if (_selectedViewportEntities.Count == 0)
        {
            SelectedEntitySummary = "No entity selected.";
            SelectedEntityType = "n/a";
            SelectedEntityX = 0;
            SelectedEntityY = 0;
            RuntimeSelectedEntityPreview = "No active selection.";
        SelectedEntityAssetName = "No asset linked.";
        SelectedEntityAssetPreviewPath = string.Empty;
        SelectedEntityAssetKind = "n/a";
            SetSelectionEditorDefaults();
            return;
        }

        SelectedViewportEntity = _selectedViewportEntities[0];
        SelectedEntityType = BuildSharedValue(_selectedViewportEntities.Select(entity => entity.Type), fallback: "Mixed");
        SelectedEntityX = _selectedViewportEntities.Average(entity => entity.X);
        SelectedEntityY = _selectedViewportEntities.Average(entity => entity.Y);
        SelectedEntitySummary = _selectedViewportEntities.Count == 1
            ? $"{_selectedViewportEntities[0].DisplayName} ({_selectedViewportEntities[0].Type})"
            : $"{_selectedViewportEntities.Count} entities selected";

        RuntimeSelectedEntityPreview = string.Join(Environment.NewLine, _selectedViewportEntities.Select(entity =>
            $"• {entity.Name} ({entity.Type})  pos({entity.X:F2}, {entity.Y:F2})  scale {entity.Scale:F2}  color {entity.ColorHex}  asset {entity.AssetLabel}"));

        var primaryAsset = _selectedViewportEntities[0];
        SelectedEntityAssetName = primaryAsset.AssetLabel;
        SelectedEntityAssetPreviewPath = primaryAsset.AssetPreviewPath ?? string.Empty;
        SelectedEntityAssetKind = string.IsNullOrWhiteSpace(primaryAsset.AssetKind) ? "n/a" : primaryAsset.AssetKind!;

        RefreshSelectionEditorsFromSelection();
    }

    private void RefreshSelectionEditorsFromSelection()
    {
        _isRefreshingSelectionEditors = true;
        try
        {
            _selectedEntityNameEditor = BuildSharedValue(_selectedViewportEntities.Select(entity => entity.Name), fallback: "(mixed)");
            _selectedEntityPositionXEditor = BuildSharedNumericValue(_selectedViewportEntities.Select(entity => entity.X));
            _selectedEntityPositionYEditor = BuildSharedNumericValue(_selectedViewportEntities.Select(entity => entity.Y));
            _selectedEntityScaleEditor = BuildSharedNumericValue(_selectedViewportEntities.Select(entity => entity.Scale));
            _selectedEntityColorEditor = BuildSharedValue(_selectedViewportEntities.Select(entity => entity.ColorHex), fallback: "(mixed)");
            OnPropertyChanged(nameof(SelectedEntityNameEditor));
            OnPropertyChanged(nameof(SelectedEntityPositionXEditor));
            OnPropertyChanged(nameof(SelectedEntityPositionYEditor));
            OnPropertyChanged(nameof(SelectedEntityScaleEditor));
            OnPropertyChanged(nameof(SelectedEntityColorEditor));
        }
        finally
        {
            _isRefreshingSelectionEditors = false;
        }
    }

    private void SetSelectionEditorDefaults()
    {
        _isRefreshingSelectionEditors = true;
        try
        {
            _selectedEntityNameEditor = string.Empty;
            _selectedEntityPositionXEditor = "0";
            _selectedEntityPositionYEditor = "0";
            _selectedEntityScaleEditor = "1";
            _selectedEntityColorEditor = "#4AA3FF";
            OnPropertyChanged(nameof(SelectedEntityNameEditor));
            OnPropertyChanged(nameof(SelectedEntityPositionXEditor));
            OnPropertyChanged(nameof(SelectedEntityPositionYEditor));
            OnPropertyChanged(nameof(SelectedEntityScaleEditor));
            OnPropertyChanged(nameof(SelectedEntityColorEditor));
        }
        finally
        {
            _isRefreshingSelectionEditors = false;
        }
    }

    private static string BuildSharedNumericValue(IEnumerable<float> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return "0";
        }

        var first = list[0];
        return list.All(value => Math.Abs(value - first) < 0.0001f)
            ? first.ToString("0.##", CultureInfo.InvariantCulture)
            : "(mixed)";
    }

    private static string BuildSharedValue(IEnumerable<string> values, string fallback)
    {
        var list = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (list.Count == 0)
        {
            return fallback;
        }

        var first = list[0];
        return list.All(value => string.Equals(value, first, StringComparison.Ordinal)) ? first : fallback;
    }

    private void ReplaceSelection(IEnumerable<ViewportEntity> entities)
    {
        foreach (var selected in _selectedViewportEntities)
        {
            selected.IsSelected = false;
        }

        _selectedViewportEntities.Clear();
        foreach (var entity in entities.DistinctBy(item => item.Id))
        {
            entity.IsSelected = true;
            _selectedViewportEntities.Add(entity);
        }

        SelectedViewportEntity = _selectedViewportEntities.FirstOrDefault();
        SyncHierarchySelectionFromViewport();
        RefreshInspectorDerivedState();
    }

    private void AddSelection(ViewportEntity entity)
    {
        if (_selectedViewportEntities.Any(item => item.Id == entity.Id))
        {
            return;
        }

        entity.IsSelected = true;
        _selectedViewportEntities.Add(entity);
        SelectedViewportEntity = _selectedViewportEntities[0];
        SyncHierarchySelectionFromViewport();
        RefreshInspectorDerivedState();
    }

    private void RemoveSelection(ViewportEntity entity)
    {
        entity.IsSelected = false;
        _selectedViewportEntities.Remove(entity);
        SelectedViewportEntity = _selectedViewportEntities.FirstOrDefault();
        SyncHierarchySelectionFromViewport();
        RefreshInspectorDerivedState();
    }

    private async Task DeleteSelectedEntityAsync(CancellationToken cancellationToken = default)
    {
        if (_selectedViewportEntities.Count == 0)
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
            var targets = _selectedViewportEntities.ToList();
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                StatusMessage = "Scene scaffold parse failed.";
                ShowToast("Scene parse failed.");
                return;
            }

            var deletedAny = false;
            foreach (var target in targets)
            {
                deletedAny = TryDeleteEntityInScene(root, target) || deletedAny;
            }

            if (!deletedAny)
            {
                ShowToast("Selected entity not found in scene.");
                return;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var label = targets.Count == 1
                ? $"Entity deleted: {targets[0].DisplayName}"
                : $"Entities deleted: {targets.Count}";
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, label, cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete entity failed: {ex.Message}";
            ShowToast("Entity delete failed.");
        }
    }

    private async Task ApplySelectionPropertiesAsync(CancellationToken cancellationToken = default)
    {
        if (_selectedViewportEntities.Count == 0)
        {
            ShowToast("Select at least one entity before applying properties.");
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before editing scene properties.";
            ShowToast("Generate prototype first.");
            return;
        }

        try
        {
            var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var root = JsonNode.Parse(beforeContent)?.AsObject();
            if (root is null)
            {
                ShowToast("Scene parse failed.");
                return;
            }

            foreach (var entity in _selectedViewportEntities)
            {
                ApplyEntityPropertiesInScene(root, entity);
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var label = _selectedViewportEntities.Count == 1
                ? $"Applied properties: {_selectedViewportEntities[0].DisplayName}"
                : $"Applied properties: {_selectedViewportEntities.Count} entities";
            await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, label, cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Property apply failed: {ex.Message}";
            ShowToast("Property apply failed.");
        }
    }

    private async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_undoStack.Count == 0 || _currentSceneHistoryEntry is null)
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
        _redoStack.Push(_currentSceneHistoryEntry.Value);
        _currentSceneHistoryEntry = history;
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, history.Content, $"Undo → {history.Description}", cancellationToken);
    }

    private async Task RedoAsync(CancellationToken cancellationToken = default)
    {
        if (_redoStack.Count == 0 || _currentSceneHistoryEntry is null)
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
        _undoStack.Push(_currentSceneHistoryEntry.Value);
        _currentSceneHistoryEntry = history;
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, history.Content, $"Redo → {history.Description}", cancellationToken);
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

        _currentSceneHistoryEntry ??= new SceneHistoryEntry(beforeContent, "Scene loaded", _nextHistoryRevision++);
        _undoStack.Push(_currentSceneHistoryEntry.Value);
        _redoStack.Clear();
        _currentSceneHistoryEntry = new SceneHistoryEntry(afterContent, operationDescription, _nextHistoryRevision++);
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
        RebuildHistoryTimeline();
    }

    private async Task JumpToHistoryAsync(object? parameter)
    {
        if (parameter is null || _currentSceneHistoryEntry is null)
        {
            return;
        }

        var targetRevision = parameter switch
        {
            int intRevision => intRevision,
            string revisionText when int.TryParse(revisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };

        if (targetRevision <= 0 || _currentSceneHistoryEntry.Value.Revision == targetRevision)
        {
            return;
        }

        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            ShowToast("No scene scaffold available.");
            return;
        }

        var undoChronological = _undoStack.Reverse().ToList();
        var redoTopFirst = _redoStack.ToList();
        var current = _currentSceneHistoryEntry.Value;

        var targetPastIndex = undoChronological.FindIndex(entry => entry.Revision == targetRevision);
        if (targetPastIndex >= 0)
        {
            var target = undoChronological[targetPastIndex];
            var nextUndoChronological = undoChronological.Take(targetPastIndex).ToList();
            var nextRedoTopFirst = undoChronological.Skip(targetPastIndex + 1).Concat(new[] { current }).Concat(redoTopFirst).ToList();
            ReplaceHistoryStacks(nextUndoChronological, nextRedoTopFirst);
            _currentSceneHistoryEntry = target;
            NotifyHistoryChanged();
            await ApplySceneContentAndRelaunchAsync(scenePath, target.Content, $"History jump → {target.Description}", CancellationToken.None);
            return;
        }

        var targetFutureIndex = redoTopFirst.FindIndex(entry => entry.Revision == targetRevision);
        if (targetFutureIndex >= 0)
        {
            var target = redoTopFirst[targetFutureIndex];
            var nextUndoChronological = undoChronological.Concat(new[] { current }).Concat(redoTopFirst.Take(targetFutureIndex)).ToList();
            var nextRedoTopFirst = redoTopFirst.Skip(targetFutureIndex + 1).ToList();
            ReplaceHistoryStacks(nextUndoChronological, nextRedoTopFirst);
            _currentSceneHistoryEntry = target;
            NotifyHistoryChanged();
            await ApplySceneContentAndRelaunchAsync(scenePath, target.Content, $"History jump → {target.Description}", CancellationToken.None);
        }
    }

    private void ReplaceHistoryStacks(IReadOnlyList<SceneHistoryEntry> undoChronological, IReadOnlyList<SceneHistoryEntry> redoTopFirst)
    {
        _undoStack.Clear();
        foreach (var entry in undoChronological)
        {
            _undoStack.Push(entry);
        }

        _redoStack.Clear();
        for (var index = redoTopFirst.Count - 1; index >= 0; index--)
        {
            _redoStack.Push(redoTopFirst[index]);
        }
    }

    private void RebuildHistoryTimeline()
    {
        _historyTimeline.Clear();
        var index = 0;
        foreach (var entry in _undoStack.Reverse())
        {
            _historyTimeline.Add(HistoryTimelineEntry.FromSceneEntry(entry, isCurrent: false, isFuture: false, index++));
        }

        if (_currentSceneHistoryEntry is SceneHistoryEntry current)
        {
            _historyTimeline.Add(HistoryTimelineEntry.FromSceneEntry(current, isCurrent: true, isFuture: false, index++));
        }

        foreach (var entry in _redoStack)
        {
            _historyTimeline.Add(HistoryTimelineEntry.FromSceneEntry(entry, isCurrent: false, isFuture: true, index++));
        }
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

    private static void ApplyEntityPropertiesInScene(JsonObject root, ViewportEntity entity)
    {
        if (entity.Type == "player")
        {
            if (root["player_spawn"] is JsonObject playerSpawn)
            {
                playerSpawn["x"] = entity.X;
                playerSpawn["y"] = entity.Y;
            }

            return;
        }

        if (root["entities"] is JsonArray entities)
        {
            foreach (var node in entities.OfType<JsonObject>())
            {
                if (!string.Equals(node["id"]?.GetValue<string>(), entity.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                node["x"] = entity.X;
                node["y"] = entity.Y;
                node["name"] = entity.Name;
                node["scale"] = entity.Scale;
                node["color"] = entity.ColorHex;
                return;
            }
        }

        if (root["npcs"] is JsonArray npcs)
        {
            foreach (var node in npcs.OfType<JsonObject>())
            {
                if (!string.Equals(node["id"]?.GetValue<string>(), entity.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                node["spawn_x"] = entity.X;
                node["spawn_y"] = entity.Y;
                node["name"] = entity.Name;
                node["scale"] = entity.Scale;
                node["color"] = entity.ColorHex;
                return;
            }
        }
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

        if (_selectedViewportEntities.Any(item => item.Id == entity.Id)
            && (e.PropertyName == nameof(ViewportEntity.X)
                || e.PropertyName == nameof(ViewportEntity.Y)
                || e.PropertyName == nameof(ViewportEntity.Name)
                || e.PropertyName == nameof(ViewportEntity.Scale)
                || e.PropertyName == nameof(ViewportEntity.ColorHex)))
        {
            RefreshInspectorDerivedState();
        }

        if (e.PropertyName == nameof(ViewportEntity.Name) || e.PropertyName == nameof(ViewportEntity.ParentId))
        {
            RebuildHierarchyTree();
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

    private static bool TryParseFloat(string value, out float parsed)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static string? SanitizeColorHex(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Trim();
        if (!normalized.StartsWith('#'))
        {
            normalized = $"#{normalized}";
        }

        if (normalized.Length != 7)
        {
            return null;
        }

        for (var index = 1; index < normalized.Length; index++)
        {
            if (!Uri.IsHexDigit(normalized[index]))
            {
                return null;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static string DefaultColorForType(string type) => type switch
    {
        "player" => "#4AA3FF",
        "npc" => "#8BD17C",
        _ => "#E4A14A",
    };

    private static bool TryGetSupportedAssetKind(string sourcePath, out string kind)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        kind = extension switch
        {
            ".png" => ImportedAssetKind.Texture,
            ".obj" => ImportedAssetKind.Model,
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(kind);
    }

    private static string BuildNextImportedAssetId(JsonArray assets)
    {
        var next = assets.OfType<JsonObject>()
            .Select(node => node["id"]?.GetValue<string>() ?? string.Empty)
            .Select(id => Regex.Match(id, @"asset_(\d+)$"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"asset_{next:00}";
    }

    private static string BuildNextEntityIdFromAssets(JsonArray entities, string assetId)
    {
        var sanitized = assetId.Replace("asset_", "import_", StringComparison.Ordinal);
        var next = entities.OfType<JsonObject>()
            .Select(node => node["id"]?.GetValue<string>() ?? string.Empty)
            .Select(id => Regex.Match(id, $@"{Regex.Escape(sanitized)}_(\d+)$"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{sanitized}_{next:00}";
    }

    private void LoadImportedAssets(JsonElement root)
    {
        if (!root.TryGetProperty("imported_assets", out var importedAssets) || importedAssets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var imported in importedAssets.EnumerateArray())
        {
            var id = imported.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var name = imported.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var path = imported.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            var kind = imported.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : ImportedAssetKind.Texture;
            var color = imported.TryGetProperty("color", out var colorElement) ? colorElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            ImportedAssets.Add(new ImportedAsset(
                id,
                name ?? Path.GetFileNameWithoutExtension(path),
                kind ?? ImportedAssetKind.Texture,
                path,
                SanitizeColorHex(color ?? string.Empty) ?? "#7FD1FF"));
        }

        SelectedImportedAsset = ImportedAssets.FirstOrDefault();
    }

    private async Task StopPreviousRuntimeIfRunningAsync(CancellationToken cancellationToken)
    {
        var candidatePid = _trackedRuntimePid ?? RuntimePid;
        if (candidatePid is not int pid || pid <= 0)
        {
            return;
        }

        var stopResult = await _runtimeSupervisor.TryStopRuntimeProcessAsync(pid, PrototypeRoot, cancellationToken);
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

    internal readonly record struct RuntimeStopResult(bool Stopped, string ErrorMessage);

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

    private readonly record struct DragSession(string EntityId, IReadOnlyList<DragEntitySnapshot> Entities);

    private readonly record struct DragEntitySnapshot(string EntityId, float StartX, float StartY);

    internal readonly record struct SceneHistoryEntry(string Content, string Description, int Revision);

    public sealed class HistoryTimelineEntry
    {
        public required int Revision { get; init; }

        public required string Description { get; init; }

        public required string Icon { get; init; }

        public required bool IsCurrent { get; init; }

        public required bool IsFuture { get; init; }

        public required int Index { get; init; }

        public string RevisionLabel => $"#{Revision:000}";

        internal static HistoryTimelineEntry FromSceneEntry(SceneHistoryEntry entry, bool isCurrent, bool isFuture, int index)
            => new()
            {
                Revision = entry.Revision,
                Description = entry.Description,
                Icon = ResolveHistoryIcon(entry.Description),
                IsCurrent = isCurrent,
                IsFuture = isFuture,
                Index = index,
            };

        private static string ResolveHistoryIcon(string description)
        {
            var text = description.ToLowerInvariant();
            if (text.Contains("added", StringComparison.Ordinal) || text.Contains("placed", StringComparison.Ordinal))
            {
                return "➕";
            }

            if (text.Contains("moved", StringComparison.Ordinal))
            {
                return "🧭";
            }

            if (text.Contains("import", StringComparison.Ordinal))
            {
                return "📥";
            }

            if (text.Contains("deleted", StringComparison.Ordinal))
            {
                return "🗑";
            }

            if (text.Contains("undo", StringComparison.Ordinal))
            {
                return "↶";
            }

            if (text.Contains("redo", StringComparison.Ordinal))
            {
                return "↷";
            }

            if (text.Contains("loaded", StringComparison.Ordinal))
            {
                return "🎬";
            }

            return "•";
        }
    }

    public sealed class HierarchyNode
    {
        public HierarchyNode(string id, string label, string icon, string? entityId, bool isEntityNode)
        {
            Id = id;
            Label = label;
            Icon = icon;
            EntityId = entityId;
            IsEntityNode = isEntityNode;
        }

        public string Id { get; }

        public string Label { get; }

        public string Icon { get; }

        public string? EntityId { get; }

        public bool IsEntityNode { get; }

        public ObservableCollection<HierarchyNode> Children { get; } = new();

        public static HierarchyNode FromEntity(ViewportEntity entity)
            => new(entity.Id, $"{ResolveIcon(entity.Type)} {entity.DisplayName}", ResolveIcon(entity.Type), entity.Id, true);

        private static string ResolveIcon(string type) => type switch
        {
            "npc" => "🧍",
            "prop" => "📦",
            "player" => "👤",
            _ => "🧩",
        };
    }

    public sealed class ViewportEntity : INotifyPropertyChanged
    {
        private string _name;
        private float _x;
        private float _y;
        private float _scale;
        private string _colorHex;
        private bool _isSelected;
        private string? _parentId;
        private readonly string? _assetId;
        private readonly string? _assetPath;
        private readonly string? _assetKind;

        public ViewportEntity(string id, string type, string name, float x, float y, float scale, string colorHex, string? parentId = null, string? assetId = null, string? assetPath = null, string? assetKind = null)
        {
            Id = id;
            Type = type;
            _name = name;
            _x = x;
            _y = y;
            _scale = scale;
            _colorHex = colorHex;
            _parentId = parentId;
            _assetId = assetId;
            _assetPath = assetPath;
            _assetKind = assetKind;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string Type { get; }

        public string Name
        {
            get => _name;
            private set
            {
                if (string.Equals(_name, value, StringComparison.Ordinal))
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

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

        public float Scale
        {
            get => _scale;
            private set
            {
                if (Math.Abs(_scale - value) < 0.0001f)
                {
                    return;
                }

                _scale = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scale)));
            }
        }

        public string ColorHex
        {
            get => _colorHex;
            private set
            {
                if (string.Equals(_colorHex, value, StringComparison.Ordinal))
                {
                    return;
                }

                _colorHex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorHex)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string? ParentId
        {
            get => _parentId;
            private set
            {
                if (string.Equals(_parentId, value, StringComparison.Ordinal))
                {
                    return;
                }

                _parentId = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParentId)));
            }
        }

        public void SetPosition(float x, float y)
        {
            X = x;
            Y = y;
        }

        public void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            Name = name.Trim();
        }

        public void SetScale(float scale)
        {
            Scale = Math.Max(0.1f, scale);
        }

        public void SetColorHex(string colorHex)
        {
            ColorHex = colorHex;
        }

        public void SetParentId(string? parentId)
        {
            ParentId = parentId;
        }

        public string DisplayName => Name;

        public string? AssetId => _assetId;

        public string? AssetPath => _assetPath;

        public string? AssetKind => _assetKind;

        public string AssetLabel => string.IsNullOrWhiteSpace(_assetPath)
            ? "No asset linked."
            : Path.GetFileName(_assetPath);

        public string? AssetPreviewPath => string.Equals(_assetKind, ImportedAssetKind.Texture, StringComparison.OrdinalIgnoreCase)
            ? _assetPath
            : null;
    }

    public sealed class ImportedAsset
    {
        public ImportedAsset(string id, string displayName, string kind, string sourcePath, string colorHex)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            SourcePath = sourcePath;
            ColorHex = colorHex;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Kind { get; }

        public string SourcePath { get; }

        public string ColorHex { get; }

        public string PreviewPath => string.Equals(Kind, ImportedAssetKind.Texture, StringComparison.OrdinalIgnoreCase)
            ? SourcePath
            : string.Empty;
    }

    private static class ImportedAssetKind
    {
        public const string Texture = "texture";
        public const string Model = "model";
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

    private sealed class AsyncRelayCommand<T>(Func<T?, Task> executeAsync) : ICommand
    {
        private readonly Func<T?, Task> _executeAsync = executeAsync;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter)
        {
            var typedParameter = parameter is T value ? value : default;
            await _executeAsync(typedParameter);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal interface IOrchestratorGateway
    {
        Task<PipelineRunResponse> RunGenerationPipelineAsync(string briefPath, bool launchRuntime, CancellationToken cancellationToken);

        string CreateBriefFromChatPrompt(string prompt);
    }

    internal interface IRuntimeSupervisor
    {
        Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken);

        LaunchResult LaunchGeneratedRunner(string generatedBuildRoot);

        Task<RuntimeStopResult> TryStopRuntimeProcessAsync(int pid, string prototypeRoot, CancellationToken cancellationToken);
    }

    internal readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

    internal readonly record struct LaunchResult(bool Success, int? Pid, string ErrorMessage);

    private sealed class OrchestratorGateway : IOrchestratorGateway
    {
        private readonly OrchestratorClient _client = new();

        public Task<PipelineRunResponse> RunGenerationPipelineAsync(string briefPath, bool launchRuntime, CancellationToken cancellationToken)
            => _client.RunGenerationPipelineAsync(briefPath, launchRuntime, cancellationToken);

        public string CreateBriefFromChatPrompt(string prompt)
            => OrchestratorClient.CreateBriefFromChatPrompt(prompt);
    }

    private sealed class RuntimeSupervisor : IRuntimeSupervisor
    {
        public async Task<ProcessResult> RunProcessAsync(
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

        public LaunchResult LaunchGeneratedRunner(string generatedBuildRoot)
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

        public async Task<RuntimeStopResult> TryStopRuntimeProcessAsync(int pid, string prototypeRoot, CancellationToken cancellationToken)
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

                var workingDirectory = prototypeRoot == "(none)" ? Environment.CurrentDirectory : prototypeRoot;
                var killResult = await RunProcessAsync("kill", $"-9 {pid}", workingDirectory, cancellationToken);
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
    }
}
