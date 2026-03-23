using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using GameForge.Editor.EditorShell;
using GameForge.Editor.EditorShell.Services;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Avalonia.Media;

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
    private string _activeLeftPanelTab = LeftPanelTabHierarchy;
    private DragSession? _activeDragSession;
    private string _selectedEntityAssetName = "No asset linked.";
    private string _selectedEntityAssetPreviewPath = string.Empty;
    private string _selectedEntityAssetKind = "n/a";
    private ImportedAsset? _selectedImportedAsset;
    private readonly ObservableCollection<ImportedAsset> _filteredImportedAssets = new();
    private readonly ReadOnlyObservableCollection<ImportedAsset> _readonlyFilteredImportedAssets;
    private string _assetSearchText = string.Empty;
    private string _assetDragGhostTitle = string.Empty;
    private string _assetDragGhostPreviewPath = string.Empty;
    private string _assetDragGhostKind = string.Empty;
    private float _assetDragGhostWorldX;
    private float _assetDragGhostWorldY;
    private bool _isAssetDragGhostVisible;
    private readonly Dictionary<string, EntityAnimationTrack> _animationTracks = new(StringComparer.Ordinal);
    private readonly ObservableCollection<TimelineMarker> _timelineMarkers = new();
    private readonly ReadOnlyObservableCollection<TimelineMarker> _readonlyTimelineMarkers;
    private float _timelineCurrentTime;
    private float _timelineDuration = 8f;
    private bool _isTimelinePlaying;
    private bool _isTimelineLoopEnabled = true;
    private bool _isTimelineApplyingPose;
    private string _timelineStateLabel = "Idle";
    private const string TimelineModePosition = "Position";
    private const string TimelineModeScale = "Scale";
    private const string TimelineModeColor = "Color";
    private const string LeftPanelTabHierarchy = "Hierarchy";
    private const string LeftPanelTabAssets = "Assets";
    private const string LeftPanelTabHistory = "History";
    private string _timelineMode = TimelineModePosition;
    private CancellationTokenSource? _timelinePlaybackCts;
    private bool _isExportChecklistVisible;
    private bool _isExporting;
    private string _exportStatus = "Export checklist idle.";
    private string _exportOutputPath = "Not packaged yet.";
    private string _exportPackagePath = "Not packaged yet.";
    private string _exportFolderPath = "Not exported yet.";
    private readonly ObservableCollection<ExportChecklistItem> _exportChecklistItems = new();
    private readonly ReadOnlyObservableCollection<ExportChecklistItem> _readonlyExportChecklistItems;
    private SteamReadinessReport? _lastSteamReadinessReport;
    private string _steamReadinessSummary = "Steam readiness not evaluated yet.";
    private string _steamReadinessWarnings = "No readiness warnings.";
    private string _publishDryRunStatus = "Publish dry-run not started.";
    private string _steamReleaseNotes = "V1 local-first release candidate.\n- Gameplay loop validated\n- Steam readiness checklist reviewed\n- Uploaded via local stub (no live Steam API)";
    private bool _isSteamUploadInProgress;
    private int _steamUploadProgressPercent;
    private string _steamUploadStatus = "Steam upload stub not started.";
    private string _steamUploadAuditPath = "No upload audit log yet.";
    private bool _isInstallerBuildInProgress;
    private int _installerBuildProgressPercent;
    private string _installerBuildStatus = "Installer build not started.";
    private string _installerOutputPath = "No installer artifact yet.";
    private string _installerBuildLog = "Installer logs will appear here.";
    private readonly string _settingsFilePath;
    private EditorPreferences _preferences = EditorPreferences.CreateDefault();

    public MainWindowViewModel()
        : this(new OrchestratorGateway(), new RuntimeSupervisor())
    {
    }

    internal MainWindowViewModel(IOrchestratorGateway orchestratorGateway, IRuntimeSupervisor runtimeSupervisor, string? settingsFilePath = null)
    {
        _orchestratorGateway = orchestratorGateway;
        _runtimeSupervisor = runtimeSupervisor;
        _settingsFilePath = settingsFilePath ?? Path.Combine(Environment.CurrentDirectory, ".forgeengine", "settings.json");
        _preferences = EditorPreferences.LoadOrDefault(_settingsFilePath);
        _readonlySelectedViewportEntities = new ReadOnlyObservableCollection<ViewportEntity>(_selectedViewportEntities);
        _readonlyHistoryTimeline = new ReadOnlyObservableCollection<HistoryTimelineEntry>(_historyTimeline);
        _readonlyHierarchyRoots = new ReadOnlyObservableCollection<HierarchyNode>(_hierarchyRoots);
        _readonlyTimelineMarkers = new ReadOnlyObservableCollection<TimelineMarker>(_timelineMarkers);
        _readonlyExportChecklistItems = new ReadOnlyObservableCollection<ExportChecklistItem>(_exportChecklistItems);
        _readonlyFilteredImportedAssets = new ReadOnlyObservableCollection<ImportedAsset>(_filteredImportedAssets);
        ResetExportChecklistItems();
        AddPlayerEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("player"));
        AddNpcEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("npc"));
        AddPropEntityCommand = new AsyncRelayCommand(() => AddEntityAndRelaunchAsync("prop"));
        DeleteSelectedEntityCommand = new AsyncRelayCommand(() => DeleteSelectedEntityAsync());
        ApplySelectionPropertiesCommand = new AsyncRelayCommand(() => ApplySelectionPropertiesAsync());
        UndoCommand = new AsyncRelayCommand(() => UndoAsync());
        RedoCommand = new AsyncRelayCommand(() => RedoAsync());
        PlayRuntimeCommand = new AsyncRelayCommand(() => PlayRuntimeAsync());
        JumpToHistoryCommand = new AsyncRelayCommand<object?>(JumpToHistoryAsync);
        AddTimelineKeyframeCommand = new AsyncRelayCommand(AddSelectedEntitiesKeyframeAsync);
        ToggleTimelinePlaybackCommand = new AsyncRelayCommand(ToggleTimelinePlaybackAsync);
        StopTimelinePlaybackCommand = new AsyncRelayCommand(() => StopTimelinePlaybackAsync(resetTime: true));
        SetTimelineModeCommand = new AsyncRelayCommand<object?>(SetTimelineModeAsync);
        SetLeftPanelTabCommand = new AsyncRelayCommand<object?>(SetLeftPanelTabAsync);
        ViewportEntities.CollectionChanged += OnViewportEntitiesCollectionChanged;
        ImportedAssets.CollectionChanged += OnImportedAssetsCollectionChanged;
        _selectedViewportEntities.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSingleSelection));
            OnPropertyChanged(nameof(SelectedEntitiesCount));
        };
        EnforceHistoryLimit();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? ThemePreferenceChanged;

    public ObservableCollection<ViewportEntity> ViewportEntities { get; } = new();

    public ObservableCollection<ImportedAsset> ImportedAssets { get; } = new();
    public ReadOnlyObservableCollection<ImportedAsset> FilteredImportedAssets => _readonlyFilteredImportedAssets;

    public ReadOnlyObservableCollection<HierarchyNode> HierarchyRoots => _readonlyHierarchyRoots;

    public ICommand AddPlayerEntityCommand { get; }

    public ICommand AddNpcEntityCommand { get; }

    public ICommand AddPropEntityCommand { get; }

    public ICommand DeleteSelectedEntityCommand { get; }

    public ICommand ApplySelectionPropertiesCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand PlayRuntimeCommand { get; }

    public ICommand JumpToHistoryCommand { get; }

    public ICommand AddTimelineKeyframeCommand { get; }

    public ICommand ToggleTimelinePlaybackCommand { get; }

    public ICommand StopTimelinePlaybackCommand { get; }

    public ICommand SetTimelineModeCommand { get; }

    public ICommand SetLeftPanelTabCommand { get; }

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

    public string ActiveLeftPanelTab
    {
        get => _activeLeftPanelTab;
        private set
        {
            if (!SetField(ref _activeLeftPanelTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsHierarchyTabActive));
            OnPropertyChanged(nameof(IsHierarchyTabInactive));
            OnPropertyChanged(nameof(IsAssetsTabActive));
            OnPropertyChanged(nameof(IsAssetsTabInactive));
            OnPropertyChanged(nameof(IsHistoryTabActive));
            OnPropertyChanged(nameof(IsHistoryTabInactive));
        }
    }

    public bool IsHierarchyTabActive => string.Equals(ActiveLeftPanelTab, LeftPanelTabHierarchy, StringComparison.Ordinal);
    public bool IsHierarchyTabInactive => !IsHierarchyTabActive;

    public bool IsAssetsTabActive => string.Equals(ActiveLeftPanelTab, LeftPanelTabAssets, StringComparison.Ordinal);
    public bool IsAssetsTabInactive => !IsAssetsTabActive;

    public bool IsHistoryTabActive => string.Equals(ActiveLeftPanelTab, LeftPanelTabHistory, StringComparison.Ordinal);
    public bool IsHistoryTabInactive => !IsHistoryTabActive;

    public bool IsAutosaveEnabled => _preferences.General.AutosaveEnabled;

    public string ThemePreference => _preferences.General.Theme;

    public string AutosaveStatusLabel => IsAutosaveEnabled ? "Autosave: On" : "Autosave: Off";

    public string RuntimePreferencesSummary => $"{_preferences.Runtime.VulkanResolution} @ {_preferences.Runtime.FpsLimit} FPS cap";

    public string ShortcutHintBar => "Shortcuts: Ctrl+N New • Ctrl+S Save • Ctrl+Z/Y Undo/Redo • Ctrl+Shift+P Play • Ctrl+I Import • Ctrl+Shift+S Settings";

    public int RibbonIconSize => _preferences.Editor.IconSize;

    public string EditorDefaultTemplateId => _preferences.Editor.DefaultTemplateId;

    public bool HasTimelineKeyframes => _animationTracks.Values.Any(track =>
        track.PositionFrames.Count > 0 || track.ScaleFrames.Count > 0 || track.ColorFrames.Count > 0);

    public ReadOnlyObservableCollection<TimelineMarker> TimelineMarkers => _readonlyTimelineMarkers;

    public float TimelineCurrentTime
    {
        get => _timelineCurrentTime;
        set => SetTimelineCurrentTime(value, fromPlayback: false);
    }

    public float TimelineDuration
    {
        get => _timelineDuration;
        set
        {
            var clamped = Math.Clamp(value, 1f, 60f);
            if (!SetField(ref _timelineDuration, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(TimelineCurrentTimeLabel));
            RebuildTimelineMarkers();
        }
    }

    public bool IsTimelinePlaying
    {
        get => _isTimelinePlaying;
        private set
        {
            if (!SetField(ref _isTimelinePlaying, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TimelinePlayPauseIcon));
            OnPropertyChanged(nameof(TimelinePlaybackStatePill));
        }
    }

    public bool IsTimelineLoopEnabled
    {
        get => _isTimelineLoopEnabled;
        set => SetField(ref _isTimelineLoopEnabled, value);
    }

    public string TimelinePlayPauseIcon => IsTimelinePlaying ? "⏸" : "▶";

    public string TimelinePlayPauseGlyph => TimelinePlayPauseIcon;

    public string TimelineCurrentTimeLabel => $"{TimelineCurrentTime:0.00}s / {TimelineDuration:0.00}s";

    public string TimelinePlaybackStatePill => IsTimelinePlaying ? "Playing" : "Paused";

    public string TimelinePlaybackHint => IsTimelineLoopEnabled ? "Loop" : "One-shot";

    public double TimelineScrubberPercent => TimelineDurationSeconds <= 0.001
        ? 0
        : (TimelineCurrentTime / TimelineDurationSeconds) * 100.0;

    public double TimelineDurationSeconds => TimelineDuration;

    public bool IsTimelineLooping
    {
        get => IsTimelineLoopEnabled;
        set
        {
            IsTimelineLoopEnabled = value;
            OnPropertyChanged(nameof(TimelinePlaybackHint));
        }
    }

    public string TimelineMode
    {
        get => _timelineMode;
        private set => SetField(ref _timelineMode, value);
    }

    public string TimelineStateLabel
    {
        get => _timelineStateLabel;
        private set => SetField(ref _timelineStateLabel, value);
    }

    public bool IsExportChecklistVisible
    {
        get => _isExportChecklistVisible;
        private set => SetField(ref _isExportChecklistVisible, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (!SetField(ref _isExporting, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanRunSteamUploadStub));
        }
    }

    public string ExportStatus
    {
        get => _exportStatus;
        private set => SetField(ref _exportStatus, value);
    }

    public string ExportOutputPath
    {
        get => _exportOutputPath;
        private set
        {
            if (!SetField(ref _exportOutputPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasExportOutput));
        }
    }

    public string ExportPackagePath
    {
        get => _exportPackagePath;
        private set
        {
            if (!SetField(ref _exportPackagePath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasExportPackageOutput));
        }
    }

    public string ExportFolderPath
    {
        get => _exportFolderPath;
        private set
        {
            if (!SetField(ref _exportFolderPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasExportFolderOutput));
        }
    }

    public bool HasExportOutput => File.Exists(ExportOutputPath) || Directory.Exists(ExportOutputPath);

    public bool HasExportPackageOutput => File.Exists(ExportPackagePath);

    public bool HasExportFolderOutput => Directory.Exists(ExportFolderPath);

    public int ExportChecklistCompletedCount => ExportChecklistItems.Count(item => item.IsComplete);

    public int ExportChecklistTotalCount => ExportChecklistItems.Count;

    public int ExportChecklistProgressPercent => ExportChecklistTotalCount == 0
        ? 0
        : (int)Math.Round((double)ExportChecklistCompletedCount / ExportChecklistTotalCount * 100.0, MidpointRounding.AwayFromZero);

    public ReadOnlyObservableCollection<ExportChecklistItem> ExportChecklistItems => _readonlyExportChecklistItems;

    public string SteamReadinessSummary
    {
        get => _steamReadinessSummary;
        private set => SetField(ref _steamReadinessSummary, value);
    }

    public string SteamReadinessWarnings
    {
        get => _steamReadinessWarnings;
        private set => SetField(ref _steamReadinessWarnings, value);
    }

    public bool HasSteamReadinessWarnings => _lastSteamReadinessReport?.WarningIssueCount > 0;

    public string PublishDryRunStatus
    {
        get => _publishDryRunStatus;
        private set => SetField(ref _publishDryRunStatus, value);
    }

    public string SteamReleaseNotes
    {
        get => _steamReleaseNotes;
        set => SetField(ref _steamReleaseNotes, value);
    }

    public bool IsSteamUploadInProgress
    {
        get => _isSteamUploadInProgress;
        private set
        {
            if (!SetField(ref _isSteamUploadInProgress, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanRunSteamUploadStub));
        }
    }

    public int SteamUploadProgressPercent
    {
        get => _steamUploadProgressPercent;
        private set => SetField(ref _steamUploadProgressPercent, Math.Clamp(value, 0, 100));
    }

    public string SteamUploadStatus
    {
        get => _steamUploadStatus;
        private set => SetField(ref _steamUploadStatus, value);
    }

    public string SteamUploadAuditPath
    {
        get => _steamUploadAuditPath;
        private set => SetField(ref _steamUploadAuditPath, value);
    }

    public bool CanRunSteamUploadStub => !IsExporting && !IsSteamUploadInProgress;

    public bool IsInstallerBuildInProgress
    {
        get => _isInstallerBuildInProgress;
        private set
        {
            if (!SetField(ref _isInstallerBuildInProgress, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanRunInstallerBuild));
        }
    }

    public bool CanRunInstallerBuild => !IsExporting && !IsSteamUploadInProgress && !IsInstallerBuildInProgress;

    public int InstallerBuildProgressPercent
    {
        get => _installerBuildProgressPercent;
        private set => SetField(ref _installerBuildProgressPercent, Math.Clamp(value, 0, 100));
    }

    public string InstallerBuildStatus
    {
        get => _installerBuildStatus;
        private set => SetField(ref _installerBuildStatus, value);
    }

    public string InstallerOutputPath
    {
        get => _installerOutputPath;
        private set
        {
            if (!SetField(ref _installerOutputPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasInstallerOutput));
        }
    }

    public bool HasInstallerOutput => InstallerOutputPath
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(path => File.Exists(path));

    public string InstallerBuildLog
    {
        get => _installerBuildLog;
        private set => SetField(ref _installerBuildLog, value);
    }

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

    public string AssetSearchText
    {
        get => _assetSearchText;
        set
        {
            if (!SetField(ref _assetSearchText, value))
            {
                return;
            }

            ApplyAssetFilter();
        }
    }

    public bool HasAssetResults => FilteredImportedAssets.Count > 0;

    public bool HasNoAssetResults => !HasAssetResults;

    public string AssetResultsSummary => HasAssetResults
        ? $"{FilteredImportedAssets.Count} shown / {ImportedAssets.Count} total"
        : $"0 shown / {ImportedAssets.Count} total";

    public bool IsAssetDragGhostVisible
    {
        get => _isAssetDragGhostVisible;
        private set => SetField(ref _isAssetDragGhostVisible, value);
    }

    public string AssetDragGhostTitle
    {
        get => _assetDragGhostTitle;
        private set => SetField(ref _assetDragGhostTitle, value);
    }

    public string AssetDragGhostPreviewPath
    {
        get => _assetDragGhostPreviewPath;
        private set => SetField(ref _assetDragGhostPreviewPath, value);
    }

    public string AssetDragGhostKind
    {
        get => _assetDragGhostKind;
        private set => SetField(ref _assetDragGhostKind, value);
    }

    public float AssetDragGhostWorldX
    {
        get => _assetDragGhostWorldX;
        private set => SetField(ref _assetDragGhostWorldX, value);
    }

    public float AssetDragGhostWorldY
    {
        get => _assetDragGhostWorldY;
        private set => SetField(ref _assetDragGhostWorldY, value);
    }

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
        StopTimelinePlayback();
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
        _filteredImportedAssets.Clear();
        _animationTracks.Clear();
        _timelineMarkers.Clear();
        _timelineCurrentTime = 0f;
        OnPropertyChanged(nameof(TimelineCurrentTime));
        OnPropertyChanged(nameof(TimelineCurrentTimeLabel));
        OnPropertyChanged(nameof(TimelineScrubberPercent));
        TimelineStateLabel = "Idle";
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

    public async Task CreateProjectFromTemplateAsync(
        ProjectTemplatePreset template,
        string projectName,
        string quickConcept,
        CancellationToken cancellationToken = default)
    {
        var sanitizedProjectName = string.IsNullOrWhiteSpace(projectName) ? template.DisplayName : projectName.Trim();
        var supplementalConcept = quickConcept?.Trim() ?? string.Empty;

        await NewPrototypeAsync(cancellationToken);
        ChatPrompt = BuildTemplatePrompt(template, sanitizedProjectName, supplementalConcept);
        _lastBriefPath = _orchestratorGateway.CreateBriefFromTemplate(template, sanitizedProjectName, supplementalConcept);
        StatusMessage = $"Creating '{sanitizedProjectName}' from {template.DisplayName} template...";
        ShowToast($"New project: {template.DisplayName}");
        await RunPipelineForBriefAsync(_lastBriefPath, launchRuntime: true, cancellationToken);
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

    public static IReadOnlyList<ProjectTemplatePreset> GetProjectTemplatePresets()
    {
        return
        [
            new ProjectTemplatePreset(
                Id: "empty-scene",
                DisplayName: "Empty Scene",
                Icon: "🌌",
                BriefSummary: "Blank solo sandbox with only player start + camera ready.",
                CoreLoop: "Explore -> Place basics -> Iterate"),
            new ProjectTemplatePreset(
                Id: "simple-rpg",
                DisplayName: "Simple RPG",
                Icon: "🗡️",
                BriefSummary: "Quest-forward starter with NPC beats and lightweight progression.",
                CoreLoop: "Talk -> Quest -> Reward"),
            new ProjectTemplatePreset(
                Id: "cozy-colony",
                DisplayName: "Cozy Colony",
                Icon: "🏡",
                BriefSummary: "Relaxed colony loop with gathering, building, and villager rhythm.",
                CoreLoop: "Gather -> Build -> Care"),
            new ProjectTemplatePreset(
                Id: "basic-rts",
                DisplayName: "Basic RTS",
                Icon: "⚔️",
                BriefSummary: "Small command-and-expand battlefield baseline with resource pressure.",
                CoreLoop: "Scout -> Expand -> Defend"),
        ];
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

    public void OpenExportChecklist()
    {
        IsExportChecklistVisible = true;
        ExportStatus = "Checklist opened. Run Export to validate and package this project.";
    }

    public void CloseExportChecklist()
    {
        IsExportChecklistVisible = false;
    }

    public async Task RunExportChecklistAsync(CancellationToken cancellationToken = default)
    {
        if (IsExporting)
        {
            return;
        }

        OpenExportChecklist();
        ResetExportChecklistItems();

        if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
        {
            ExportStatus = "Generate a prototype before exporting.";
            FailExportChecklistItem("validate-scene", "No generated prototype found.");
            StatusMessage = ExportStatus;
            ShowToast("Export blocked: generate a prototype first.");
            return;
        }

        IsExporting = true;
        try
        {
            var exportRoot = Path.Combine(PrototypeRoot, "build", "export");
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var exportFolder = Path.Combine(exportRoot, $"publish-{stamp}");
            var packagePath = Path.Combine(exportRoot, $"publish-{stamp}.zip");
            Directory.CreateDirectory(exportRoot);

            StartExportChecklistItem("validate-scene", "Validating scene scaffold...");
            var scenePath = Path.Combine(PrototypeRoot, "scene", "scene_scaffold.json");
            if (!File.Exists(scenePath))
            {
                throw new InvalidOperationException("Scene scaffold not found.");
            }

            using (var sceneDoc = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath, cancellationToken)))
            {
                if (!sceneDoc.RootElement.TryGetProperty("entities", out _))
                {
                    throw new InvalidOperationException("Scene scaffold is missing entities.");
                }
            }

            CompleteExportChecklistItem("validate-scene", "Scene scaffold validated.");

            StartExportChecklistItem("export-assets-code", "Collecting generated assets + code...");
            Directory.CreateDirectory(exportFolder);
            CopyDirectory(Path.Combine(PrototypeRoot, "scene"), Path.Combine(exportFolder, "scene"));
            CopyDirectory(Path.Combine(PrototypeRoot, "generated", "cpp"), Path.Combine(exportFolder, "generated", "cpp"));
            CopyDirectory(Path.Combine(PrototypeRoot, "assets"), Path.Combine(exportFolder, "assets"), includeIfMissing: true);
            WriteSteamReleaseNotesFile(exportFolder);
            CompleteExportChecklistItem("export-assets-code", "Assets + C++ runtime scaffolding exported.");

            StartExportChecklistItem("package-build", "Packaging export ZIP...");
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            ZipFile.CreateFromDirectory(exportFolder, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            CompleteExportChecklistItem("package-build", "Packaged ZIP is ready.");

            StartExportChecklistItem("steam-readiness", "Running Steam readiness policy check...");
            var metricsPath = ResolveSteamReadinessMetricsPath();
            var readinessReport = SteamReadinessPolicy.Evaluate(SteamReadinessPolicy.LoadMetrics(metricsPath));
            ApplySteamReadinessDetails(readinessReport);
            if (readinessReport.CriticalIssueCount > 0)
            {
                throw new InvalidOperationException($"Steam readiness has {readinessReport.CriticalIssueCount} critical issue(s).");
            }

            var readinessSummary = readinessReport.WarningIssueCount > 0
                ? $"Readiness score {readinessReport.Score}/100 with {readinessReport.WarningIssueCount} warning(s)."
                : $"Readiness score {readinessReport.Score}/100 with no warnings.";
            CompleteExportChecklistItem("steam-readiness", readinessSummary, hasWarning: readinessReport.WarningIssueCount > 0);

            ExportPackagePath = packagePath;
            ExportFolderPath = exportFolder;
            ExportOutputPath = packagePath;
            ExportStatus = $"Export complete ({ExportChecklistCompletedCount}/{ExportChecklistTotalCount}).";
            PipelineProgress = "Export package complete";
            StatusMessage = $"{ExportStatus} ZIP: {packagePath}";
            ShowToast("Export package complete.");
        }
        catch (Exception ex)
        {
            var firstRunning = ExportChecklistItems.FirstOrDefault(item => item.IsRunning);
            if (firstRunning is not null)
            {
                FailExportChecklistItem(firstRunning.Id, ex.Message);
            }

            ExportStatus = $"Export failed: {ex.Message}";
            PipelineProgress = "Export failed";
            StatusMessage = ExportStatus;
            ShowToast("Export failed. See status message.");
        }
        finally
        {
            IsExporting = false;
        }
    }

    public void OpenExportOutputPath()
    {
        OpenExportPackagePath();
    }

    public async Task RunPublishToSteamDryRunAsync(bool userConfirmed, CancellationToken cancellationToken = default)
    {
        OpenExportChecklist();

        SteamReadinessReport readinessReport;
        SteamQualityMetrics metrics;
        try
        {
            var metricsPath = ResolveSteamReadinessMetricsPath();
            metrics = SteamReadinessPolicy.LoadMetrics(metricsPath);
            readinessReport = SteamReadinessPolicy.Evaluate(metrics);
            ApplySteamReadinessDetails(readinessReport);
        }
        catch (Exception ex)
        {
            PublishDryRunStatus = $"Publish dry-run unavailable: {ex.Message}";
            ExportStatus = PublishDryRunStatus;
            StatusMessage = PublishDryRunStatus;
            ShowToast("Steam publish dry-run unavailable.");
            return;
        }

        var gate = SteamReadinessPolicy.EvaluatePublishGate(readinessReport, warningAcknowledged: userConfirmed);
        PublishDryRunStatus = gate.Message;
        ExportStatus = $"Publish dry-run: {gate.Message}";
        StatusMessage = ExportStatus;

        if (!userConfirmed)
        {
            ShowToast("Publish confirmation required to continue dry-run.");
            return;
        }

        if (gate.Decision != PublishDecision.Ready)
        {
            ShowToast("Publish dry-run blocked by readiness policy.");
            return;
        }

        var auditOutputPath = ResolvePublishAuditOutputPath();
        var signingKey = SteamReadinessPolicy.EnsureLocalSigningKey();
        var auditTrail = SteamReadinessPolicy.BuildAuditTrail(metrics, readinessReport, signingKey);
        SteamReadinessPolicy.WriteAuditTrail(auditTrail, auditOutputPath);

        PublishDryRunStatus = $"Publish dry-run complete. Audit generated: {auditOutputPath}";
        ExportStatus = PublishDryRunStatus;
        StatusMessage = PublishDryRunStatus;
        ShowToast("Steam publish dry-run completed.");
        await Task.CompletedTask;
    }

    public async Task RunSteamUploadStubAsync(CancellationToken cancellationToken = default)
    {
        if (IsSteamUploadInProgress)
        {
            return;
        }

        IsSteamUploadInProgress = true;
        SteamUploadProgressPercent = 0;
        SteamUploadStatus = "Preparing Steam upload stub bundle...";
        ExportStatus = SteamUploadStatus;
        var timeline = new List<UploadTimelineEntry>();
        string? uploadZipPath = null;
        string? releaseNotesPath = null;

        try
        {
            if (string.IsNullOrWhiteSpace(PrototypeRoot) || PrototypeRoot == "(none)")
            {
                throw new InvalidOperationException("Generate a prototype before running Steam upload.");
            }

            if (!HasExportFolderOutput || !Directory.Exists(ExportFolderPath))
            {
                await RunExportChecklistAsync(cancellationToken);
                if (!HasExportFolderOutput || !Directory.Exists(ExportFolderPath))
                {
                    throw new InvalidOperationException("Steam upload requires export output. Run Export first.");
                }
            }

            releaseNotesPath = WriteSteamReleaseNotesFile(ExportFolderPath);
            timeline.Add(new UploadTimelineEntry(DateTimeOffset.UtcNow, "release_notes", $"Release notes staged: {releaseNotesPath}"));

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            uploadZipPath = Path.Combine(PrototypeRoot, "build", "export", $"steam-upload-stub-{stamp}.zip");
            if (File.Exists(uploadZipPath))
            {
                File.Delete(uploadZipPath);
            }

            ZipFile.CreateFromDirectory(ExportFolderPath, uploadZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            timeline.Add(new UploadTimelineEntry(DateTimeOffset.UtcNow, "package", $"Upload ZIP generated: {uploadZipPath}"));

            var progressStages = new (int Progress, string Message)[]
            {
                (12, "Queued local upload stub."),
                (28, "Validating package manifest + release notes."),
                (46, "Simulating Steam depot chunk upload."),
                (63, "Simulating Steam branch metadata sync."),
                (81, "Simulating publish gate verification."),
                (100, "Steam upload stub completed successfully."),
            };

            foreach (var stage in progressStages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(250, cancellationToken);
                SteamUploadProgressPercent = stage.Progress;
                SteamUploadStatus = stage.Message;
                ExportStatus = $"Upload stub: {stage.Message}";
                timeline.Add(new UploadTimelineEntry(DateTimeOffset.UtcNow, "progress", $"{stage.Progress}% - {stage.Message}"));
            }

            var auditPath = ResolveSteamUploadStubAuditOutputPath();
            var auditPayload = BuildSteamUploadStubAuditPayload(
                success: true,
                uploadZipPath,
                releaseNotesPath,
                timeline,
                errorMessage: null);
            await File.WriteAllTextAsync(auditPath, auditPayload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            SteamUploadAuditPath = auditPath;
            SteamUploadStatus = $"Steam upload stub complete. Audit log: {auditPath}";
            ExportStatus = SteamUploadStatus;
            StatusMessage = $"Steam upload stub succeeded. ZIP: {uploadZipPath}";
            ShowToast("Steam upload stub completed.");
        }
        catch (Exception ex)
        {
            var auditPath = ResolveSteamUploadStubAuditOutputPath();
            var auditPayload = BuildSteamUploadStubAuditPayload(
                success: false,
                uploadZipPath,
                releaseNotesPath,
                timeline,
                errorMessage: ex.Message);
            await File.WriteAllTextAsync(auditPath, auditPayload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            SteamUploadAuditPath = auditPath;
            SteamUploadStatus = $"Steam upload stub failed: {ex.Message}";
            ExportStatus = SteamUploadStatus;
            StatusMessage = SteamUploadStatus;
            ShowToast("Steam upload stub failed.");
        }
        finally
        {
            IsSteamUploadInProgress = false;
        }
    }

    public async Task RunInstallerBuildAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstallerBuildInProgress)
        {
            return;
        }

        OpenExportChecklist();
        IsInstallerBuildInProgress = true;
        InstallerBuildProgressPercent = 3;
        InstallerBuildStatus = "Preparing installer build metadata...";
        ExportStatus = InstallerBuildStatus;
        PipelineProgress = "Installer build started";
        var logLines = new List<string>
        {
            $"[{DateTimeOffset.UtcNow:O}] Installer build triggered from editor shell.",
        };

        try
        {
            var repositoryRoot = ResolveRepositoryRoot();
            var releaseNotesPath = WriteInstallerReleaseNotes(repositoryRoot);
            var auditPath = WriteInstallerAuditTrail(repositoryRoot);
            logLines.Add($"Staged release notes at {releaseNotesPath}");
            logLines.Add($"Staged installer audit at {auditPath}");

            var invocation = ResolvePackagingInvocation(repositoryRoot);
            InstallerBuildStatus = $"Running {invocation.Label} packaging script...";
            ExportStatus = InstallerBuildStatus;
            InstallerBuildProgressPercent = 8;
            await RunPackagingProcessAsync(invocation, logLines, cancellationToken);

            var outputPaths = ResolveInstallerOutputPaths(repositoryRoot, invocation);
            if (outputPaths.Count == 0)
            {
                throw new FileNotFoundException("Packaging script completed but no installer artifact was found.");
            }

            InstallerOutputPath = string.Join(Environment.NewLine, outputPaths);
            InstallerBuildProgressPercent = 100;
            InstallerBuildStatus = $"Installer build complete: {outputPaths.Count} artifact(s) ready.";
            ExportStatus = InstallerBuildStatus;
            StatusMessage = $"{InstallerBuildStatus} Primary: {outputPaths[0]}";
            PipelineProgress = "Installer build complete";
            ShowToast("Installer build complete.");
        }
        catch (Exception ex)
        {
            InstallerBuildProgressPercent = 0;
            InstallerBuildStatus = $"Installer build failed: {ex.Message}";
            ExportStatus = InstallerBuildStatus;
            StatusMessage = InstallerBuildStatus;
            PipelineProgress = "Installer build failed";
            ShowToast("Installer build failed.");
            logLines.Add($"ERROR: {ex.Message}");
        }
        finally
        {
            InstallerBuildLog = string.Join(Environment.NewLine, logLines.TakeLast(140));
            IsInstallerBuildInProgress = false;
        }
    }

    public void OpenExportPackagePath()
    {
        if (!HasExportPackageOutput)
        {
            StatusMessage = "No export ZIP available yet.";
            ShowToast("Run export checklist first.");
            return;
        }

        OpenPath(ExportPackagePath, "Unable to open export ZIP output.");
    }

    public void OpenExportFolderPath()
    {
        if (!HasExportFolderOutput)
        {
            StatusMessage = "No export folder available yet.";
            ShowToast("Run export checklist first.");
            return;
        }

        OpenPath(ExportFolderPath, "Unable to open export folder.");
    }

    public void OpenInstallerOutputPath()
    {
        var outputPaths = InstallerOutputPath
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstOutput = outputPaths.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(firstOutput))
        {
            StatusMessage = "No installer artifact available yet.";
            ShowToast("Build installer first.");
            return;
        }

        OpenPath(firstOutput, "Unable to open installer artifact output.");
    }

    private void OpenPath(string path, string failureToast)
    {
        try
        {
            var revealTarget = Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path) ?? path;
            Process.Start(new ProcessStartInfo
            {
                FileName = revealTarget,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"{failureToast}: {ex.Message}";
            ShowToast(failureToast);
        }
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

    private void ResetExportChecklistItems()
    {
        _exportChecklistItems.Clear();
        _exportChecklistItems.Add(new ExportChecklistItem("validate-scene", "🧪", "Validate scene", "Pending", false, false, false, false, "Validate generated scene scaffold and entity data."));
        _exportChecklistItems.Add(new ExportChecklistItem("export-assets-code", "🗃", "Export assets/code", "Pending", false, false, false, false, "Copy scene, C++ runtime scaffolding, and assets into export folder."));
        _exportChecklistItems.Add(new ExportChecklistItem("package-build", "📦", "Package build", "Pending", false, false, false, false, "Create release ZIP for local distribution."));
        _exportChecklistItems.Add(new ExportChecklistItem("steam-readiness", "🚦", "Steam readiness check", "Pending", false, false, false, false, "Evaluate metrics, policy gate, and warnings for Steam readiness."));
        ExportPackagePath = "Not packaged yet.";
        ExportFolderPath = "Not exported yet.";
        ExportOutputPath = "Not packaged yet.";
        PublishDryRunStatus = "Publish dry-run not started.";
        SteamUploadProgressPercent = 0;
        SteamUploadStatus = "Steam upload stub not started.";
        SteamUploadAuditPath = "No upload audit log yet.";
        InstallerBuildProgressPercent = 0;
        InstallerBuildStatus = "Installer build not started.";
        InstallerOutputPath = "No installer artifact yet.";
        InstallerBuildLog = "Installer logs will appear here.";
        SteamReadinessSummary = "Steam readiness not evaluated yet.";
        SteamReadinessWarnings = "No readiness warnings.";
        _lastSteamReadinessReport = null;
        OnPropertyChanged(nameof(HasSteamReadinessWarnings));
        OnPropertyChanged(nameof(ExportChecklistCompletedCount));
        OnPropertyChanged(nameof(ExportChecklistTotalCount));
        OnPropertyChanged(nameof(ExportChecklistProgressPercent));
    }

    private sealed record PackagingInvocation(
        string Label,
        string ScriptPath,
        string ShellFileName,
        string ShellArguments,
        string RuntimeIdentifier,
        string[] ExpectedExtensions);

    private static PackagingInvocation ResolvePackagingInvocation(string repositoryRoot)
    {
        var scriptsRoot = Path.Combine(repositoryRoot, "scripts");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var script = Path.Combine(scriptsRoot, "package_windows.ps1");
            return new PackagingInvocation(
                "Windows",
                script,
                "powershell",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                "win-x64",
                [".msi"]);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var script = Path.Combine(scriptsRoot, "package_ubuntu.sh");
            return new PackagingInvocation(
                "Ubuntu",
                script,
                "bash",
                $"\"{script}\"",
                "linux-x64",
                [".deb", ".AppImage"]);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var script = Path.Combine(scriptsRoot, "package_macos.sh");
            return new PackagingInvocation(
                "macOS",
                script,
                "bash",
                $"\"{script}\"",
                "osx-arm64",
                [".dmg"]);
        }

        throw new PlatformNotSupportedException("Installer packaging is supported on Windows, Ubuntu, and macOS hosts only.");
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GAMEFORGE_V1_BLUEPRINT.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root.");
    }

    private string WriteInstallerReleaseNotes(string repositoryRoot)
    {
        var installerPayloadRoot = Path.Combine(repositoryRoot, "app", "release");
        Directory.CreateDirectory(installerPayloadRoot);
        var releaseNotesPath = Path.Combine(installerPayloadRoot, "installer_release_notes.txt");
        var notes = string.IsNullOrWhiteSpace(SteamReleaseNotes)
            ? "No release notes provided."
            : SteamReleaseNotes.Trim();
        File.WriteAllText(releaseNotesPath, notes, Encoding.UTF8);
        return releaseNotesPath;
    }

    private string WriteInstallerAuditTrail(string repositoryRoot)
    {
        var installerPayloadRoot = Path.Combine(repositoryRoot, "app", "release");
        Directory.CreateDirectory(installerPayloadRoot);
        var auditPath = Path.Combine(installerPayloadRoot, "installer_build_audit.json");
        var payload = new JsonObject
        {
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["mode"] = "installer-build",
            ["source"] = "editor-shell",
            ["prototypeRoot"] = PrototypeRoot,
            ["exportPackagePath"] = ExportPackagePath,
            ["exportFolderPath"] = ExportFolderPath,
            ["steamReadinessSummary"] = SteamReadinessSummary,
            ["steamReadinessWarnings"] = SteamReadinessWarnings,
            ["steamUploadAuditPath"] = SteamUploadAuditPath,
        };
        File.WriteAllText(auditPath, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return auditPath;
    }

    private async Task RunPackagingProcessAsync(PackagingInvocation invocation, List<string> logLines, CancellationToken cancellationToken)
    {
        if (!File.Exists(invocation.ScriptPath))
        {
            throw new FileNotFoundException("Packaging script not found.", invocation.ScriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ShellFileName,
            Arguments = invocation.ShellArguments,
            WorkingDirectory = Path.GetDirectoryName(invocation.ScriptPath) ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = StreamProcessLinesAsync(process.StandardOutput, logLines, cancellationToken);
        var stderrTask = StreamProcessLinesAsync(process.StandardError, logLines, cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken));
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{invocation.Label} packaging script failed with exit code {process.ExitCode}.");
        }
    }

    private async Task StreamProcessLinesAsync(StreamReader reader, List<string> logLines, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            logLines.Add(line);
            InstallerBuildStatus = line;
            ExportStatus = $"Installer: {line}";
            UpdateInstallerProgressFromLine(line);
        }
    }

    private void UpdateInstallerProgressFromLine(string line)
    {
        var match = Regex.Match(line, "\\[(\\d+)\\/(\\d+)\\]");
        if (!match.Success)
        {
            return;
        }

        var current = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var total = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (total <= 0)
        {
            return;
        }

        var normalized = (int)Math.Round((double)current / total * 90d, MidpointRounding.AwayFromZero);
        InstallerBuildProgressPercent = Math.Clamp(normalized + 8, 8, 98);
    }

    private static List<string> ResolveInstallerOutputPaths(string repositoryRoot, PackagingInvocation invocation)
    {
        var releaseRoot = Path.Combine(repositoryRoot, "build", "release", invocation.RuntimeIdentifier);
        if (!Directory.Exists(releaseRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(releaseRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => invocation.ExpectedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void StartExportChecklistItem(string id, string status)
    {
        UpdateExportChecklistItem(id, status, isRunning: true, isComplete: false, isFailed: false);
    }

    private void CompleteExportChecklistItem(string id, string status, bool hasWarning = false)
    {
        UpdateExportChecklistItem(id, status, isRunning: false, isComplete: true, isFailed: false, hasWarning: hasWarning);
    }

    private void FailExportChecklistItem(string id, string status)
    {
        UpdateExportChecklistItem(id, status, isRunning: false, isComplete: false, isFailed: true, hasWarning: false);
    }

    private void UpdateExportChecklistItem(string id, string status, bool isRunning, bool isComplete, bool isFailed, bool hasWarning = false)
    {
        var index = _exportChecklistItems.ToList().FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        var existing = _exportChecklistItems[index];
        _exportChecklistItems[index] = existing with
        {
            Status = status,
            IsRunning = isRunning,
            IsComplete = isComplete,
            IsFailed = isFailed,
            HasWarning = hasWarning,
        };

        OnPropertyChanged(nameof(ExportChecklistCompletedCount));
        OnPropertyChanged(nameof(ExportChecklistProgressPercent));
    }

    private static string ResolveSteamReadinessMetricsPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var markerPath = Path.Combine(current.FullName, "GAMEFORGE_V1_BLUEPRINT.md");
            if (File.Exists(markerPath))
            {
                var metricsPath = Path.Combine(current.FullName, "docs", "release", "evidence", "readiness_metrics_sample.json");
                if (!File.Exists(metricsPath))
                {
                    throw new FileNotFoundException("Steam readiness metrics fixture not found.", metricsPath);
                }

                return metricsPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root for steam readiness metrics.");
    }

    private static string ResolvePublishAuditOutputPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var markerPath = Path.Combine(current.FullName, "GAMEFORGE_V1_BLUEPRINT.md");
            if (File.Exists(markerPath))
            {
                return Path.Combine(current.FullName, "docs", "release", "evidence", $"steam-readiness-audit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root for steam publish audit output.");
    }

    private static string ResolveSteamUploadStubAuditOutputPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var markerPath = Path.Combine(current.FullName, "GAMEFORGE_V1_BLUEPRINT.md");
            if (File.Exists(markerPath))
            {
                return Path.Combine(current.FullName, "docs", "release", "evidence", $"steam-upload-stub-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root for steam upload audit output.");
    }

    private string WriteSteamReleaseNotesFile(string exportFolder)
    {
        var releaseFolder = Path.Combine(exportFolder, "release");
        Directory.CreateDirectory(releaseFolder);
        var releaseNotesPath = Path.Combine(releaseFolder, "steam_release_notes.txt");
        var notes = string.IsNullOrWhiteSpace(SteamReleaseNotes)
            ? "No release notes provided."
            : SteamReleaseNotes.Trim();
        File.WriteAllText(releaseNotesPath, notes, Encoding.UTF8);
        return releaseNotesPath;
    }

    private JsonObject BuildSteamUploadStubAuditPayload(
        bool success,
        string? uploadZipPath,
        string? releaseNotesPath,
        IReadOnlyList<UploadTimelineEntry> timeline,
        string? errorMessage)
    {
        return new JsonObject
        {
            ["mode"] = "steam-upload-stub",
            ["success"] = success,
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["uploadZipPath"] = uploadZipPath,
            ["releaseNotesPath"] = releaseNotesPath,
            ["publishDryRunStatus"] = PublishDryRunStatus,
            ["steamReadinessSummary"] = SteamReadinessSummary,
            ["error"] = errorMessage,
            ["timeline"] = new JsonArray(timeline.Select(item => new JsonObject
            {
                ["timestampUtc"] = item.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                ["stage"] = item.Stage,
                ["message"] = item.Message,
            }).ToArray()),
        };
    }

    private void ApplySteamReadinessDetails(SteamReadinessReport readinessReport)
    {
        _lastSteamReadinessReport = readinessReport;
        SteamReadinessSummary = $"Readiness score {readinessReport.Score}/100 • Critical {readinessReport.CriticalIssueCount} • Warnings {readinessReport.WarningIssueCount}";
        var warningItems = readinessReport.Checklist
            .Where(item => item.Severity == ReadinessSeverity.Warning && !item.Passed)
            .Select(item => item.Label)
            .ToList();
        SteamReadinessWarnings = warningItems.Count == 0
            ? "No readiness warnings."
            : $"Warnings: {string.Join("; ", warningItems)}";
        OnPropertyChanged(nameof(HasSteamReadinessWarnings));
    }

    private static void CopyDirectory(string sourcePath, string destinationPath, bool includeIfMissing = false)
    {
        if (!Directory.Exists(sourcePath))
        {
            if (includeIfMissing)
            {
                return;
            }

            throw new DirectoryNotFoundException($"Required export folder missing: {sourcePath}");
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var filePath in Directory.EnumerateFiles(sourcePath))
        {
            var targetPath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, targetPath, overwrite: true);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourcePath))
        {
            var childDestination = Path.Combine(destinationPath, Path.GetFileName(childDirectory));
            CopyDirectory(childDirectory, childDestination);
        }
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

    public async Task RefreshImportedAssetsAsync(CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (scenePath is null)
        {
            StatusMessage = "Generate a prototype before refreshing assets.";
            ShowToast("Generate prototype first.");
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(scenePath, cancellationToken);
            using var document = JsonDocument.Parse(content);
            ImportedAssets.Clear();
            LoadImportedAssets(document.RootElement);
            StatusMessage = $"Assets refreshed ({ImportedAssets.Count} loaded).";
            ShowToast("Asset browser refreshed.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh assets: {ex.Message}";
            ShowToast("Asset refresh failed.");
        }
    }

    public bool SetAssetDragGhost(string assetId, float worldX, float worldY)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return false;
        }

        var asset = ImportedAssets.FirstOrDefault(item => string.Equals(item.Id, assetId, StringComparison.Ordinal));
        if (asset is null)
        {
            return false;
        }

        SelectedImportedAsset = asset;
        AssetDragGhostTitle = asset.DisplayName;
        AssetDragGhostPreviewPath = asset.PreviewPath;
        AssetDragGhostKind = asset.Kind;
        AssetDragGhostWorldX = worldX;
        AssetDragGhostWorldY = worldY;
        IsAssetDragGhostVisible = true;
        return true;
    }

    public void ClearAssetDragGhost()
    {
        if (!IsAssetDragGhostVisible)
        {
            return;
        }

        IsAssetDragGhostVisible = false;
        AssetDragGhostTitle = string.Empty;
        AssetDragGhostPreviewPath = string.Empty;
        AssetDragGhostKind = string.Empty;
    }

    public EditorPreferences GetPreferencesSnapshot() => _preferences.Clone();

    public void ApplyPreferencesPreview(EditorPreferences updatedPreferences)
    {
        ApplyPreferencesCore(updatedPreferences, triggerToast: false, statusMessageOverride: null);
    }

    public async Task ApplyAndSavePreferencesAsync(EditorPreferences updatedPreferences, CancellationToken cancellationToken = default)
    {
        ApplyPreferencesCore(updatedPreferences, triggerToast: true, statusMessageOverride: null);
        await _preferences.SaveAsync(_settingsFilePath, cancellationToken);
        StatusMessage = $"Settings saved to {_settingsFilePath}";
    }

    private void ApplyPreferencesCore(EditorPreferences updatedPreferences, bool triggerToast, string? statusMessageOverride)
    {
        _preferences = updatedPreferences.Sanitize();
        EnforceHistoryLimit();
        OnPropertyChanged(nameof(IsAutosaveEnabled));
        OnPropertyChanged(nameof(ThemePreference));
        OnPropertyChanged(nameof(AutosaveStatusLabel));
        OnPropertyChanged(nameof(RuntimePreferencesSummary));
        OnPropertyChanged(nameof(RibbonIconSize));
        OnPropertyChanged(nameof(EditorDefaultTemplateId));
        ThemePreferenceChanged?.Invoke(_preferences.General.Theme);

        if (!string.IsNullOrWhiteSpace(statusMessageOverride))
        {
            StatusMessage = statusMessageOverride;
        }

        if (triggerToast)
        {
            ShowToast("Settings applied.");
        }
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

    private static string BuildTemplatePrompt(ProjectTemplatePreset template, string projectName, string quickConcept)
    {
        var builder = new StringBuilder();
        builder.Append(projectName.Trim());
        builder.Append(" — ");
        builder.Append(template.DisplayName);
        builder.Append(". Core loop: ");
        builder.Append(template.CoreLoop);

        if (!string.IsNullOrWhiteSpace(quickConcept))
        {
            builder.Append(". Concept twist: ");
            builder.Append(quickConcept.Trim());
        }

        builder.Append(". Single-player local-first prototype for Windows + Ubuntu.");
        return builder.ToString();
    }

    private void ApplyRuntimePreview(PipelineRunResponse response)
    {
        StopTimelinePlayback();
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
        RebuildTimelineMarkers();
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

    private void SetTimelineCurrentTime(float value, bool fromPlayback)
    {
        var clamped = Math.Clamp(value, 0f, TimelineDuration);
        if (!SetField(ref _timelineCurrentTime, clamped, nameof(TimelineCurrentTime)))
        {
            return;
        }

        OnPropertyChanged(nameof(TimelineCurrentTimeLabel));
        OnPropertyChanged(nameof(TimelineScrubberPercent));
        ApplyTimelineToViewport(clamped);
        if (fromPlayback)
        {
            TimelineStateLabel = $"Previewing animation at {clamped:0.00}s.";
        }

        RebuildTimelineMarkers();
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
        StopTimelinePlayback();
        ViewportEntities.Clear();
        ImportedAssets.Clear();
        _filteredImportedAssets.Clear();
        _animationTracks.Clear();
        _timelineMarkers.Clear();
        _timelineCurrentTime = 0f;
        OnPropertyChanged(nameof(TimelineCurrentTime));
        OnPropertyChanged(nameof(TimelineCurrentTimeLabel));
        OnPropertyChanged(nameof(TimelineScrubberPercent));
        TimelineStateLabel = "Idle";
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

        var sceneRoot = new HierarchyNode("scene_root", "Scene", "🧭", null, false);
        var npcGroup = new HierarchyNode("group_npcs", "NPCs", "🧍", null, false);
        var propGroup = new HierarchyNode("group_props", "Props", "📦", null, false);
        var miscGroup = new HierarchyNode("group_misc", "Groups", "🧩", null, false);
        sceneRoot.Children.Add(new HierarchyNode("player_root", "Player", "👤", "player_spawn", true));
        sceneRoot.Children.Add(npcGroup);
        sceneRoot.Children.Add(propGroup);
        sceneRoot.Children.Add(miscGroup);

        var entities = ViewportEntities
            .Where(entity => entity.Type != "player")
            .ToList();
        var entityLookup = entities.ToDictionary(entity => entity.Id, entity => entity, StringComparer.Ordinal);
        var childrenByParentId = entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.ParentId))
            .GroupBy(entity => entity.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var attached = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(item => string.IsNullOrWhiteSpace(item.ParentId)))
        {
            var parentGroup = entity.Type switch
            {
                "npc" => npcGroup,
                "prop" => propGroup,
                _ => miscGroup,
            };
            parentGroup.Children.Add(BuildHierarchyEntitySubtree(entity, childrenByParentId, attached, new HashSet<string>(StringComparer.Ordinal)));
        }

        foreach (var orphan in entities.Where(item =>
                     !attached.Contains(item.Id)
                     && !string.IsNullOrWhiteSpace(item.ParentId)
                     && !entityLookup.ContainsKey(item.ParentId!)))
        {
            miscGroup.Children.Add(BuildHierarchyEntitySubtree(orphan, childrenByParentId, attached, new HashSet<string>(StringComparer.Ordinal)));
        }

        _hierarchyRoots.Add(sceneRoot);
        SyncHierarchySelectionFromViewport();
        OnPropertyChanged(nameof(HierarchyRoots));
    }

    private HierarchyNode BuildHierarchyEntitySubtree(
        ViewportEntity entity,
        IReadOnlyDictionary<string, List<ViewportEntity>> childrenByParentId,
        ISet<string> attached,
        ISet<string> activePath)
    {
        var node = HierarchyNode.FromEntity(entity);
        if (!activePath.Add(entity.Id))
        {
            return node;
        }

        attached.Add(entity.Id);
        if (childrenByParentId.TryGetValue(entity.Id, out var children))
        {
            foreach (var child in children)
            {
                node.Children.Add(BuildHierarchyEntitySubtree(child, childrenByParentId, attached, activePath));
            }
        }

        activePath.Remove(entity.Id);
        return node;
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
        RebuildTimelineMarkers();
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
        RebuildTimelineMarkers();
    }

    private void RemoveSelection(ViewportEntity entity)
    {
        entity.IsSelected = false;
        _selectedViewportEntities.Remove(entity);
        SelectedViewportEntity = _selectedViewportEntities.FirstOrDefault();
        SyncHierarchySelectionFromViewport();
        RefreshInspectorDerivedState();
        RebuildTimelineMarkers();
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
                _animationTracks.Remove(target.Id);
                deletedAny = TryDeleteEntityInScene(root, target) || deletedAny;
            }

            if (!deletedAny)
            {
                ShowToast("Selected entity not found in scene.");
                return;
            }

            var afterContent = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            RebuildTimelineMarkers();
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
        EnforceHistoryLimit();
        NotifyHistoryChanged();
        await ApplySceneContentAndRelaunchAsync(scenePath, afterContent, operationDescription, cancellationToken);
    }

    private async Task ApplySceneContentAndRelaunchAsync(string scenePath, string content, string operationDescription, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(scenePath, content, cancellationToken);
        await WriteAutosaveSnapshotAsync(content, cancellationToken);
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

    private void EnforceHistoryLimit()
    {
        var historyLimit = _preferences.Editor.HistoryLength;
        if (_undoStack.Count > historyLimit)
        {
            var retained = _undoStack.Take(historyLimit).ToArray();
            _undoStack.Clear();
            for (var index = retained.Length - 1; index >= 0; index--)
            {
                _undoStack.Push(retained[index]);
            }
        }

        if (_redoStack.Count > historyLimit)
        {
            var retained = _redoStack.Take(historyLimit).ToArray();
            _redoStack.Clear();
            for (var index = retained.Length - 1; index >= 0; index--)
            {
                _redoStack.Push(retained[index]);
            }
        }
    }

    private async Task WriteAutosaveSnapshotAsync(string sceneContent, CancellationToken cancellationToken)
    {
        if (!IsAutosaveEnabled)
        {
            return;
        }

        var settingsRoot = Path.GetDirectoryName(_settingsFilePath) ?? ".forgeengine";
        var autosavePath = Path.Combine(settingsRoot, "autosave", "scene_autosave.json");
        var autosaveDirectory = Path.GetDirectoryName(autosavePath);
        if (!string.IsNullOrWhiteSpace(autosaveDirectory))
        {
            Directory.CreateDirectory(autosaveDirectory);
        }

        await File.WriteAllTextAsync(autosavePath, sceneContent, cancellationToken);
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

    private Task SetTimelineModeAsync(object? parameter)
    {
        if (parameter is not string mode)
        {
            return Task.CompletedTask;
        }

        if (string.Equals(mode, TimelineModePosition, StringComparison.Ordinal)
            || string.Equals(mode, TimelineModeScale, StringComparison.Ordinal)
            || string.Equals(mode, TimelineModeColor, StringComparison.Ordinal))
        {
            TimelineMode = mode;
            RebuildTimelineMarkers();
            TimelineStateLabel = $"{mode} keyframing active.";
        }

        return Task.CompletedTask;
    }

    private Task SetLeftPanelTabAsync(object? parameter)
    {
        if (parameter is not string tab)
        {
            return Task.CompletedTask;
        }

        if (string.Equals(tab, LeftPanelTabHierarchy, StringComparison.Ordinal)
            || string.Equals(tab, LeftPanelTabAssets, StringComparison.Ordinal)
            || string.Equals(tab, LeftPanelTabHistory, StringComparison.Ordinal))
        {
            ActiveLeftPanelTab = tab;
        }

        return Task.CompletedTask;
    }

    private Task AddSelectedEntitiesKeyframeAsync()
    {
        if (_selectedViewportEntities.Count == 0)
        {
            ShowToast("Select at least one entity to keyframe.");
            return Task.CompletedTask;
        }

        foreach (var entity in _selectedViewportEntities)
        {
            var track = GetOrCreateTrack(entity.Id);
            if (string.Equals(TimelineMode, TimelineModePosition, StringComparison.Ordinal))
            {
                UpsertPositionKeyframe(track.PositionFrames, TimelineCurrentTime, entity.X, entity.Y);
                continue;
            }

            if (string.Equals(TimelineMode, TimelineModeScale, StringComparison.Ordinal))
            {
                UpsertScalarKeyframe(track.ScaleFrames, TimelineCurrentTime, entity.Scale);
                continue;
            }

            UpsertColorKeyframe(track.ColorFrames, TimelineCurrentTime, entity.ColorHex);
        }

        RebuildTimelineMarkers();
        TimelineStateLabel = $"{TimelineMode} keyframe captured @ {TimelineCurrentTime:0.00}s.";
        StatusMessage = TimelineStateLabel;
        ShowToast($"{TimelineMode} keyframe saved at {TimelineCurrentTime:0.00}s.");
        return Task.CompletedTask;
    }

    private async Task ToggleTimelinePlaybackAsync()
    {
        if (IsTimelinePlaying)
        {
            await StopTimelinePlaybackAsync(resetTime: false);
            return;
        }

        if (_animationTracks.Count == 0)
        {
            ShowToast("Add keyframes before playing the timeline.");
            return;
        }

        _timelinePlaybackCts = new CancellationTokenSource();
        var token = _timelinePlaybackCts.Token;
        var startTime = TimelineCurrentTime;
        var stopwatch = Stopwatch.StartNew();
        IsTimelinePlaying = true;
        RuntimeLaunchStatus = "Timeline preview playing";
        TimelineStateLabel = IsTimelineLooping
            ? "Playing timeline preview (loop)."
            : "Playing timeline preview (one-shot).";
        StatusMessage = TimelineStateLabel;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var next = startTime + elapsed;
                if (next >= TimelineDurationSeconds)
                {
                    if (IsTimelineLooping)
                    {
                        startTime = 0;
                        stopwatch.Restart();
                        next = 0;
                    }
                    else
                    {
                        TimelineCurrentTime = (float)TimelineDurationSeconds;
                        break;
                    }
                }

                TimelineCurrentTime = (float)next;
                await Task.Delay(33, token);
            }
        }
        catch (TaskCanceledException)
        {
            // no-op
        }
        finally
        {
            IsTimelinePlaying = false;
            TimelineStateLabel = $"Paused at {TimelineCurrentTime:0.00}s.";
            if (string.Equals(RuntimeLaunchStatus, "Timeline preview playing", StringComparison.Ordinal))
            {
                RuntimeLaunchStatus = RuntimePid is int ? "Running" : "Not launched";
            }
        }
    }

    private Task StopTimelinePlaybackAsync(bool resetTime)
    {
        StopTimelinePlayback();
        if (resetTime)
        {
            TimelineCurrentTime = 0;
        }

        TimelineStateLabel = resetTime
            ? "Timeline stopped and rewound."
            : $"Timeline paused at {TimelineCurrentTime:0.00}s.";

        return Task.CompletedTask;
    }

    private void StopTimelinePlayback()
    {
        _timelinePlaybackCts?.Cancel();
        _timelinePlaybackCts?.Dispose();
        _timelinePlaybackCts = null;
        IsTimelinePlaying = false;
    }

    private void ApplyTimelineToViewport(double timeSeconds)
    {
        if (_isTimelineApplyingPose)
        {
            return;
        }

        _isTimelineApplyingPose = true;
        try
        {
            foreach (var entity in ViewportEntities)
            {
                if (!_animationTracks.TryGetValue(entity.Id, out var track))
                {
                    continue;
                }

                if (TrySamplePosition(track.PositionFrames, timeSeconds, out var x, out var y))
                {
                    entity.SetPosition(x, y);
                }

                if (TrySampleScalar(track.ScaleFrames, timeSeconds, out var scale))
                {
                    entity.SetScale(scale);
                }

                if (TrySampleColor(track.ColorFrames, timeSeconds, out var color))
                {
                    entity.SetColorHex(color);
                }
            }

            RefreshInspectorDerivedState();

            RuntimePreviewSummary = IsTimelinePlaying
                ? $"Live in Vulkan (timeline preview @ {TimelineCurrentTime:0.00}s)"
                : RuntimePid is int pid ? $"Live in Vulkan (PID: {pid})" : $"Runtime status: {RuntimeLaunchStatus}";
        }
        finally
        {
            _isTimelineApplyingPose = false;
        }
    }

    private EntityAnimationTrack GetOrCreateTrack(string entityId)
    {
        if (_animationTracks.TryGetValue(entityId, out var existing))
        {
            return existing;
        }

        var track = new EntityAnimationTrack();
        _animationTracks[entityId] = track;
        return track;
    }

    private void RebuildTimelineMarkers()
    {
        _timelineMarkers.Clear();
        var markerFrames = _selectedViewportEntities.Count > 0
            ? _selectedViewportEntities
                .Where(entity => _animationTracks.ContainsKey(entity.Id))
                .SelectMany(entity => ResolveFramesByMode(_animationTracks[entity.Id], TimelineMode))
            : _animationTracks.Values.SelectMany(track => ResolveFramesByMode(track, TimelineMode));

        var uniqueTimes = markerFrames
            .Select(frame => frame.TimeSeconds)
            .Distinct()
            .OrderBy(time => time)
            .ToList();

        foreach (var time in uniqueTimes)
        {
            _timelineMarkers.Add(new TimelineMarker
            {
                TimeSeconds = time,
                TimeLabel = $"{time:0.00}s",
                OffsetPercent = (time / TimelineDurationSeconds) * 100.0,
                IsActive = Math.Abs(time - TimelineCurrentTime) < 0.02,
                Icon = string.Equals(TimelineMode, TimelineModePosition, StringComparison.Ordinal)
                    ? "📍"
                    : string.Equals(TimelineMode, TimelineModeScale, StringComparison.Ordinal) ? "🔳" : "🎨",
            });
        }

        OnPropertyChanged(nameof(HasTimelineKeyframes));
        OnPropertyChanged(nameof(TimelineMarkers));
    }

    private static IEnumerable<IBaseTimelineKeyframe> ResolveFramesByMode(EntityAnimationTrack track, string mode)
        => string.Equals(mode, TimelineModePosition, StringComparison.Ordinal)
            ? track.PositionFrames
            : string.Equals(mode, TimelineModeScale, StringComparison.Ordinal)
                ? track.ScaleFrames
                : track.ColorFrames;

    private static void UpsertPositionKeyframe(IList<PositionKeyframe> frames, double timeSeconds, float x, float y)
    {
        var existing = frames.FirstOrDefault(frame => Math.Abs(frame.TimeSeconds - timeSeconds) < 0.0001);
        if (existing is not null)
        {
            existing.X = x;
            existing.Y = y;
            return;
        }

        frames.Add(new PositionKeyframe { TimeSeconds = timeSeconds, X = x, Y = y });
        SortFrames(frames);
    }

    private static void UpsertScalarKeyframe(IList<ScalarKeyframe> frames, double timeSeconds, float value)
    {
        var existing = frames.FirstOrDefault(frame => Math.Abs(frame.TimeSeconds - timeSeconds) < 0.0001);
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        frames.Add(new ScalarKeyframe { TimeSeconds = timeSeconds, Value = value });
        SortFrames(frames);
    }

    private static void UpsertColorKeyframe(IList<ColorKeyframe> frames, double timeSeconds, string colorHex)
    {
        var sanitized = SanitizeColorHex(colorHex) ?? "#4AA3FF";
        var existing = frames.FirstOrDefault(frame => Math.Abs(frame.TimeSeconds - timeSeconds) < 0.0001);
        if (existing is not null)
        {
            existing.ColorHex = sanitized;
            return;
        }

        frames.Add(new ColorKeyframe { TimeSeconds = timeSeconds, ColorHex = sanitized });
        SortFrames(frames);
    }

    private static void SortFrames<T>(IList<T> frames) where T : IBaseTimelineKeyframe
    {
        var ordered = frames.OrderBy(frame => frame.TimeSeconds).ToList();
        frames.Clear();
        foreach (var frame in ordered)
        {
            frames.Add(frame);
        }
    }

    private static bool TrySamplePosition(IReadOnlyList<PositionKeyframe> frames, double timeSeconds, out float x, out float y)
    {
        if (!TrySampleBounds(frames, timeSeconds, out var previous, out var next))
        {
            x = 0;
            y = 0;
            return false;
        }

        if (ReferenceEquals(previous, next))
        {
            x = previous.X;
            y = previous.Y;
            return true;
        }

        var t = (float)((timeSeconds - previous.TimeSeconds) / (next.TimeSeconds - previous.TimeSeconds));
        x = Lerp(previous.X, next.X, t);
        y = Lerp(previous.Y, next.Y, t);
        return true;
    }

    private static bool TrySampleScalar(IReadOnlyList<ScalarKeyframe> frames, double timeSeconds, out float value)
    {
        if (!TrySampleBounds(frames, timeSeconds, out var previous, out var next))
        {
            value = 1;
            return false;
        }

        value = ReferenceEquals(previous, next)
            ? previous.Value
            : Lerp(previous.Value, next.Value, (float)((timeSeconds - previous.TimeSeconds) / (next.TimeSeconds - previous.TimeSeconds)));
        return true;
    }

    private static bool TrySampleColor(IReadOnlyList<ColorKeyframe> frames, double timeSeconds, out string colorHex)
    {
        if (!TrySampleBounds(frames, timeSeconds, out var previous, out var next))
        {
            colorHex = string.Empty;
            return false;
        }

        if (ReferenceEquals(previous, next))
        {
            colorHex = previous.ColorHex;
            return true;
        }

        var start = Color.Parse(previous.ColorHex);
        var end = Color.Parse(next.ColorHex);
        var t = (float)((timeSeconds - previous.TimeSeconds) / (next.TimeSeconds - previous.TimeSeconds));
        var lerped = Color.FromRgb(
            (byte)Math.Clamp(Math.Round(Lerp(start.R, end.R, t)), 0, 255),
            (byte)Math.Clamp(Math.Round(Lerp(start.G, end.G, t)), 0, 255),
            (byte)Math.Clamp(Math.Round(Lerp(start.B, end.B, t)), 0, 255));
        colorHex = $"#{lerped.R:X2}{lerped.G:X2}{lerped.B:X2}";
        return true;
    }

    private static bool TrySampleBounds<T>(IReadOnlyList<T> frames, double timeSeconds, out T previous, out T next) where T : IBaseTimelineKeyframe
    {
        previous = default!;
        next = default!;
        if (frames.Count == 0)
        {
            return false;
        }

        if (timeSeconds <= frames[0].TimeSeconds)
        {
            previous = frames[0];
            next = frames[0];
            return true;
        }

        var last = frames[^1];
        if (timeSeconds >= last.TimeSeconds)
        {
            previous = last;
            next = last;
            return true;
        }

        for (var index = 0; index < frames.Count - 1; index++)
        {
            var current = frames[index];
            var upcoming = frames[index + 1];
            if (timeSeconds < current.TimeSeconds || timeSeconds > upcoming.TimeSeconds)
            {
                continue;
            }

            previous = current;
            next = upcoming;
            return true;
        }

        previous = last;
        next = last;
        return true;
    }

    private static float Lerp(float start, float end, float t)
        => start + ((end - start) * Math.Clamp(t, 0f, 1f));

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
                _animationTracks.Remove(item.Id);
            }
        }

        RebuildTimelineMarkers();
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

        ApplyAssetFilter();
        SelectedImportedAsset = FilteredImportedAssets.FirstOrDefault() ?? ImportedAssets.FirstOrDefault();
    }

    private void OnImportedAssetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyAssetFilter();
    }

    private void ApplyAssetFilter()
    {
        var query = AssetSearchText.Trim();
        var next = string.IsNullOrWhiteSpace(query)
            ? ImportedAssets.ToList()
            : ImportedAssets
                .Where(asset => asset.SearchCorpus.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _filteredImportedAssets.Clear();
        foreach (var asset in next)
        {
            _filteredImportedAssets.Add(asset);
        }

        OnPropertyChanged(nameof(HasAssetResults));
        OnPropertyChanged(nameof(HasNoAssetResults));
        OnPropertyChanged(nameof(AssetResultsSummary));
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

    private sealed class EntityAnimationTrack
    {
        public List<PositionKeyframe> PositionFrames { get; } = new();
        public List<ScalarKeyframe> ScaleFrames { get; } = new();
        public List<ColorKeyframe> ColorFrames { get; } = new();
    }

    public sealed class TimelineMarker
    {
        public double TimeSeconds { get; init; }

        public string TimeLabel { get; init; } = string.Empty;

        public double OffsetPercent { get; init; }

        public bool IsActive { get; init; }

        public string Icon { get; init; } = "•";
    }

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
            => new(entity.Id, entity.DisplayName, ResolveIcon(entity.Type), entity.Id, true);

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

    private interface IBaseTimelineKeyframe
    {
        double TimeSeconds { get; set; }
    }

    private sealed class PositionKeyframe : IBaseTimelineKeyframe
    {
        public double TimeSeconds { get; set; }

        public float X { get; set; }

        public float Y { get; set; }
    }

    private sealed class ScalarKeyframe : IBaseTimelineKeyframe
    {
        public double TimeSeconds { get; set; }

        public float Value { get; set; }
    }

    private sealed class ColorKeyframe : IBaseTimelineKeyframe
    {
        public double TimeSeconds { get; set; }

        public string ColorHex { get; set; } = "#4AA3FF";
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

        public bool IsTexture => string.Equals(Kind, ImportedAssetKind.Texture, StringComparison.OrdinalIgnoreCase);

        public bool IsModel => string.Equals(Kind, ImportedAssetKind.Model, StringComparison.OrdinalIgnoreCase);

        public string PreviewPath => IsTexture
            ? SourcePath
            : string.Empty;

        public bool HasPreviewImage => !string.IsNullOrWhiteSpace(PreviewPath);

        public bool HasNoPreviewImage => !HasPreviewImage;

        public string ThumbnailGlyph => IsModel
            ? "🧊"
            : "🖼";

        public string KindLabel => IsModel
            ? "OBJ Model"
            : "PNG Texture";

        public string ThumbnailBadge => IsModel ? "OBJ" : "PNG";

        public string SearchCorpus => $"{DisplayName} {Kind} {KindLabel} {Id} {SourcePath}";
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

        string CreateBriefFromTemplate(ProjectTemplatePreset template, string projectName, string quickConcept);
    }

    public sealed record ProjectTemplatePreset(
        string Id,
        string DisplayName,
        string Icon,
        string BriefSummary,
        string CoreLoop);

    public sealed record ExportChecklistItem(
        string Id,
        string Icon,
        string Label,
        string Status,
        bool IsComplete,
        bool IsFailed,
        bool IsRunning,
        bool HasWarning,
        string Tooltip)
    {
        public string ProgressIcon => IsComplete
            ? "✅"
            : IsFailed
                ? "❌"
                : IsRunning
                    ? "⏳"
                    : "◻";

        public string SeverityIcon => IsFailed
            ? "⛔"
            : HasWarning
                ? "⚠️"
                : IsComplete
                    ? "✅"
                    : "•";
    }

    private sealed record UploadTimelineEntry(DateTimeOffset TimestampUtc, string Stage, string Message);

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

        public string CreateBriefFromTemplate(ProjectTemplatePreset template, string projectName, string quickConcept)
            => OrchestratorClient.CreateBriefFromTemplate(template.Id, template.DisplayName, template.CoreLoop, projectName, quickConcept);
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
