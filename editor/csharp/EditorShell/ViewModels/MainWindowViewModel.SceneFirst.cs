using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string AssetLibraryTabApproved = "Approved";
    private const string AssetLibraryTabPending = "Pending";
    private const string AssetLibraryTabRejected = "Rejected";
    internal const string PanelPlacementLeft = "Left";
    internal const string PanelPlacementRight = "Right";
    internal const string PanelPlacementBottom = "Bottom";
    internal const string PanelPlacementFloat = "Float";
    internal const string PanelPlacementHidden = "Hidden";

    private readonly ObservableCollection<AssetLibraryItem> _approvedAssetLibrary = new();
    private readonly ObservableCollection<AssetLibraryItem> _pendingAssetLibrary = new();
    private readonly ObservableCollection<AssetLibraryItem> _rejectedAssetLibrary = new();
    private readonly ObservableCollection<InterviewHistoryItem> _aiInterviewHistory = new();
    private ReadOnlyObservableCollection<AssetLibraryItem>? _readonlyApprovedAssetLibrary;
    private ReadOnlyObservableCollection<AssetLibraryItem>? _readonlyPendingAssetLibrary;
    private ReadOnlyObservableCollection<AssetLibraryItem>? _readonlyRejectedAssetLibrary;
    private ReadOnlyObservableCollection<InterviewHistoryItem>? _readonlyAiInterviewHistory;

    private string _activeScenePath = string.Empty;
    private string _projectRootPath = string.Empty;
    private bool _isSceneDirty;
    private string _sceneValidationStatus = "Validation: Ready";
    private string _assetGenerationPrompt = string.Empty;
    private string _assetGenerationType = "sprite";
    private bool _isAssetGenerationInProgress;
    private string _assetGenerationStatus = "Idle";
    private string _activeAssetLibraryTab = AssetLibraryTabApproved;
    private string _assetLibraryViewMode = "Grid";
    private AssetLibraryItem? _selectedAssetLibraryItem;
    private bool _isAssetPreviewStacked;
    private string _assetsPanelPlacement = PanelPlacementLeft;
    private string _aiInterviewPlacement = PanelPlacementHidden;
    private int _toastSequence;
    private string _aiInterviewHeader = "Deep AI Interview - Current Project";
    private string _aiInterviewQuestion = "What should this project focus on next?";
    private string _aiInterviewAnswer = string.Empty;
    private int _aiInterviewCurrentStep = 1;
    private bool _isAiInterviewSuggestionsVisible;
    private string _aiInterviewSuggestionOne = "Clarify the player fantasy and the first 10 minutes.";
    private string _aiInterviewSuggestionTwo = "Describe the most important scene and its key interactions.";
    private string _aiInterviewSuggestionThree = "List three constraints that should shape generation.";

    private void InitializeSceneFirstState()
    {
        _readonlyApprovedAssetLibrary = new ReadOnlyObservableCollection<AssetLibraryItem>(_approvedAssetLibrary);
        _readonlyPendingAssetLibrary = new ReadOnlyObservableCollection<AssetLibraryItem>(_pendingAssetLibrary);
        _readonlyRejectedAssetLibrary = new ReadOnlyObservableCollection<AssetLibraryItem>(_rejectedAssetLibrary);
        _readonlyAiInterviewHistory = new ReadOnlyObservableCollection<InterviewHistoryItem>(_aiInterviewHistory);
        RefreshSceneStatus();
        RefreshAiInterviewHeader();
        _ = RefreshAssetLibraryAsync();
    }

    public string ActiveScenePath
    {
        get => _activeScenePath;
        private set
        {
            if (!SetField(ref _activeScenePath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasActiveScenePath));
            OnPropertyChanged(nameof(IsChatCopilotPanelVisible));
            OnPropertyChanged(nameof(SceneNameLabel));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(CanSaveScene));
        }
    }

    public string ProjectRootPath
    {
        get => _projectRootPath;
        private set
        {
            if (!SetField(ref _projectRootPath, value))
            {
                return;
            }

            RefreshAiInterviewHeader();
            OnPropertyChanged(nameof(HasProjectRootPath));
            OnPropertyChanged(nameof(ProjectRootNameLabel));
            OnPropertyChanged(nameof(CanOpenProjectFolder));
        }
    }

    public bool HasActiveScenePath => !string.IsNullOrWhiteSpace(ActiveScenePath);
    public bool HasProjectRootPath => !string.IsNullOrWhiteSpace(ProjectRootPath);
    public bool CanOpenProjectFolder => HasProjectRootPath;
    public bool CanSaveScene => HasActiveScenePath;
    public string SceneNameLabel => HasActiveScenePath ? Path.GetFileName(ActiveScenePath) : "No scene";
    public string ProjectRootNameLabel => HasProjectRootPath ? Path.GetFileName(ProjectRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : "No project";
    public string WindowTitle => HasActiveScenePath ? $"{SceneNameLabel} - ForgeEngine Editor" : "ForgeEngine Editor";

    public bool IsChatCopilotPanelVisible => HasActiveScenePath && IsCreatorModeEnabled;

    public bool IsSceneDirty
    {
        get => _isSceneDirty;
        private set
        {
            if (!SetField(ref _isSceneDirty, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SceneDirtyLabel));
        }
    }

    public string SceneDirtyLabel => IsSceneDirty ? "Dirty" : "Saved";

    public string SceneValidationStatus
    {
        get => _sceneValidationStatus;
        private set => SetField(ref _sceneValidationStatus, value);
    }

    public string FpsStatusLabel => $"{_preferences.Runtime.FpsLimit} FPS";
    public string NpcCountStatusLabel => $"{ViewportEntities.Count(entity => string.Equals(entity.Type, "npc", StringComparison.OrdinalIgnoreCase))} NPCs";
    public string SceneStatusLine => $"{SceneNameLabel}  •  {SceneDirtyLabel}  •  {SceneValidationStatus}  •  {FpsStatusLabel}  •  {NpcCountStatusLabel}";

    public string AssetGenerationPrompt
    {
        get => _assetGenerationPrompt;
        set => SetField(ref _assetGenerationPrompt, value);
    }

    public string AssetGenerationType
    {
        get => _assetGenerationType;
        set => SetField(ref _assetGenerationType, NormalizeAssetGenerationType(value));
    }

    public IReadOnlyList<string> AssetGenerationTypeOptions { get; } = ["sprite", "texture", "ui"];

    public bool IsAssetGenerationInProgress
    {
        get => _isAssetGenerationInProgress;
        private set
        {
            if (!SetField(ref _isAssetGenerationInProgress, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAssetGenerationIdle));
        }
    }

    public bool IsAssetGenerationIdle => !IsAssetGenerationInProgress;

    public string AssetGenerationStatus
    {
        get => _assetGenerationStatus;
        private set => SetField(ref _assetGenerationStatus, value);
    }

    public string ActiveAssetLibraryTab
    {
        get => _activeAssetLibraryTab;
        set
        {
            var normalized = NormalizeAssetTab(value);
            if (!SetField(ref _activeAssetLibraryTab, normalized))
            {
                return;
            }

            EnsureSelectedAssetLibraryItem();
            OnPropertyChanged(nameof(IsApprovedAssetTabActive));
            OnPropertyChanged(nameof(IsPendingAssetTabActive));
            OnPropertyChanged(nameof(IsRejectedAssetTabActive));
            OnPropertyChanged(nameof(VisibleAssetLibraryItems));
            OnPropertyChanged(nameof(VisibleAssetLibrarySummary));
            OnPropertyChanged(nameof(IsAssetsPanelVisibleInLeftDock));
            OnPropertyChanged(nameof(IsAssetsPanelNoticeVisibleInLeftDock));
        }
    }

    public bool IsApprovedAssetTabActive => string.Equals(ActiveAssetLibraryTab, AssetLibraryTabApproved, StringComparison.Ordinal);
    public bool IsPendingAssetTabActive => string.Equals(ActiveAssetLibraryTab, AssetLibraryTabPending, StringComparison.Ordinal);
    public bool IsRejectedAssetTabActive => string.Equals(ActiveAssetLibraryTab, AssetLibraryTabRejected, StringComparison.Ordinal);

    public string AssetLibraryViewMode
    {
        get => _assetLibraryViewMode;
        set
        {
            var normalized = string.Equals(value, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
            if (!SetField(ref _assetLibraryViewMode, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAssetGridView));
            OnPropertyChanged(nameof(IsAssetListView));
        }
    }

    public bool IsAssetGridView => string.Equals(AssetLibraryViewMode, "Grid", StringComparison.Ordinal);
    public bool IsAssetListView => !IsAssetGridView;

    public ReadOnlyObservableCollection<AssetLibraryItem> ApprovedAssetLibrary => _readonlyApprovedAssetLibrary!;
    public ReadOnlyObservableCollection<AssetLibraryItem> PendingAssetLibrary => _readonlyPendingAssetLibrary!;
    public ReadOnlyObservableCollection<AssetLibraryItem> RejectedAssetLibrary => _readonlyRejectedAssetLibrary!;
    public IEnumerable<AssetLibraryItem> VisibleAssetLibraryItems => (ActiveAssetLibraryTab switch
    {
        AssetLibraryTabPending => PendingAssetLibrary,
        AssetLibraryTabRejected => RejectedAssetLibrary,
        _ => ApprovedAssetLibrary,
    }).Where(item => item.Matches(AssetSearchText, SelectedAssetKindFilter));

    public string VisibleAssetLibrarySummary => $"{VisibleAssetLibraryItems.Count()} items - {ActiveAssetLibraryTab}";

    public AssetLibraryItem? SelectedAssetLibraryItem
    {
        get => _selectedAssetLibraryItem;
        set
        {
            if (!SetField(ref _selectedAssetLibraryItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedAssetLibraryItem));
            OnPropertyChanged(nameof(HasNoSelectedAssetLibraryItem));
            OnPropertyChanged(nameof(SelectedAssetLibraryPreviewPath));
            OnPropertyChanged(nameof(SelectedAssetLibraryPreviewHasImage));
            OnPropertyChanged(nameof(SelectedAssetLibraryMetadata));
            OnPropertyChanged(nameof(SelectedAssetLibraryPrompt));
            OnPropertyChanged(nameof(CanApproveSelectedAsset));
            OnPropertyChanged(nameof(CanRejectSelectedAsset));
            OnPropertyChanged(nameof(CanRegenerateSelectedAsset));
            OnPropertyChanged(nameof(CanDeleteSelectedAsset));
        }
    }

    public bool HasSelectedAssetLibraryItem => SelectedAssetLibraryItem is not null;
    public bool HasNoSelectedAssetLibraryItem => !HasSelectedAssetLibraryItem;
    public string SelectedAssetLibraryPreviewPath => SelectedAssetLibraryItem?.AssetPath ?? string.Empty;
    public bool SelectedAssetLibraryPreviewHasImage => HasPreviewImage(SelectedAssetLibraryPreviewPath);
    public string SelectedAssetLibraryPrompt => SelectedAssetLibraryItem?.Prompt ?? "Select an asset to preview.";
    public string SelectedAssetLibraryMetadata => SelectedAssetLibraryItem is null
        ? "No asset selected."
        : $"File: {SelectedAssetLibraryItem.DisplayName}\nType: {SelectedAssetLibraryItem.AssetType}\nStatus: {SelectedAssetLibraryItem.StatusBadge}\nQuality: {SelectedAssetLibraryItem.QualityScoreLabel}\nMetadata: {SelectedAssetLibraryItem.MetadataPath}\nSource: {SelectedAssetLibraryItem.SourceFolder}";
    public bool CanApproveSelectedAsset => SelectedAssetLibraryItem is { CanApprove: true };
    public bool CanRejectSelectedAsset => SelectedAssetLibraryItem is { CanReject: true };
    public bool CanRegenerateSelectedAsset => SelectedAssetLibraryItem is { CanRegenerate: true };
    public bool CanDeleteSelectedAsset => SelectedAssetLibraryItem is not null;

    public bool IsAssetPreviewStacked
    {
        get => _isAssetPreviewStacked;
        set
        {
            if (!SetField(ref _isAssetPreviewStacked, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAssetPreviewSideBySide));
        }
    }

    public bool IsAssetPreviewSideBySide => !IsAssetPreviewStacked;

    public string AssetsPanelPlacement
    {
        get => _assetsPanelPlacement;
        set
        {
            var normalized = NormalizePanelPlacement(value, PanelPlacementLeft);
            if (!SetField(ref _assetsPanelPlacement, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAssetsPanelInLeft));
            OnPropertyChanged(nameof(IsAssetsPanelInRight));
            OnPropertyChanged(nameof(IsAssetsPanelInBottom));
            OnPropertyChanged(nameof(IsAssetsPanelFloating));
            OnPropertyChanged(nameof(IsAssetsPanelHidden));
            OnPropertyChanged(nameof(IsAssetsPanelVisibleInLeftDock));
            OnPropertyChanged(nameof(IsAssetsPanelNoticeVisibleInLeftDock));
        }
    }

    public string AiInterviewPlacement
    {
        get => _aiInterviewPlacement;
        set
        {
            var normalized = NormalizePanelPlacement(value, PanelPlacementBottom);
            if (!SetField(ref _aiInterviewPlacement, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAiInterviewInBottom));
            OnPropertyChanged(nameof(IsAiInterviewInRight));
            OnPropertyChanged(nameof(IsAiInterviewFloating));
            OnPropertyChanged(nameof(IsAiInterviewHidden));
        }
    }

    public bool IsAssetsPanelInLeft => string.Equals(AssetsPanelPlacement, PanelPlacementLeft, StringComparison.Ordinal);
    public bool IsAssetsPanelInRight => string.Equals(AssetsPanelPlacement, PanelPlacementRight, StringComparison.Ordinal);
    public bool IsAssetsPanelInBottom => string.Equals(AssetsPanelPlacement, PanelPlacementBottom, StringComparison.Ordinal);
    public bool IsAssetsPanelFloating => string.Equals(AssetsPanelPlacement, PanelPlacementFloat, StringComparison.Ordinal);
    public bool IsAssetsPanelHidden => string.Equals(AssetsPanelPlacement, PanelPlacementHidden, StringComparison.Ordinal);
    public bool IsAssetsPanelVisibleInLeftDock => IsAssetsPanelInLeft && IsAssetsTabActive;
    public bool IsAssetsPanelNoticeVisibleInLeftDock => !IsAssetsPanelInLeft && IsAssetsTabActive;
    public bool IsAiInterviewInBottom => string.Equals(AiInterviewPlacement, PanelPlacementBottom, StringComparison.Ordinal);
    public bool IsAiInterviewInRight => string.Equals(AiInterviewPlacement, PanelPlacementRight, StringComparison.Ordinal);
    public bool IsAiInterviewFloating => string.Equals(AiInterviewPlacement, PanelPlacementFloat, StringComparison.Ordinal);
    public bool IsAiInterviewHidden => string.Equals(AiInterviewPlacement, PanelPlacementHidden, StringComparison.Ordinal);

    public string AiInterviewHeader
    {
        get => _aiInterviewHeader;
        private set => SetField(ref _aiInterviewHeader, value);
    }

    public string AiInterviewProgressLabel => $"Step {_aiInterviewCurrentStep} of 7";
    public double AiInterviewProgressValue => _aiInterviewCurrentStep;
    public double AiInterviewProgressMaximum => 7d;

    public string AiInterviewQuestion
    {
        get => _aiInterviewQuestion;
        private set => SetField(ref _aiInterviewQuestion, value);
    }

    public string AiInterviewAnswer
    {
        get => _aiInterviewAnswer;
        set => SetField(ref _aiInterviewAnswer, value);
    }

    public bool IsAiInterviewSuggestionsVisible
    {
        get => _isAiInterviewSuggestionsVisible;
        private set => SetField(ref _isAiInterviewSuggestionsVisible, value);
    }

    public string AiInterviewSuggestionOne
    {
        get => _aiInterviewSuggestionOne;
        private set => SetField(ref _aiInterviewSuggestionOne, value);
    }

    public string AiInterviewSuggestionTwo
    {
        get => _aiInterviewSuggestionTwo;
        private set => SetField(ref _aiInterviewSuggestionTwo, value);
    }

    public string AiInterviewSuggestionThree
    {
        get => _aiInterviewSuggestionThree;
        private set => SetField(ref _aiInterviewSuggestionThree, value);
    }

    public ReadOnlyObservableCollection<InterviewHistoryItem> AiInterviewHistory => _readonlyAiInterviewHistory!;

    public async Task CreateNewSceneAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new InvalidOperationException("Project root is required.");
        }

        EnsureSceneProjectFolders(projectRoot);
        var scenesFolder = Path.Combine(projectRoot, "Scenes");
        var scenePath = GetUniqueScenePath(scenesFolder, "Untitled Scene");
        await File.WriteAllTextAsync(scenePath, BuildNewSceneScaffold(), cancellationToken);
        SetSceneContext(scenePath);
        ResetSceneSelectionState();
        LoadViewportEntitiesFromScene(PrototypeRoot);
        IsSceneDirty = false;
        StatusMessage = $"Scene created: {scenePath}";
        ShowToast("Scene created.");
    }

    public async Task OpenSceneAsync(string scenePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
        {
            throw new FileNotFoundException("Scene file not found.", scenePath);
        }

        await using var stream = File.OpenRead(scenePath);
        using var _ = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        SetSceneContext(scenePath);
        ResetSceneSelectionState();
        LoadViewportEntitiesFromScene(PrototypeRoot);
        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);
        RuntimeLaunchStatus = "Scene loaded";
        RuntimePreviewSummary = "Scene loaded. Click Play Runtime for live preview.";
        IsSceneDirty = false;
        StatusMessage = $"Scene opened: {scenePath}";
        ShowToast("Scene opened.");
    }

    public async Task<bool> SaveSceneAsync(CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            StatusMessage = "Save failed: no active scene.";
            ShowFailureToast("Save failed", "There is no active scene to save.", "Create or open a scene first, then retry.");
            return false;
        }

        if (!File.Exists(scenePath))
        {
            StatusMessage = $"Save failed: scene file missing ({scenePath}).";
            ShowFailureToast("Save failed", "The active scene file could not be found.", "Use Save Scene As to create a new file or reopen the scene and retry.");
            return false;
        }

        try
        {
            var content = await File.ReadAllTextAsync(scenePath, cancellationToken);
            await File.WriteAllTextAsync(scenePath, content, cancellationToken);
            await WriteAutosaveSnapshotAsync(content, cancellationToken);
            IsSceneDirty = false;
            RefreshSceneStatus();
            StatusMessage = $"Scene saved: {scenePath}";
            ShowToast("Scene saved.");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            ShowFailureToast("Save failed", "The active scene could not be written.", "Check file permissions and scene path validity, then retry.");
            return false;
        }
    }

    public async Task<bool> SaveSceneAsAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            StatusMessage = "Save failed: no active scene.";
            ShowFailureToast("Save failed", "There is no active scene to save.", "Create or open a scene first, then retry.");
            return false;
        }

        if (!File.Exists(scenePath))
        {
            StatusMessage = $"Save failed: scene file missing ({scenePath}).";
            ShowFailureToast("Save failed", "The active scene file could not be found.", "Use Save Scene As to create a new file or reopen the scene and retry.");
            return false;
        }

        try
        {
            var content = await File.ReadAllTextAsync(scenePath, cancellationToken);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
            SetSceneContext(targetPath);
            IsSceneDirty = false;
            RefreshSceneStatus();
            StatusMessage = $"Scene saved as: {targetPath}";
            ShowToast("Scene saved as.");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            ShowFailureToast("Save failed", "The scene could not be saved to the selected location.", "Check the target path and write permissions, then retry.");
            return false;
        }
    }

    public void OpenProjectFolder()
    {
        if (!HasProjectRootPath || !Directory.Exists(ProjectRootPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = ProjectRootPath,
            UseShellExecute = true,
        });
    }

    public async Task GenerateAssetAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(AssetGenerationPrompt))
        {
            ShowToast("Enter an asset prompt first.");
            return;
        }

        var orchestratorPath = Path.Combine(Environment.CurrentDirectory, "ai-orchestration", "python", "orchestrator.py");
        if (!File.Exists(orchestratorPath))
        {
            ShowFailureToast("Generation unavailable", "orchestrator.py was not found.", "Check the local AI orchestration checkout and retry.");
            return;
        }

        IsAssetGenerationInProgress = true;
        AssetGenerationStatus = "Generating...";
        try
        {
            var projectRoot = ResolveProjectRootForSceneOperations();
            EnsureSceneProjectFolders(projectRoot);
            var repositoryRoot = ResolveRepositoryRoot();
            var fileName = PythonEnvironment.ResolvePythonExecutable(repositoryRoot);
            var artBiblePath = Path.Combine(projectRoot, "art_bible.json");
            var arguments = $"\"{orchestratorPath}\" generate-asset \"{AssetGenerationPrompt}\" {AssetGenerationType} 1 \"{artBiblePath}\"";
            var result = await _runtimeSupervisor.RunProcessAsync(fileName, arguments, repositoryRoot, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);
            }

            AssetGenerationStatus = "Refreshing library...";
            await RefreshAssetLibraryAsync(cancellationToken);
            ActiveAssetLibraryTab = AssetLibraryTabPending;
            StatusMessage = "Asset generated.";
            ShowToast("Asset generated.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Asset generation failed: {ex.Message}";
            ShowFailureToast("Asset generation failed", "The asset request did not complete.", "Check the prompt, local model availability, and orchestration logs, then retry.");
        }
        finally
        {
            IsAssetGenerationInProgress = false;
            AssetGenerationStatus = "Idle";
        }
    }

    public Task RefreshAssetLibraryAsync(CancellationToken cancellationToken = default)
    {
        _approvedAssetLibrary.Clear();
        _pendingAssetLibrary.Clear();
        _rejectedAssetLibrary.Clear();

        var projectRoot = ResolveProjectRootForSceneOperations();
        LoadAssetLibraryFolder(Path.Combine(projectRoot, "Assets", "Approved"), "approved", _approvedAssetLibrary, cancellationToken);
        LoadAssetLibraryFolder(Path.Combine(projectRoot, "Assets", "Generated"), "pending-review", _pendingAssetLibrary, cancellationToken);
        LoadAssetLibraryFolder(Path.Combine(projectRoot, "Assets", "Rejected"), "rejected", _rejectedAssetLibrary, cancellationToken);

        EnsureSelectedAssetLibraryItem();
        _assetCatalogLastRefreshedAtUtc = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(AssetLastRefreshLabel));
        OnPropertyChanged(nameof(VisibleAssetLibraryItems));
        OnPropertyChanged(nameof(VisibleAssetLibrarySummary));
        return Task.CompletedTask;
    }

    public async Task ApproveSelectedAssetAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAssetLibraryItem is null || !SelectedAssetLibraryItem.CanApprove)
        {
            return;
        }

        await ReviewGeneratedAssetAsync(SelectedAssetLibraryItem.AssetPath, "approve", cancellationToken);
        await RefreshAssetLibraryAsync(cancellationToken);
        ActiveAssetLibraryTab = AssetLibraryTabApproved;
    }

    public async Task RejectSelectedAssetAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAssetLibraryItem is null || !SelectedAssetLibraryItem.CanReject)
        {
            return;
        }

        await ReviewGeneratedAssetAsync(SelectedAssetLibraryItem.AssetPath, "reject", cancellationToken);
        await RefreshAssetLibraryAsync(cancellationToken);
        ActiveAssetLibraryTab = AssetLibraryTabRejected;
    }

    public async Task RegenerateSelectedAssetAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAssetLibraryItem is null || !SelectedAssetLibraryItem.CanRegenerate)
        {
            return;
        }

        AssetGenerationPrompt = SelectedAssetLibraryItem.Prompt;
        AssetGenerationType = SelectedAssetLibraryItem.AssetType;
        await ReviewGeneratedAssetAsync(SelectedAssetLibraryItem.AssetPath, "regenerate", cancellationToken);
        await RefreshAssetLibraryAsync(cancellationToken);
        ActiveAssetLibraryTab = AssetLibraryTabPending;
    }

    public async Task DeleteSelectedAssetAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAssetLibraryItem is null)
        {
            return;
        }

        if (File.Exists(SelectedAssetLibraryItem.AssetPath))
        {
            File.Delete(SelectedAssetLibraryItem.AssetPath);
        }

        if (!string.IsNullOrWhiteSpace(SelectedAssetLibraryItem.MetadataPath) && File.Exists(SelectedAssetLibraryItem.MetadataPath))
        {
            File.Delete(SelectedAssetLibraryItem.MetadataPath);
        }

        await RefreshAssetLibraryAsync(cancellationToken);
        ShowToast("Asset deleted.");
    }

    public void ShowAiInterviewSuggestions()
    {
        IsAiInterviewSuggestionsVisible = true;
    }

    public void ApplyAiInterviewSuggestion(string suggestion)
    {
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            AiInterviewAnswer = suggestion;
        }
    }

    public void SubmitAiInterviewAnswer()
    {
        if (string.IsNullOrWhiteSpace(AiInterviewAnswer))
        {
            ShowToast("Enter an answer before sending.");
            return;
        }

        _aiInterviewHistory.Insert(0, new InterviewHistoryItem(_aiInterviewCurrentStep, AiInterviewQuestion, AiInterviewAnswer.Trim(), "Captured for the next orchestration pass."));
        _aiInterviewCurrentStep = Math.Min(_aiInterviewCurrentStep + 1, 7);
        AiInterviewQuestion = _aiInterviewCurrentStep switch
        {
            2 => "What should the active scene communicate visually?",
            3 => "Which assets should be generated or reviewed next?",
            4 => "What interaction or build-mode behavior matters most?",
            5 => "Which systems need the strongest inspector support?",
            6 => "What should the validation/status bar emphasize?",
            7 => "What would make the editor feel finished for daily use?",
            _ => "Interview complete. Review the captured answers below.",
        };
        AiInterviewAnswer = string.Empty;
        IsAiInterviewSuggestionsVisible = false;
        OnPropertyChanged(nameof(AiInterviewProgressLabel));
        OnPropertyChanged(nameof(AiInterviewProgressValue));
    }

    internal string GetEffectiveSettingsPath()
    {
        var projectRoot = ResolveProjectRootForSceneOperations();
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return _settingsFilePath;
        }

        return Path.Combine(projectRoot, ".soulloom", "settings.json");
    }

    private static string NormalizeAssetGenerationType(string? value)
    {
        if (string.Equals(value, "texture", StringComparison.OrdinalIgnoreCase))
        {
            return "texture";
        }

        if (string.Equals(value, "ui", StringComparison.OrdinalIgnoreCase))
        {
            return "ui";
        }

        return "sprite";
    }

    private static string NormalizeAssetTab(string? value)
    {
        if (string.Equals(value, AssetLibraryTabPending, StringComparison.OrdinalIgnoreCase))
        {
            return AssetLibraryTabPending;
        }

        if (string.Equals(value, AssetLibraryTabRejected, StringComparison.OrdinalIgnoreCase))
        {
            return AssetLibraryTabRejected;
        }

        return AssetLibraryTabApproved;
    }

    private static string NormalizePanelPlacement(string? value, string fallback)
        => value switch
        {
            PanelPlacementLeft => PanelPlacementLeft,
            PanelPlacementRight => PanelPlacementRight,
            PanelPlacementBottom => PanelPlacementBottom,
            PanelPlacementFloat => PanelPlacementFloat,
            PanelPlacementHidden => PanelPlacementHidden,
            _ => fallback,
        };

    private static bool HasPreviewImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshAiInterviewHeader()
    {
        var projectLabel = HasProjectRootPath ? ProjectRootNameLabel : "Current Project";
        AiInterviewHeader = $"Deep AI Interview – {projectLabel}";
    }

    private string ResolveProjectRootForSceneOperations()
    {
        if (HasProjectRootPath)
        {
            return ProjectRootPath;
        }

        if (HasActiveScenePath)
        {
            return ResolveProjectRootFromScenePath(ActiveScenePath);
        }

        return Path.GetDirectoryName(_settingsFilePath) is { Length: > 0 } settingsDirectory
            ? Path.GetDirectoryName(settingsDirectory) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;
    }

    private void EnsureSceneProjectFolders(string projectRoot)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Scenes"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Approved"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Generated"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Rejected"));
        Directory.CreateDirectory(Path.Combine(projectRoot, ".soulloom"));
    }

    private static string ResolveProjectRootFromScenePath(string scenePath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(scenePath) ?? Environment.CurrentDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets"))
                || Directory.Exists(Path.Combine(current.FullName, ".soulloom"))
                || File.Exists(Path.Combine(current.FullName, "art_bible.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetDirectoryName(scenePath) ?? Environment.CurrentDirectory;
    }

    private static string GetUniqueScenePath(string scenesFolder, string baseName)
    {
        var index = 1;
        while (true)
        {
            var fileName = index == 1 ? $"{baseName}.scene.json" : $"{baseName} {index}.scene.json";
            var path = Path.Combine(scenesFolder, fileName);
            if (!File.Exists(path))
            {
                return path;
            }

            index++;
        }
    }

    private static string BuildNewSceneScaffold()
    {
        return """
{
  "scene_name": "Untitled Scene",
  "entities": [],
  "npcs": [],
  "assets": []
}
""";
    }

    private void SetSceneContext(string scenePath)
    {
        ActiveScenePath = scenePath;
        ProjectRootPath = ResolveProjectRootFromScenePath(scenePath);
        var projectPreferencesPath = GetEffectiveSettingsPath();
        if (File.Exists(projectPreferencesPath))
        {
            ApplyPreferencesCore(EditorPreferences.LoadOrDefault(projectPreferencesPath), triggerToast: false, statusMessageOverride: null);
        }

        PrototypeRoot = ResolvePrototypeRootFromScenePath(scenePath, ProjectRootPath);
        RuntimeEntityList = BuildGeneratedEntityList(PrototypeRoot);
        RefreshSceneStatus();
        _ = RefreshAssetLibraryAsync();
    }

    private static string ResolvePrototypeRootFromScenePath(string scenePath, string projectRootPath)
    {
        var sceneDirectory = Path.GetDirectoryName(scenePath) ?? projectRootPath;
        var fileName = Path.GetFileName(scenePath);
        var directoryName = Path.GetFileName(sceneDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(fileName, "scene_scaffold.json", StringComparison.OrdinalIgnoreCase)
            && string.Equals(directoryName, "scene", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(sceneDirectory)?.FullName ?? sceneDirectory;
        }

        if (string.Equals(directoryName, "Scenes", StringComparison.OrdinalIgnoreCase))
        {
            return projectRootPath;
        }

        return sceneDirectory;
    }

    private void ResetSceneSelectionState()
    {
        _selectedViewportEntities.Clear();
        SelectedViewportEntity = null;
        SelectedHierarchyNode = null;
        RuntimeSelectedEntityPreview = "No active selection.";
    }

    private void RefreshSceneStatus()
    {
        SceneValidationStatus = HasActiveScenePath && File.Exists(ActiveScenePath) ? "Validation: Ready" : "Validation: No active scene";
        OnPropertyChanged(nameof(FpsStatusLabel));
        OnPropertyChanged(nameof(NpcCountStatusLabel));
        OnPropertyChanged(nameof(SceneStatusLine));
        OnPropertyChanged(nameof(IsAssetsPanelVisibleInLeftDock));
        OnPropertyChanged(nameof(IsAssetsPanelNoticeVisibleInLeftDock));
    }

    private void EnsureSelectedAssetLibraryItem()
    {
        if (SelectedAssetLibraryItem is not null
            && VisibleAssetLibraryItems.Any(item => string.Equals(item.AssetPath, SelectedAssetLibraryItem.AssetPath, StringComparison.Ordinal)))
        {
            return;
        }

        SelectedAssetLibraryItem = VisibleAssetLibraryItems.FirstOrDefault();
    }

    private void LoadAssetLibraryFolder(string folderPath, string defaultStatus, ObservableCollection<AssetLibraryItem> target, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(folderPath)
                     .Where(path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = $"{filePath}.metadata.json";
            var prompt = string.Empty;
            var assetType = InferAssetTypeFromPath(filePath);
            var status = defaultStatus;
            var qualityScore = 0d;
            if (File.Exists(metadataPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    var root = document.RootElement;
                    prompt = root.TryGetProperty("prompt", out var promptElement) ? promptElement.GetString() ?? string.Empty : string.Empty;
                    assetType = root.TryGetProperty("asset_type", out var typeElement) ? typeElement.GetString() ?? assetType : assetType;
                    status = root.TryGetProperty("review_status", out var statusElement) ? statusElement.GetString() ?? status : status;
                    qualityScore = root.TryGetProperty("quality_score", out var qualityElement) && qualityElement.TryGetDouble(out var parsedQuality)
                        ? parsedQuality
                        : 0d;
                }
                catch
                {
                }
            }

            target.Add(new AssetLibraryItem(filePath, File.Exists(metadataPath) ? metadataPath : string.Empty, Path.GetFileName(filePath), assetType, status, prompt, qualityScore, folderPath));
        }
    }

    private static string InferAssetTypeFromPath(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "sprite";
        }

        return "texture";
    }

    public sealed record AssetLibraryItem(
        string AssetPath,
        string MetadataPath,
        string DisplayName,
        string AssetType,
        string ReviewStatus,
        string Prompt,
        double QualityScore,
        string SourceFolder)
    {
        public string PromptSnippet => string.IsNullOrWhiteSpace(Prompt) ? "(no prompt)" : (Prompt.Length <= 72 ? Prompt : $"{Prompt[..72]}...");
        public string QualityScoreLabel => $"{Math.Clamp(QualityScore, 0d, 100d):0.0}/100";
        public string StatusBadge => string.Equals(ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase)
                ? "Rejected"
                : "Pending";
        public bool IsApproved => string.Equals(ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase);
        public bool IsRejected => string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase);
        public bool CanApprove => !IsApproved;
        public bool CanReject => !IsRejected;
        public bool CanRegenerate => !string.IsNullOrWhiteSpace(Prompt);

        public bool Matches(string query, string kindFilter)
        {
            var kindMatches = !string.Equals(kindFilter, AssetFilterObj, StringComparison.OrdinalIgnoreCase);

            if (!kindMatches)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var corpus = $"{DisplayName} {AssetType} {Prompt} {StatusBadge} {AssetPath}";
            return corpus.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record InterviewHistoryItem(int StepNumber, string Question, string Answer, string FollowUp)
    {
        public string StepTitle => $"Step {StepNumber}";
        public string StepBadge => $"Step {StepNumber}";
    }
}
