using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using GameForge.Editor.EditorShell.EditorSystems;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string SystemTabDayNight = "DayNight";
    private const string SystemTabBuildings = "Buildings";
    private const string SystemTabInventoryRecipes = "InventoryRecipes";
    private const string SystemTabDialogs = "Dialogs";
    private const string SystemTabAi = "AI";
    private const string SystemTabCoCreator = "CoCreator";
    private const string SystemTabStory = "Story";
    private const string SystemTabWeather = "Weather";
    private const string SystemTabLivingNpcs = "LivingNpcs";
    private const string SystemTabSettlement = "Settlement";
    private const string SystemTabCombat = "Combat";

    private string _activeSystemTab = SystemTabDayNight;
    private DayNightPanelState _dayNight = new();
    private BuildingPanelState _buildings = new();
    private InventoryRecipesPanelState _inventoryRecipes = new();
    private DialogPanelState _dialogs = new();
    private StoryPanelState _storyPanel = new();
    private WeatherPanelState _weather = new();
    private LivingNpcsPanelState _livingNpcs = new();
    private readonly List<string> _aiCommandLog = [];
    private readonly ObservableCollection<ModelManagerEntry> _modelManagerEntries = [];
    private readonly ObservableCollection<OptimizationChangeEntry> _recentOptimizationChanges = [];
    private readonly ObservableCollection<OptimizationSuggestion> _optimizationSuggestions = [];
    private readonly HashSet<string> _modelDownloadsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _coCreatorRecentActions = [];
    private readonly ObservableCollection<CoCreatorSuggestion> _coCreatorSuggestions = [];
    private CoCreatorSuggestion? _selectedCoCreatorSuggestion;
    private bool _coCreatorLiveEnabled;
    private CancellationTokenSource? _coCreatorLiveCts;
    private string _recipeNameEditor = "NewRecipe";
    private string _recipeInputsEditor = "wood:2,stone:1";
    private string _recipeOutputEditor = "crafted_item";
    private int _recipeQuantityEditor = 1;
    private ulong _selectedBuildableEntityId;
    private string _selectedBuildableType = "SmallHouse";
    private int _selectedBuildableGridX = 2;
    private int _selectedBuildableGridY = 2;
    private ulong _selectedDialogEntityId;
    private BuildableEntityRow? _selectedBuildableEntity;
    private DialogNpcRow? _selectedDialogNpc;
    private string _dialogStartNodeEditor = "intro";
    private string _dialogNodeIdEditor = "intro";
    private string _dialogNodeTextEditor = "Hello there.";
    private string _dialogChoiceTextEditor = "Continue";
    private string _dialogChoiceNextNodeEditor = "";
    private string _dialogEffectItemEditor = "coin";
    private int _dialogEffectInventoryDelta;
    private float _dialogEffectRelationshipDelta;
    private string _aiPromptEditor = "Add 3 Houses";
    private string _modelManagerStatus = "Model manager idle.";
    private string _modelRecommendationSummary = "Run onboarding to receive hardware-matched model recommendations.";
    private string _forgeGuardKeepInstalledMessage = "ForgeGuard stays installed as a permanent helper for guardrails and critique passes.";
    private string _optimizationStatus = "Optimization insights idle.";
    private string _performanceHealthSummary = "Performance health unavailable.";
    private int _projectHealthScore = 50;
    private string _projectHealthBand = "Yellow";
    private IBrush _projectHealthBrush = Brushes.Goldenrod;
    private string _selectedOptimizationPreview = "Run Optimization Check to see suggestions.";
    private bool _isModelManagerBusy;
    private string _biomeEditor = "temperate";
    private string _worldStyleGuideEditor = "grounded stylized frontier";
    private string _selectedFactionIdEditor = "guild_builders";
    private float _reputationDeltaEditor = 5f;
    private string _factionStatusSummary = "No faction data in scene.";
    private string _relationshipStatusSummary = "No relationship data yet.";
    private ulong _selectedRelationshipNpcId;
    private float _relationshipTrustEditor;
    private float _relationshipRespectEditor;
    private float _relationshipGrudgeEditor;
    private float _relationshipDebtEditor;
    private float _relationshipLoyaltyEditor;
    private string _coCreatorStatus = "Live suggestions idle.";
    private string _economySummary = "Economy unavailable.";
    private string _tradeRouteSummary = "No trade routes loaded.";
    private string _storyLoreEditor = "";
    private string _storyNpcEditor = "";
    private string _storyEventsEditor = "";
    private string _storyFactionNotesEditor = "";
    private string _storyBeatIdEditor = "beat_new";
    private string _storyBeatTitleEditor = "New Beat";
    private string _storyBeatSummaryEditor = "Summary";
    private bool _storyBeatCompletedEditor;
    private bool _storyBeatCutsceneTriggerEditor;
    private string _storyEventIdEditor = "event_new";
    private string _storyEventTitleEditor = "New Story Event";
    private string _storyEventBeatIdEditor = "beat_new";
    private string _storyRippleTypeEditor = "faction_reputation";
    private string _storyRippleTargetEditor = "guild_builders";
    private string _storyRippleDimensionEditor = "";
    private float _storyRippleValueEditor = 5f;
    private bool _narratorEnabledEditor = true;
    private string _narratorVoiceEditor = "default";
    private string _characterVoiceProfileIdEditor = "auto";
    private string _characterVoiceGenderEditor = "neutral";
    private string _characterVoiceBuildEditor = "average";
    private string _characterVoicePersonalityEditor = "neutral";
    private string _characterVoiceStyleEditor = "neutral";
    private string _characterVoiceBaseVoiceEditor = "auto";
    private float _characterVoicePitchEditor;
    private float _characterVoiceRateEditor;
    private float _characterVoiceVolumeEditor = 1f;
    private string _storyNarratorLineEditor = "";
    private string _storyStatus = "Story tools ready.";
    private bool _combatModeEnabledEditor;
    private string _livingNpcsStatus = "Living NPC settings ready.";
    private readonly IReadOnlyList<string> _narratorVoiceOptions = ["default", "en-us", "en+f3", "Microsoft David Desktop", "Microsoft Zira Desktop", "Samantha", "Alex"];
    private readonly IReadOnlyList<string> _voiceGenderOptions = ["neutral", "female", "male"];
    private readonly IReadOnlyList<string> _voiceBuildOptions = ["average", "light", "heavy"];
    private readonly IReadOnlyList<string> _voicePersonalityOptions = ["neutral", "warm", "stoic", "playful", "stern"];
    private readonly IReadOnlyList<string> _voiceStyleOptions = ["neutral", "calm", "urgent", "confident", "whisper"];
    private readonly ObservableCollection<CoCreatorSuggestion> _storyBeatSuggestions = [];
    private CoCreatorSuggestion? _selectedStoryBeatSuggestion;

    public string ActiveSystemTab
    {
        get => _activeSystemTab;
        private set
        {
            if (_activeSystemTab == value)
            {
                return;
            }

            _activeSystemTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDayNightTabActive));
            OnPropertyChanged(nameof(IsBuildingsTabActive));
            OnPropertyChanged(nameof(IsInventoryRecipesTabActive));
            OnPropertyChanged(nameof(IsDialogsTabActive));
            OnPropertyChanged(nameof(IsAiTabActive));
            OnPropertyChanged(nameof(IsCoCreatorTabActive));
            OnPropertyChanged(nameof(IsStoryTabActive));
            OnPropertyChanged(nameof(IsWeatherTabActive));
            OnPropertyChanged(nameof(IsLivingNpcsTabActive));
            OnPropertyChanged(nameof(IsSettlementTabActive));
            OnPropertyChanged(nameof(IsCombatTabActive));
        }
    }

    public bool IsDayNightTabActive => string.Equals(ActiveSystemTab, SystemTabDayNight, StringComparison.Ordinal);
    public bool IsBuildingsTabActive => string.Equals(ActiveSystemTab, SystemTabBuildings, StringComparison.Ordinal);
    public bool IsInventoryRecipesTabActive => string.Equals(ActiveSystemTab, SystemTabInventoryRecipes, StringComparison.Ordinal);
    public bool IsDialogsTabActive => string.Equals(ActiveSystemTab, SystemTabDialogs, StringComparison.Ordinal);
    public bool IsAiTabActive => string.Equals(ActiveSystemTab, SystemTabAi, StringComparison.Ordinal);
    public bool IsCoCreatorTabActive => string.Equals(ActiveSystemTab, SystemTabCoCreator, StringComparison.Ordinal);
    public bool IsStoryTabActive => string.Equals(ActiveSystemTab, SystemTabStory, StringComparison.Ordinal);
    public bool IsWeatherTabActive => string.Equals(ActiveSystemTab, SystemTabWeather, StringComparison.Ordinal);
    public bool IsLivingNpcsTabActive => string.Equals(ActiveSystemTab, SystemTabLivingNpcs, StringComparison.Ordinal);
    public bool IsSettlementTabActive => string.Equals(ActiveSystemTab, SystemTabSettlement, StringComparison.Ordinal);
    public bool IsCombatTabActive => string.Equals(ActiveSystemTab, SystemTabCombat, StringComparison.Ordinal);

    public float DayCycleSpeedEditor
    {
        get => _dayNight.DayCycleSpeed;
        set
        {
            var clamped = Math.Max(0f, value);
            if (Math.Abs(_dayNight.DayCycleSpeed - clamped) < 0.0001f)
            {
                return;
            }

            _dayNight.DayCycleSpeed = clamped;
            OnPropertyChanged();
        }
    }

    public float DayProgressEditor
    {
        get => _dayNight.DayProgress;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_dayNight.DayProgress - clamped) < 0.0001f)
            {
                return;
            }

            _dayNight.DayProgress = clamped;
            OnPropertyChanged();
        }
    }

    public int DayCountEditor
    {
        get => _dayNight.DayCount;
        set
        {
            var clamped = Math.Max(1, value);
            if (_dayNight.DayCount == clamped)
            {
                return;
            }

            _dayNight.DayCount = clamped;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<BuildableEntityRow> BuildableEntities => _buildings.Buildables;
    public IReadOnlyList<RecipeRow> Recipes => _inventoryRecipes.Recipes;
    public string PlayerInventorySummary => _inventoryRecipes.PlayerInventory.Count == 0
        ? "(empty)"
        : string.Join(", ", _inventoryRecipes.PlayerInventory.Select(kvp => $"{kvp.Key}:{kvp.Value}"));

    public IReadOnlyList<DialogNpcRow> DialogNpcs => _dialogs.Npcs;

    public string RecipeNameEditor
    {
        get => _recipeNameEditor;
        set
        {
            if (_recipeNameEditor == value)
            {
                return;
            }

            _recipeNameEditor = value;
            OnPropertyChanged();
        }
    }

    public string RecipeInputsEditor
    {
        get => _recipeInputsEditor;
        set
        {
            if (_recipeInputsEditor == value)
            {
                return;
            }

            _recipeInputsEditor = value;
            OnPropertyChanged();
        }
    }

    public string RecipeOutputEditor
    {
        get => _recipeOutputEditor;
        set
        {
            if (_recipeOutputEditor == value)
            {
                return;
            }

            _recipeOutputEditor = value;
            OnPropertyChanged();
        }
    }

    public int RecipeQuantityEditor
    {
        get => _recipeQuantityEditor;
        set
        {
            var clamped = Math.Max(1, value);
            if (_recipeQuantityEditor == clamped)
            {
                return;
            }

            _recipeQuantityEditor = clamped;
            OnPropertyChanged();
        }
    }

    public ulong SelectedBuildableEntityId
    {
        get => _selectedBuildableEntityId;
        set
        {
            if (_selectedBuildableEntityId == value)
            {
                return;
            }

            _selectedBuildableEntityId = value;
            var selected = _buildings.Buildables.FirstOrDefault(item => item.EntityId == value);
            if (selected is not null)
            {
                _selectedBuildableEntity = selected;
                _selectedBuildableType = selected.Type;
                _selectedBuildableGridX = selected.GridX;
                _selectedBuildableGridY = selected.GridY;
                OnPropertyChanged(nameof(SelectedBuildableEntity));
                OnPropertyChanged(nameof(SelectedBuildableType));
                OnPropertyChanged(nameof(SelectedBuildableGridX));
                OnPropertyChanged(nameof(SelectedBuildableGridY));
            }

            OnPropertyChanged();
        }
    }

    public BuildableEntityRow? SelectedBuildableEntity
    {
        get => _selectedBuildableEntity;
        set
        {
            if (EqualityComparer<BuildableEntityRow?>.Default.Equals(_selectedBuildableEntity, value))
            {
                return;
            }

            _selectedBuildableEntity = value;
            OnPropertyChanged();
            if (value is not null)
            {
                SelectedBuildableEntityId = value.EntityId;
            }
        }
    }

    public string SelectedBuildableType
    {
        get => _selectedBuildableType;
        set
        {
            if (_selectedBuildableType == value)
            {
                return;
            }

            _selectedBuildableType = value;
            OnPropertyChanged();
        }
    }

    public int SelectedBuildableGridX
    {
        get => _selectedBuildableGridX;
        set
        {
            var clamped = Math.Max(1, value);
            if (_selectedBuildableGridX == clamped)
            {
                return;
            }

            _selectedBuildableGridX = clamped;
            OnPropertyChanged();
        }
    }

    public int SelectedBuildableGridY
    {
        get => _selectedBuildableGridY;
        set
        {
            var clamped = Math.Max(1, value);
            if (_selectedBuildableGridY == clamped)
            {
                return;
            }

            _selectedBuildableGridY = clamped;
            OnPropertyChanged();
        }
    }

    public ulong SelectedDialogEntityId
    {
        get => _selectedDialogEntityId;
        set
        {
            if (_selectedDialogEntityId == value)
            {
                return;
            }

            _selectedDialogEntityId = value;
            _selectedDialogNpc = _dialogs.Npcs.FirstOrDefault(item => item.EntityId == value);
            if (value != 0)
            {
                SelectedRelationshipNpcId = value;
            }
            OnPropertyChanged(nameof(SelectedDialogNpc));
            OnPropertyChanged();
            ReloadSelectedNpcVoiceEditorsFromScene();
        }
    }

    public DialogNpcRow? SelectedDialogNpc
    {
        get => _selectedDialogNpc;
        set
        {
            if (EqualityComparer<DialogNpcRow?>.Default.Equals(_selectedDialogNpc, value))
            {
                return;
            }

            _selectedDialogNpc = value;
            OnPropertyChanged();
            if (value is not null)
            {
                SelectedDialogEntityId = value.EntityId;
            }
        }
    }

    public string DialogStartNodeEditor { get => _dialogStartNodeEditor; set { _dialogStartNodeEditor = value; OnPropertyChanged(); } }
    public string DialogNodeIdEditor { get => _dialogNodeIdEditor; set { _dialogNodeIdEditor = value; OnPropertyChanged(); } }
    public string DialogNodeTextEditor { get => _dialogNodeTextEditor; set { _dialogNodeTextEditor = value; OnPropertyChanged(); } }
    public string DialogChoiceTextEditor { get => _dialogChoiceTextEditor; set { _dialogChoiceTextEditor = value; OnPropertyChanged(); } }
    public string DialogChoiceNextNodeEditor { get => _dialogChoiceNextNodeEditor; set { _dialogChoiceNextNodeEditor = value; OnPropertyChanged(); } }
    public string DialogEffectItemEditor { get => _dialogEffectItemEditor; set { _dialogEffectItemEditor = value; OnPropertyChanged(); } }
    public int DialogEffectInventoryDelta { get => _dialogEffectInventoryDelta; set { _dialogEffectInventoryDelta = value; OnPropertyChanged(); } }
    public float DialogEffectRelationshipDelta { get => _dialogEffectRelationshipDelta; set { _dialogEffectRelationshipDelta = value; OnPropertyChanged(); } }

    public string AiPromptEditor { get => _aiPromptEditor; set { _aiPromptEditor = value; OnPropertyChanged(); } }
    public string WeatherCurrentEditor { get => _weather.CurrentWeather; set { _weather.CurrentWeather = value; OnPropertyChanged(); } }
    public string WeatherTargetEditor { get => _weather.TargetWeather; set { _weather.TargetWeather = value; OnPropertyChanged(); } }
    public float WeatherIntensityEditor { get => _weather.Intensity; set { _weather.Intensity = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); } }
    public float WeatherTransitionSecondsEditor { get => _weather.TransitionSeconds; set { _weather.TransitionSeconds = Math.Max(2f, value); OnPropertyChanged(); } }
    public float WeatherNextTransitionSecondsEditor { get => _weather.NextTransitionSeconds; set { _weather.NextTransitionSeconds = Math.Max(5f, value); OnPropertyChanged(); } }
    public bool LivingNpcsFreeWillEnabledEditor { get => _livingNpcs.FreeWillEnabled; set { _livingNpcs.FreeWillEnabled = value; OnPropertyChanged(); } }
    public bool LivingNpcsLlmEnabledEditor { get => _livingNpcs.LlmEnabled; set { _livingNpcs.LlmEnabled = value; OnPropertyChanged(); } }
    public float LivingNpcsSparkChancePerSecondEditor { get => _livingNpcs.SparkChancePerSecond; set { _livingNpcs.SparkChancePerSecond = Math.Max(0f, value); OnPropertyChanged(); } }
    public int LivingNpcsMaxSparksPerNpcPerDayEditor { get => _livingNpcs.MaxSparksPerNpcPerDay; set { _livingNpcs.MaxSparksPerNpcPerDay = Math.Max(1, value); OnPropertyChanged(); } }
    public string LivingNpcsModelPathEditor { get => _livingNpcs.ModelPath; set { _livingNpcs.ModelPath = value; OnPropertyChanged(); } }
    public int LivingNpcsSparksToday => _livingNpcs.SparksToday;
    public IReadOnlyList<string> LivingNpcsRecentSparks => _livingNpcs.RecentSparks;
    public string SettlementVillageNameEditor { get => _livingNpcs.VillageName; set { _livingNpcs.VillageName = value; OnPropertyChanged(); } }
    public int SettlementPopulation => _livingNpcs.TotalPopulation;
    public float SettlementMoraleEditor { get => _livingNpcs.VillageMorale; set { _livingNpcs.VillageMorale = Math.Clamp(value, 0f, 100f); OnPropertyChanged(); } }
    public float SettlementFoodEditor { get => _livingNpcs.FoodStockpile; set { _livingNpcs.FoodStockpile = Math.Max(0f, value); OnPropertyChanged(); } }
    public float SettlementStockpileEditor { get => _livingNpcs.SharedStockpile; set { _livingNpcs.SharedStockpile = Math.Max(0f, value); OnPropertyChanged(); } }
    public string LivingNpcsStatus { get => _livingNpcsStatus; private set { _livingNpcsStatus = value; OnPropertyChanged(); } }
    public string AiCommandLog => _aiCommandLog.Count == 0 ? "No AI hook commands run yet." : string.Join(Environment.NewLine, _aiCommandLog.TakeLast(8));
    public IReadOnlyList<ModelManagerEntry> ModelManagerEntries => _modelManagerEntries;
    public IReadOnlyList<OptimizationChangeEntry> RecentOptimizationChanges => _recentOptimizationChanges;
    public IReadOnlyList<OptimizationSuggestion> OptimizationSuggestions => _optimizationSuggestions;
    public string ModelManagerStatus { get => _modelManagerStatus; private set { _modelManagerStatus = value; OnPropertyChanged(); } }
    public string ModelRecommendationSummary { get => _modelRecommendationSummary; private set { _modelRecommendationSummary = value; OnPropertyChanged(); } }
    public string ForgeGuardKeepInstalledMessage { get => _forgeGuardKeepInstalledMessage; private set { _forgeGuardKeepInstalledMessage = value; OnPropertyChanged(); } }
    public string OptimizationStatus { get => _optimizationStatus; private set { _optimizationStatus = value; OnPropertyChanged(); } }
    public string PerformanceHealthSummary { get => _performanceHealthSummary; private set { _performanceHealthSummary = value; OnPropertyChanged(); } }
    public int ProjectHealthScore
    {
        get => _projectHealthScore;
        private set
        {
            _projectHealthScore = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
        }
    }
    public string ProjectHealthBand { get => _projectHealthBand; private set { _projectHealthBand = value; OnPropertyChanged(); } }
    public IBrush ProjectHealthBrush { get => _projectHealthBrush; private set { _projectHealthBrush = value; OnPropertyChanged(); } }
    public string SelectedOptimizationPreview { get => _selectedOptimizationPreview; private set { _selectedOptimizationPreview = value; OnPropertyChanged(); } }
    public bool IsModelManagerBusy { get => _isModelManagerBusy; private set { _isModelManagerBusy = value; OnPropertyChanged(); } }
    public string BiomeEditor { get => _biomeEditor; set { _biomeEditor = value; OnPropertyChanged(); } }
    public string WorldStyleGuideEditor { get => _worldStyleGuideEditor; set { _worldStyleGuideEditor = value; OnPropertyChanged(); } }
    public string SelectedFactionIdEditor { get => _selectedFactionIdEditor; set { _selectedFactionIdEditor = value; OnPropertyChanged(); } }
    public float ReputationDeltaEditor { get => _reputationDeltaEditor; set { _reputationDeltaEditor = value; OnPropertyChanged(); } }
    public string FactionStatusSummary { get => _factionStatusSummary; private set { _factionStatusSummary = value; OnPropertyChanged(); } }
    public string RelationshipStatusSummary { get => _relationshipStatusSummary; private set { _relationshipStatusSummary = value; OnPropertyChanged(); } }
    public ulong SelectedRelationshipNpcId { get => _selectedRelationshipNpcId; set { _selectedRelationshipNpcId = value; OnPropertyChanged(); } }
    public float RelationshipTrustEditor { get => _relationshipTrustEditor; set { _relationshipTrustEditor = Math.Clamp(value, -100f, 100f); OnPropertyChanged(); } }
    public float RelationshipRespectEditor { get => _relationshipRespectEditor; set { _relationshipRespectEditor = Math.Clamp(value, -100f, 100f); OnPropertyChanged(); } }
    public float RelationshipGrudgeEditor { get => _relationshipGrudgeEditor; set { _relationshipGrudgeEditor = Math.Clamp(value, -100f, 100f); OnPropertyChanged(); } }
    public float RelationshipDebtEditor { get => _relationshipDebtEditor; set { _relationshipDebtEditor = Math.Clamp(value, -100f, 100f); OnPropertyChanged(); } }
    public float RelationshipLoyaltyEditor { get => _relationshipLoyaltyEditor; set { _relationshipLoyaltyEditor = Math.Clamp(value, -100f, 100f); OnPropertyChanged(); } }
    public string CoCreatorStatus { get => _coCreatorStatus; private set { _coCreatorStatus = value; OnPropertyChanged(); } }
    public bool CoCreatorLiveEnabled { get => _coCreatorLiveEnabled; private set { _coCreatorLiveEnabled = value; OnPropertyChanged(); } }
    public IReadOnlyList<CoCreatorSuggestion> CoCreatorSuggestions => _coCreatorSuggestions;
    public CoCreatorSuggestion? SelectedCoCreatorSuggestion
    {
        get => _selectedCoCreatorSuggestion;
        set
        {
            if (_selectedCoCreatorSuggestion == value)
            {
                return;
            }
            _selectedCoCreatorSuggestion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoCreatorWhyThisFits));
        }
    }

    public string CoCreatorWhyThisFits => SelectedCoCreatorSuggestion?.WhyThisFits ?? "Pick a suggestion to see the rationale.";
    public string EconomySummary { get => _economySummary; private set { _economySummary = value; OnPropertyChanged(); } }
    public string TradeRouteSummary { get => _tradeRouteSummary; private set { _tradeRouteSummary = value; OnPropertyChanged(); } }
    public string StoryLoreEditor { get => _storyLoreEditor; set { _storyLoreEditor = value; OnPropertyChanged(); } }
    public string StoryNpcEditor { get => _storyNpcEditor; set { _storyNpcEditor = value; OnPropertyChanged(); } }
    public string StoryEventsEditor { get => _storyEventsEditor; set { _storyEventsEditor = value; OnPropertyChanged(); } }
    public string StoryFactionNotesEditor { get => _storyFactionNotesEditor; set { _storyFactionNotesEditor = value; OnPropertyChanged(); } }
    public string StoryBeatIdEditor { get => _storyBeatIdEditor; set { _storyBeatIdEditor = value; OnPropertyChanged(); } }
    public string StoryBeatTitleEditor { get => _storyBeatTitleEditor; set { _storyBeatTitleEditor = value; OnPropertyChanged(); } }
    public string StoryBeatSummaryEditor { get => _storyBeatSummaryEditor; set { _storyBeatSummaryEditor = value; OnPropertyChanged(); } }
    public bool StoryBeatCompletedEditor { get => _storyBeatCompletedEditor; set { _storyBeatCompletedEditor = value; OnPropertyChanged(); } }
    public bool StoryBeatCutsceneTriggerEditor { get => _storyBeatCutsceneTriggerEditor; set { _storyBeatCutsceneTriggerEditor = value; OnPropertyChanged(); } }
    public string StoryEventIdEditor { get => _storyEventIdEditor; set { _storyEventIdEditor = value; OnPropertyChanged(); } }
    public string StoryEventTitleEditor { get => _storyEventTitleEditor; set { _storyEventTitleEditor = value; OnPropertyChanged(); } }
    public string StoryEventBeatIdEditor { get => _storyEventBeatIdEditor; set { _storyEventBeatIdEditor = value; OnPropertyChanged(); } }
    public string StoryRippleTypeEditor { get => _storyRippleTypeEditor; set { _storyRippleTypeEditor = value; OnPropertyChanged(); } }
    public string StoryRippleTargetEditor { get => _storyRippleTargetEditor; set { _storyRippleTargetEditor = value; OnPropertyChanged(); } }
    public string StoryRippleDimensionEditor { get => _storyRippleDimensionEditor; set { _storyRippleDimensionEditor = value; OnPropertyChanged(); } }
    public float StoryRippleValueEditor { get => _storyRippleValueEditor; set { _storyRippleValueEditor = value; OnPropertyChanged(); } }
    public bool NarratorEnabledEditor { get => _narratorEnabledEditor; set { _narratorEnabledEditor = value; OnPropertyChanged(); } }
    public string NarratorVoiceEditor { get => _narratorVoiceEditor; set { _narratorVoiceEditor = value; OnPropertyChanged(); } }
    public string CharacterVoiceProfileIdEditor { get => _characterVoiceProfileIdEditor; set { _characterVoiceProfileIdEditor = value; OnPropertyChanged(); } }
    public string CharacterVoiceGenderEditor { get => _characterVoiceGenderEditor; set { _characterVoiceGenderEditor = value; OnPropertyChanged(); } }
    public string CharacterVoiceBuildEditor { get => _characterVoiceBuildEditor; set { _characterVoiceBuildEditor = value; OnPropertyChanged(); } }
    public string CharacterVoicePersonalityEditor { get => _characterVoicePersonalityEditor; set { _characterVoicePersonalityEditor = value; OnPropertyChanged(); } }
    public string CharacterVoiceStyleEditor { get => _characterVoiceStyleEditor; set { _characterVoiceStyleEditor = value; OnPropertyChanged(); } }
    public string CharacterVoiceBaseVoiceEditor { get => _characterVoiceBaseVoiceEditor; set { _characterVoiceBaseVoiceEditor = value; OnPropertyChanged(); } }
    public float CharacterVoicePitchEditor { get => _characterVoicePitchEditor; set { _characterVoicePitchEditor = Math.Clamp(value, -50f, 50f); OnPropertyChanged(); } }
    public float CharacterVoiceRateEditor { get => _characterVoiceRateEditor; set { _characterVoiceRateEditor = Math.Clamp(value, -40f, 40f); OnPropertyChanged(); } }
    public float CharacterVoiceVolumeEditor { get => _characterVoiceVolumeEditor; set { _characterVoiceVolumeEditor = Math.Clamp(value, 0.2f, 1.6f); OnPropertyChanged(); } }
    public string StoryNarratorLineEditor { get => _storyNarratorLineEditor; set { _storyNarratorLineEditor = value; OnPropertyChanged(); } }
    public IReadOnlyList<string> NarratorVoiceOptions => _narratorVoiceOptions;
    public IReadOnlyList<string> VoiceGenderOptions => _voiceGenderOptions;
    public IReadOnlyList<string> VoiceBuildOptions => _voiceBuildOptions;
    public IReadOnlyList<string> VoicePersonalityOptions => _voicePersonalityOptions;
    public IReadOnlyList<string> VoiceStyleOptions => _voiceStyleOptions;
    public string StoryStatus { get => _storyStatus; private set { _storyStatus = value; OnPropertyChanged(); } }
    public bool CombatModeEnabledEditor { get => _combatModeEnabledEditor; set { _combatModeEnabledEditor = value; OnPropertyChanged(); } }
    public IReadOnlyList<StoryBeatRow> StoryBeats => _storyPanel.Beats;
    public IReadOnlyList<CoCreatorSuggestion> StoryBeatSuggestions => _storyBeatSuggestions;
    public CoCreatorSuggestion? SelectedStoryBeatSuggestion
    {
        get => _selectedStoryBeatSuggestion;
        set
        {
            if (_selectedStoryBeatSuggestion == value)
            {
                return;
            }

            _selectedStoryBeatSuggestion = value;
            OnPropertyChanged();
        }
    }

    public void SetSystemTab(string tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        ActiveSystemTab = tab;
        if (string.Equals(tab, SystemTabAi, StringComparison.Ordinal))
        {
            RefreshOptimizationSnapshot();
        }
    }

    public async Task RunOptimizationCheckAsync()
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            OptimizationStatus = "Generate a prototype before running optimization checks.";
            return;
        }

        try
        {
            OptimizationStatus = "Running benchmark + ForgeGuard critique...";
            await RunAiHookProcessAsync("benchmark-now", [scenePath, "optimization_check"]);
            var critiqueResult = await RunAiHookProcessAsync("optimization-critique", [scenePath, "5"]);
            if (critiqueResult.ExitCode != 0)
            {
                OptimizationStatus = $"Optimization check failed: {critiqueResult.Stderr.Trim()}";
                return;
            }

            ApplyOptimizationPayload(critiqueResult.Stdout);
            OptimizationStatus = $"Optimization check complete ({_optimizationSuggestions.Count} suggestion(s)).";
        }
        catch (Exception ex)
        {
            OptimizationStatus = $"Optimization check failed: {ex.Message}";
        }
    }

    public async Task OptimizeProjectOneClickAsync()
    {
        await RunOptimizationCheckAsync();
        if (_optimizationSuggestions.Count == 0)
        {
            return;
        }

        var safeSuggestions = _optimizationSuggestions.Where(item => item.IsSafeToAutoApply).Take(2).ToArray();
        if (safeSuggestions.Length == 0)
        {
            OptimizationStatus = "No safe suggestions available for one-click apply.";
            return;
        }

        foreach (var suggestion in safeSuggestions)
        {
            await ApplyOptimizationSuggestionAsync(suggestion.Id, autoApplied: true);
        }

        OptimizationStatus = $"Optimize Project applied {safeSuggestions.Length} safe suggestion(s).";
    }

    public void PreviewOptimizationSuggestion(string suggestionId)
    {
        var suggestion = _optimizationSuggestions.FirstOrDefault(item => string.Equals(item.Id, suggestionId, StringComparison.Ordinal));
        if (suggestion is null)
        {
            return;
        }

        var confidenceText = suggestion.Confidence > 0
            ? $"{Environment.NewLine}Confidence: {suggestion.Confidence:0.00} • Impact: {suggestion.Impact}"
            : string.Empty;
        var estimatedWinText = string.IsNullOrWhiteSpace(suggestion.EstimatedWinSummary)
            ? string.Empty
            : $"{Environment.NewLine}Estimated Win: {suggestion.EstimatedWinSummary}";
        SelectedOptimizationPreview = $"{suggestion.Title}{Environment.NewLine}{suggestion.Summary}{confidenceText}{estimatedWinText}{Environment.NewLine}{suggestion.Preview}";
    }

    public async Task ApplyOptimizationSuggestionAsync(string suggestionId, bool autoApplied = false)
    {
        var suggestion = _optimizationSuggestions.FirstOrDefault(item => string.Equals(item.Id, suggestionId, StringComparison.Ordinal));
        if (suggestion is null)
        {
            return;
        }

        await ApplySceneMutationAsync(
            autoApplied ? $"Auto-optimized: {suggestion.Title}" : $"Optimization applied: {suggestion.Title}",
            root => ApplyOptimizationPatchOperations(root, suggestion.PatchOperations));

        _optimizationSuggestions.Remove(suggestion);
        OnPropertyChanged(nameof(OptimizationSuggestions));
        SelectedOptimizationPreview = $"{suggestion.Title} applied.";
    }

    public void IgnoreOptimizationSuggestion(string suggestionId)
    {
        var suggestion = _optimizationSuggestions.FirstOrDefault(item => string.Equals(item.Id, suggestionId, StringComparison.Ordinal));
        if (suggestion is null)
        {
            return;
        }

        _optimizationSuggestions.Remove(suggestion);
        OnPropertyChanged(nameof(OptimizationSuggestions));
        SelectedOptimizationPreview = $"Ignored: {suggestion.Title}";
    }

    public async Task ApplyDayNightAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Day/night updated",
            root =>
            {
                _dayNight.ApplyToScene(root);
                return true;
            },
            cancellationToken);

    public async Task ApplyWeatherAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Weather updated",
            root =>
            {
                _weather.ApplyToScene(root);
                return true;
            },
            cancellationToken);

    public async Task SaveLivingNpcsSettingsAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Living NPC settings updated",
            root =>
            {
                _livingNpcs.ApplyToScene(root);
                LivingNpcsStatus = "Living NPC settings saved to scene.";
                return true;
            },
            cancellationToken);

    public async Task ReseedLivingNpcDefaultsAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Living NPC defaults re-seeded",
            root =>
            {
                if (root["entities"] is not JsonArray entities)
                {
                    return false;
                }

                var selectedNpcIds = _selectedViewportEntities
                    .Where(entity => string.Equals(entity.Type, "npc", StringComparison.OrdinalIgnoreCase))
                    .Select(entity => TryParseEntityId(entity.Id))
                    .Where(id => id > 0UL)
                    .ToHashSet();
                if (SelectedDialogEntityId > 0)
                {
                    selectedNpcIds.Add(SelectedDialogEntityId);
                }

                var updatedCount = 0;
                foreach (var entity in entities.OfType<JsonObject>())
                {
                    var entityType = entity["type"]?.GetValue<string>() ?? string.Empty;
                    if (!string.Equals(entityType, "npc", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var npcId = ReadUlong(entity["id"], 0);
                    if (selectedNpcIds.Count > 0 && !selectedNpcIds.Contains(npcId))
                    {
                        continue;
                    }

                    var baseX = ReadSingle(entity["x"], 0f);
                    var baseY = ReadSingle(entity["y"], 0f);

                    var schedule = entity["schedule"] as JsonObject ?? new JsonObject();
                    entity["schedule"] = schedule;
                    schedule["home_entity_id"] = ReadUlong(schedule["home_entity_id"], 0);
                    schedule["workplace_entity_id"] = ReadUlong(schedule["workplace_entity_id"], 0);
                    schedule["home_position"] = schedule["home_position"] as JsonObject ?? CreatePosition(baseX, baseY);
                    schedule["workplace_position"] = schedule["workplace_position"] as JsonObject ?? CreatePosition(baseX + 0.8f, baseY + 0.6f);
                    schedule["job_id"] = string.IsNullOrWhiteSpace(schedule["job_id"]?.GetValue<string>())
                        ? "unassigned"
                        : schedule["job_id"]?.GetValue<string>();
                    schedule["current_activity"] = string.IsNullOrWhiteSpace(schedule["current_activity"]?.GetValue<string>())
                        ? "idle"
                        : schedule["current_activity"]?.GetValue<string>();
                    schedule["current_location"] = string.IsNullOrWhiteSpace(schedule["current_location"]?.GetValue<string>())
                        ? "anywhere"
                        : schedule["current_location"]?.GetValue<string>();

                    var needs = entity["needs"] as JsonObject ?? new JsonObject();
                    entity["needs"] = needs;
                    needs["hunger"] = ClampPercent(ReadSingle(needs["hunger"], 20f));
                    needs["energy"] = ClampPercent(ReadSingle(needs["energy"], 80f));
                    needs["social"] = ClampPercent(ReadSingle(needs["social"], 60f));
                    needs["fun"] = ClampPercent(ReadSingle(needs["fun"], 55f));

                    updatedCount++;
                }

                LivingNpcsStatus = updatedCount == 0
                    ? "No NPCs matched the current selection for re-seed."
                    : $"Re-seeded living defaults for {updatedCount} NPC(s).";
                return updatedCount > 0;
            },
            cancellationToken);

    public async Task ToggleCombatModeAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Combat mode toggled",
            root =>
            {
                var combat = root["combat"] as JsonObject ?? new JsonObject();
                root["combat"] = combat;
                var active = combat["active"]?.GetValue<bool>() ?? false;
                combat["active"] = !active;
                combat["grid_width"] = Math.Max(4, combat["grid_width"]?.GetValue<int>() ?? 8);
                combat["grid_height"] = Math.Max(4, combat["grid_height"]?.GetValue<int>() ?? 8);
                CombatModeEnabledEditor = !active;
                StoryStatus = CombatModeEnabledEditor
                    ? "Combat mode enabled in scene. Use /combat_start in runtime to begin encounter."
                    : "Combat mode disabled in scene.";
                return true;
            },
            cancellationToken);

    public async Task ApplyBuildableSelectionAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Buildable updated",
            root => BuildingPanelState.TryApplyBuildableEdit(root, SelectedBuildableEntityId, SelectedBuildableType, SelectedBuildableGridX, SelectedBuildableGridY),
            cancellationToken);

    public async Task UpsertRecipeAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Recipe upserted",
            root =>
            {
                InventoryRecipesPanelState.UpsertRecipe(root, new RecipeRow(RecipeNameEditor, RecipeInputsEditor, RecipeOutputEditor, RecipeQuantityEditor));
                return true;
            },
            cancellationToken);

    public async Task RemoveRecipeAtAsync(int index, CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Recipe removed",
            root =>
            {
                InventoryRecipesPanelState.RemoveRecipe(root, index);
                return true;
            },
            cancellationToken);

    public async Task ApplyDialogDraftAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Dialog updated",
            root =>
            {
                var dialogPayload = new JsonObject
                {
                    ["start_node_id"] = DialogStartNodeEditor,
                    ["active_node_id"] = DialogStartNodeEditor,
                    ["in_progress"] = false,
                    ["nodes"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = DialogNodeIdEditor,
                            ["text"] = DialogNodeTextEditor,
                            ["choices"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["text"] = DialogChoiceTextEditor,
                                    ["next_node_id"] = DialogChoiceNextNodeEditor,
                                    ["effect"] = new JsonObject
                                    {
                                        ["inventory_item"] = DialogEffectItemEditor,
                                        ["inventory_delta"] = DialogEffectInventoryDelta,
                                        ["relationship_delta"] = DialogEffectRelationshipDelta,
                                    },
                                },
                            },
                        },
                    },
                };

                return DialogPanelState.TrySetDialogForEntity(root, SelectedDialogEntityId, dialogPayload);
            },
            cancellationToken);

    public async Task SetRenderModeAsync(string mode, CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            $"Render mode set to {mode}",
            root =>
            {
                if (!string.Equals(mode, "2D", StringComparison.Ordinal) && !string.Equals(mode, "3D", StringComparison.Ordinal))
                {
                    return false;
                }

                var render2d = root["render_2d"] as JsonObject ?? new JsonObject();
                root["render_2d"] = render2d;
                render2d["render_mode"] = mode;
                if (render2d["enabled"] is null)
                {
                    render2d["enabled"] = true;
                }

                _aiCommandLog.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] /render_mode {mode}");
                OnPropertyChanged(nameof(AiCommandLog));
                return true;
            },
            cancellationToken);

    public async Task RunAiHookAsync(string command, params string[] args)
    {
        var requiresScene = string.Equals(command, "add-npc", StringComparison.Ordinal)
            || string.Equals(command, "modify-scene", StringComparison.Ordinal)
            || string.Equals(command, "kit-bash-scene", StringComparison.Ordinal)
            || string.Equals(command, "generate-loot", StringComparison.Ordinal)
            || string.Equals(command, "edit-scene", StringComparison.Ordinal);

        var scenePath = GetScenePath();
        if (requiresScene && (scenePath is null || !File.Exists(scenePath)))
        {
            StatusMessage = "Generate a prototype before running AI hooks.";
            return;
        }

        var finalArgs = new List<string> { command };
        finalArgs.AddRange(args);

        if (string.Equals(command, "modify-scene", StringComparison.Ordinal))
        {
            finalArgs.Add(scenePath);
            finalArgs.Add(string.IsNullOrWhiteSpace(AiPromptEditor) ? "Add 3 Houses" : AiPromptEditor);
        }
        else if (string.Equals(command, "kit-bash-scene", StringComparison.Ordinal))
        {
            finalArgs.Add(scenePath);
            finalArgs.Add(string.IsNullOrWhiteSpace(AiPromptEditor) ? "build a farmhouse" : AiPromptEditor);
        }
        else if (string.Equals(command, "generate-loot", StringComparison.Ordinal))
        {
            finalArgs.Add(scenePath);
            finalArgs.Add(string.IsNullOrWhiteSpace(AiPromptEditor) ? "autumn trader reward cache" : AiPromptEditor);
            finalArgs.Add("1");
            finalArgs.Add("player");
            finalArgs.Add("weapon");
        }
        else if (string.Equals(command, "edit-scene", StringComparison.Ordinal))
        {
            finalArgs.Add(scenePath);
            finalArgs.Add(string.IsNullOrWhiteSpace(AiPromptEditor) ? "make the farm feel more melancholic at dusk with glowing lanterns and autumn leaves" : AiPromptEditor);
        }

        if (string.Equals(command, "add-npc", StringComparison.Ordinal) && args.Length == 0)
        {
            finalArgs.Add("Generated NPC");
            finalArgs.Add("villager");
        }

        var processResult = await RunAiHookProcessAsync(command, finalArgs);
        var stdout = processResult.Stdout;
        var stderr = processResult.Stderr;

        if (processResult.ExitCode != 0)
        {
            StatusMessage = $"AI hook failed: {stderr}";
            return;
        }

        if (string.Equals(command, "add-npc", StringComparison.Ordinal))
        {
            await ApplySceneMutationAsync(
                "AI NPC generated",
                root =>
                {
                    JsonNode? payload = JsonNode.Parse(stdout);
                    if (payload is not JsonObject npcObject)
                    {
                        return false;
                    }

                    var entities = root["entities"] as JsonArray ?? new JsonArray();
                    root["entities"] = entities;
                    var maxId = entities.OfType<JsonObject>()
                        .Select(node => node["id"])
                        .OfType<JsonValue>()
                        .Select(value => value.TryGetValue<ulong>(out var id) ? id : 0UL)
                        .DefaultIfEmpty(0UL)
                        .Max();
                    npcObject["id"] = maxId + 1;
                    entities.Add(npcObject);
                    return true;
                });
        }
        else if (requiresScene)
        {
            LoadViewportEntitiesFromScene(PrototypeRoot);
            ReloadSystemPanelsFromScene();
        }

        var completion = string.IsNullOrWhiteSpace(stderr)
            ? $"AI hook complete: {command}"
            : $"AI hook complete with warnings: {stderr.Trim()}";

        if (string.Equals(command, "edit-scene", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(stdout))
        {
            try
            {
                var output = JsonNode.Parse(stdout) as JsonObject;
                var qualityScore = output?["applied"]?["quality_score"]?.GetValue<double?>();
                if (qualityScore is double q)
                {
                    completion = $"{completion} • Quality {Math.Clamp(q, 0d, 100d):0.0}/100";
                }
            }
            catch
            {
                // Preserve backward-compatible behavior if hook output shape changes.
            }
        }

        StatusMessage = completion;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAiHookProcessAsync(string command, IReadOnlyList<string> finalArgs)
    {
        var projectRoot = ResolveRepositoryRoot();
        var startInfo = AiOrchestrationPanel.CreateOrchestratorStartInfo(projectRoot, finalArgs.ToArray());
        var oneLine = $"{startInfo.FileName} {string.Join(" ", startInfo.ArgumentList.Select(item => item.Contains(' ') ? $"\"{item}\"" : item))}";
        _aiCommandLog.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {oneLine}");
        OnPropertyChanged(nameof(AiCommandLog));

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, string.Empty, $"Failed to start orchestrator.py ({command}).");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    public async Task RefreshModelManagerAsync()
    {
        try
        {
            var repositoryRoot = ResolveRepositoryRoot();
            var modelsJsonPath = Path.Combine(repositoryRoot, "models.json");
            JsonObject payload;
            if (File.Exists(modelsJsonPath))
            {
                payload = JsonNode.Parse(await File.ReadAllTextAsync(modelsJsonPath)) as JsonObject ?? new JsonObject();
            }
            else
            {
                payload = new JsonObject();
            }

            RebuildModelManagerEntries(payload);
        }
        catch (Exception ex)
        {
            ModelManagerStatus = $"Model state unavailable: {ex.Message}";
        }
    }

    public async Task DownloadManagedModelAsync(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return;
        }

        _modelDownloadsInProgress.Add(friendlyName);
        RebuildModelManagerEntries(null);
        IsModelManagerBusy = true;
        try
        {
            await RunAiHookAsync("download-model", friendlyName.Trim().ToLowerInvariant());
            ModelManagerStatus = $"Download finished for {friendlyName}.";
            await RefreshModelManagerAsync();
        }
        finally
        {
            _modelDownloadsInProgress.Remove(friendlyName);
            IsModelManagerBusy = false;
            RebuildModelManagerEntries(null);
        }
    }

    public async Task RunModelOnboardingAsync()
    {
        IsModelManagerBusy = true;
        try
        {
            await RunAiHookAsync("onboarding-run");
            ModelManagerStatus = "Onboarding complete. Recommendations updated from models.json.";
            await RefreshModelManagerAsync();
        }
        finally
        {
            IsModelManagerBusy = false;
        }
    }

    public async Task SetupRecommendedModelsAsync()
    {
        IsModelManagerBusy = true;
        try
        {
            await RunModelOnboardingAsync();
            foreach (var model in _modelManagerEntries.Where(item => item.ShouldDownloadByDefault))
            {
                await DownloadManagedModelAsync(model.FriendlyName);
            }

            ModelManagerStatus = "Recommended model setup complete.";
            await RefreshModelManagerAsync();
        }
        finally
        {
            IsModelManagerBusy = false;
        }
    }

    public async Task SetupFreeWillModelAsync()
    {
        IsModelManagerBusy = true;
        _modelDownloadsInProgress.Add("freewill");
        RebuildModelManagerEntries(null);
        try
        {
            await RunAiHookAsync("setup-freewill");
            await SyncLivingNpcModelPathFromModelsAsync();
            ModelManagerStatus = "Free-Will model ready and enabled in Scene.";
            await RefreshModelManagerAsync();
        }
        finally
        {
            _modelDownloadsInProgress.Remove("freewill");
            IsModelManagerBusy = false;
            RebuildModelManagerEntries(null);
        }
    }

    public async Task RemoveManagedModelAsync(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return;
        }

        IsModelManagerBusy = true;
        try
        {
            await RunAiHookAsync("remove-model", friendlyName.Trim().ToLowerInvariant());
            if (string.Equals(friendlyName.Trim(), "forgeguard", StringComparison.OrdinalIgnoreCase))
            {
                ForgeGuardKeepInstalledMessage = "ForgeGuard removed. You can reinstall it anytime from Models.";
            }
            ModelManagerStatus = $"{friendlyName} removed from local managed models.";
            await RefreshModelManagerAsync();
        }
        finally
        {
            IsModelManagerBusy = false;
        }
    }

    private void RebuildModelManagerEntries(JsonObject? modelsPayload)
    {
        if (modelsPayload is null)
        {
            var repositoryRoot = ResolveRepositoryRoot();
            var modelsJsonPath = Path.Combine(repositoryRoot, "models.json");
            if (File.Exists(modelsJsonPath))
            {
                modelsPayload = JsonNode.Parse(File.ReadAllText(modelsJsonPath)) as JsonObject ?? new JsonObject();
            }
            else
            {
                modelsPayload = new JsonObject();
            }
        }

        var onboarding = modelsPayload["onboarding"] as JsonObject;
        var recommendations = onboarding?["recommendations"] as JsonObject;
        var configuredModels = modelsPayload["models"] as JsonObject;
        var vram = onboarding?["benchmark"]?["hardware"]?["gpu_vram_gb"]?.GetValue<int?>() ?? 0;
        var sessionCount = (modelsPayload["session_history"] as JsonArray)?.Count ?? 0;

        var profileLead = vram > 0
            ? sessionCount > 0
                ? $"Based on your previous sessions ({sessionCount}) and ~{vram}GB VRAM"
                : $"Based on your onboarding profile and ~{vram}GB VRAM"
            : "Based on your local profile";

        var defaults = new (string Friendly, string Display, string Size)[]
        {
            ("freewill", "Free-Will", "~2.0GB (Q4)"),
            ("coding", "Coding", "~2.4GB (Q4)"),
            ("assetgen", "Asset-Gen", "~5.2GB"),
            ("forgeguard", "ForgeGuard", "~2.2GB (Q4)"),
        };

        _modelManagerEntries.Clear();
        foreach (var model in defaults)
        {
            var recommendation = recommendations?[model.Friendly] as JsonObject;
            var configured = configuredModels?[model.Friendly] as JsonObject;
            var path = configured?["path"]?.GetValue<string>() ?? string.Empty;
            var installed = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            var isDownloading = _modelDownloadsInProgress.Contains(model.Friendly);
            var status = isDownloading ? "Downloading" : installed ? "Installed" : "Not found";
            var reason = recommendation?["reason"]?.GetValue<string>() ?? "No recommendation yet. Run onboarding.";
            var estimatedSize = recommendation?["estimated_size"]?.GetValue<string>() ?? model.Size;
            var shouldDownload = recommendation is not null && !installed && !string.Equals(model.Friendly, "forgeguard", StringComparison.Ordinal);
            var removable = string.Equals(model.Friendly, "forgeguard", StringComparison.Ordinal);
            _modelManagerEntries.Add(new ModelManagerEntry(
                model.Friendly,
                model.Display,
                status,
                estimatedSize,
                $"{profileLead}: {reason}",
                shouldDownload,
                removable));
        }

        ModelRecommendationSummary = _modelManagerEntries.FirstOrDefault(item => item.FriendlyName == "freewill")?.Recommendation
            ?? "Run onboarding to receive hardware-matched model recommendations.";
        ForgeGuardKeepInstalledMessage = onboarding?["forgeguard_keep_message"]?.GetValue<string>()
            ?? "ForgeGuard stays installed as a permanent helper for guardrails and critique passes.";
        if (_modelManagerEntries.Count > 0 && string.Equals(ModelManagerStatus, "Model manager idle.", StringComparison.Ordinal))
        {
            ModelManagerStatus = $"Loaded {_modelManagerEntries.Count} managed model entries.";
        }

        OnPropertyChanged(nameof(ModelManagerEntries));
    }

    public sealed record ModelManagerEntry(
        string FriendlyName,
        string DisplayName,
        string Status,
        string EstimatedSize,
        string Recommendation,
        bool ShouldDownloadByDefault,
        bool IsRemovable);

    private async Task SyncLivingNpcModelPathFromModelsAsync()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var modelsJsonPath = Path.Combine(repositoryRoot, "models.json");
        if (!File.Exists(modelsJsonPath))
        {
            return;
        }

        var payload = JsonNode.Parse(await File.ReadAllTextAsync(modelsJsonPath)) as JsonObject;
        var freewillPath = payload?["models"]?["freewill"]?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(freewillPath))
        {
            return;
        }

        LivingNpcsModelPathEditor = freewillPath;
        await SaveLivingNpcsSettingsAsync();
    }

    public async Task SaveCoCreatorSettingsAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Co-creator world context updated",
            root =>
            {
                root["biome"] = string.IsNullOrWhiteSpace(BiomeEditor) ? "temperate" : BiomeEditor.Trim();
                root["world_style_guide"] = string.IsNullOrWhiteSpace(WorldStyleGuideEditor) ? "grounded stylized frontier" : WorldStyleGuideEditor.Trim();
                if (root["factions"] is not JsonObject factions)
                {
                    factions = new JsonObject
                    {
                        ["guild_builders"] = new JsonObject
                        {
                            ["id"] = "guild_builders",
                            ["display_name"] = "Guild Builders",
                            ["category"] = "profession",
                            ["biome_hint"] = "temperate",
                        },
                        ["river_clans"] = new JsonObject
                        {
                            ["id"] = "river_clans",
                            ["display_name"] = "River Clans",
                            ["category"] = "culture",
                            ["biome_hint"] = "temperate",
                        },
                    };
                    root["factions"] = factions;
                }
                var reputation = root["player_reputation"] as JsonObject ?? new JsonObject();
                root["player_reputation"] = reputation;
                foreach (var factionEntry in factions)
                {
                    reputation.TryAdd(factionEntry.Key, 0f);
                }
                return true;
            },
            cancellationToken);

    public async Task RebuildNavmeshAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Navmesh rebuild requested",
            root =>
            {
                var navmesh = root["navmesh"] as JsonObject ?? new JsonObject();
                root["navmesh"] = navmesh;
                navmesh["dirty"] = true;
                var revision = navmesh["revision"]?.GetValue<long>() ?? 0L;
                navmesh["revision"] = revision + 1L;
                CoCreatorStatus = "Navmesh marked dirty. Runtime will rebuild paths on next update.";
                return true;
            },
            cancellationToken);

    public async Task AdjustFactionReputationAsync(float direction, CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Faction reputation updated",
            root =>
            {
                if (string.IsNullOrWhiteSpace(SelectedFactionIdEditor))
                {
                    return false;
                }

                var reputation = root["player_reputation"] as JsonObject ?? new JsonObject();
                root["player_reputation"] = reputation;
                var key = SelectedFactionIdEditor.Trim();
                var current = reputation[key]?.GetValue<float>() ?? 0f;
                var delta = Math.Clamp(ReputationDeltaEditor, 0.5f, 50f) * direction;
                var updated = Math.Clamp(current + delta, -100f, 100f);
                reputation[key] = updated;
                return true;
            },
            cancellationToken);

    public async Task ApplyRelationshipEditsAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Relationship profile updated",
            root =>
            {
                if (SelectedRelationshipNpcId == 0)
                {
                    return false;
                }

                var relationships = root["relationships"] as JsonObject ?? new JsonObject();
                root["relationships"] = relationships;
                var key = SelectedRelationshipNpcId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var profile = relationships[key] as JsonObject ?? new JsonObject();
                relationships[key] = profile;
                profile["trust"] = Math.Clamp(RelationshipTrustEditor, -100f, 100f);
                profile["respect"] = Math.Clamp(RelationshipRespectEditor, -100f, 100f);
                profile["grudge"] = Math.Clamp(RelationshipGrudgeEditor, -100f, 100f);
                profile["debt"] = Math.Clamp(RelationshipDebtEditor, -100f, 100f);
                profile["loyalty"] = Math.Clamp(RelationshipLoyaltyEditor, -100f, 100f);
                if (profile["memories"] is not JsonArray)
                {
                    profile["memories"] = new JsonArray();
                }

                var legacy = root["npc_relationships"] as JsonObject ?? new JsonObject();
                root["npc_relationships"] = legacy;
                var affinity = (RelationshipTrustEditor * 0.36f) + (RelationshipRespectEditor * 0.26f) +
                    (RelationshipLoyaltyEditor * 0.30f) - (RelationshipGrudgeEditor * 0.32f) -
                    Math.Max(0f, RelationshipDebtEditor) * 0.12f;
                legacy[key] = Math.Clamp(affinity, -100f, 100f);
                return true;
            },
            cancellationToken);

    public async Task SaveStoryBibleAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Story Bible updated",
            root =>
            {
                var story = root["story"] as JsonObject ?? new JsonObject();
                root["story"] = story;
                story["lore_entries"] = ParseSimpleBibleEntries(StoryLoreEditor, "lore");
                story["major_npcs"] = ParseSimpleBibleEntries(StoryNpcEditor, "npc");
                story["key_events"] = ParseSimpleBibleEntries(StoryEventsEditor, "event");
                story["faction_notes"] = ParseSimpleBibleEntries(StoryFactionNotesEditor, "faction");
                StoryStatus = "Story Bible saved to scene.";
                return true;
            },
            cancellationToken);

    public async Task SaveNarratorSettingsAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Narrator settings updated",
            root =>
            {
                var narrator = root["narrator"] as JsonObject ?? new JsonObject();
                root["narrator"] = narrator;
                narrator["enabled"] = NarratorEnabledEditor;
                narrator["voice_id"] = string.IsNullOrWhiteSpace(NarratorVoiceEditor) ? "default" : NarratorVoiceEditor.Trim();
                narrator["voice_profile"] = new JsonObject
                {
                    ["profile_id"] = "narrator",
                    ["gender"] = CharacterVoiceGenderEditor.Trim(),
                    ["build"] = CharacterVoiceBuildEditor.Trim(),
                    ["personality"] = CharacterVoicePersonalityEditor.Trim(),
                    ["style"] = CharacterVoiceStyleEditor.Trim(),
                    ["base_voice_id"] = string.IsNullOrWhiteSpace(NarratorVoiceEditor) ? "default" : NarratorVoiceEditor.Trim(),
                    ["pitch"] = CharacterVoicePitchEditor,
                    ["rate"] = CharacterVoiceRateEditor,
                    ["volume"] = CharacterVoiceVolumeEditor,
                };
                narrator["pending_lines"] = narrator["pending_lines"] as JsonArray ?? new JsonArray();
                narrator["spoken_history"] = narrator["spoken_history"] as JsonArray ?? new JsonArray();
                StoryStatus = $"Narrator {(NarratorEnabledEditor ? "enabled" : "disabled")} with voice '{NarratorVoiceEditor}'.";
                return true;
            },
            cancellationToken);

    public async Task SaveSelectedCharacterVoiceProfileAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Character voice profile updated",
            root =>
            {
                if (SelectedDialogEntityId == 0 || root["entities"] is not JsonArray entities)
                {
                    return false;
                }

                var target = entities
                    .OfType<JsonObject>()
                    .FirstOrDefault(node => node["id"]?.GetValue<ulong>() == SelectedDialogEntityId);
                if (target is null)
                {
                    return false;
                }

                target["voice_profile"] = new JsonObject
                {
                    ["profile_id"] = string.IsNullOrWhiteSpace(CharacterVoiceProfileIdEditor) ? "auto" : CharacterVoiceProfileIdEditor.Trim(),
                    ["gender"] = CharacterVoiceGenderEditor.Trim(),
                    ["build"] = CharacterVoiceBuildEditor.Trim(),
                    ["personality"] = CharacterVoicePersonalityEditor.Trim(),
                    ["style"] = CharacterVoiceStyleEditor.Trim(),
                    ["base_voice_id"] = string.IsNullOrWhiteSpace(CharacterVoiceBaseVoiceEditor) ? "auto" : CharacterVoiceBaseVoiceEditor.Trim(),
                    ["pitch"] = CharacterVoicePitchEditor,
                    ["rate"] = CharacterVoiceRateEditor,
                    ["volume"] = CharacterVoiceVolumeEditor,
                };
                StoryStatus = $"Saved voice profile for NPC #{SelectedDialogEntityId}.";
                return true;
            },
            cancellationToken);

    public async Task UpsertStoryBeatAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Story beat updated",
            root =>
            {
                if (string.IsNullOrWhiteSpace(StoryBeatIdEditor))
                {
                    return false;
                }
                var story = root["story"] as JsonObject ?? new JsonObject();
                root["story"] = story;
                var beats = story["campaign_beats"] as JsonArray ?? new JsonArray();
                story["campaign_beats"] = beats;
                var beatId = StoryBeatIdEditor.Trim();
                var existing = beats.OfType<JsonObject>()
                    .FirstOrDefault(node => string.Equals(node["id"]?.GetValue<string>(), beatId, StringComparison.Ordinal));
                if (existing is null)
                {
                    existing = new JsonObject();
                    beats.Add(existing);
                }
                existing["id"] = beatId;
                existing["title"] = string.IsNullOrWhiteSpace(StoryBeatTitleEditor) ? beatId : StoryBeatTitleEditor.Trim();
                existing["summary"] = StoryBeatSummaryEditor.Trim();
                existing["completed"] = StoryBeatCompletedEditor;
                existing["cutscene_trigger"] = StoryBeatCutsceneTriggerEditor;
                existing["next_ids"] = existing["next_ids"] as JsonArray ?? new JsonArray();
                StoryStatus = $"Beat '{beatId}' saved.";
                return true;
            },
            cancellationToken);

    public void SelectStoryBeatForEditing(StoryBeatRow? beat)
    {
        if (beat is null)
        {
            return;
        }

        StoryBeatIdEditor = beat.Id;
        StoryBeatTitleEditor = beat.Title;
        StoryBeatSummaryEditor = beat.Summary;
        StoryBeatCompletedEditor = beat.Completed;
        StoryBeatCutsceneTriggerEditor = beat.CutsceneTrigger;
        StoryStatus = $"Loaded beat '{beat.Id}' for editing.";
    }

    public void StageSelectedStorySuggestionForEdit()
    {
        var selected = SelectedStoryBeatSuggestion;
        if (selected is null)
        {
            StoryStatus = "Select an AI beat suggestion first.";
            return;
        }

        var mutationType = selected.Mutation["type"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(mutationType, "story_mark_cutscene", StringComparison.Ordinal))
        {
            StoryBeatIdEditor = selected.Mutation["beat_id"]?.GetValue<string>() ?? StoryBeatIdEditor;
            if (!string.IsNullOrWhiteSpace(selected.Mutation["title"]?.GetValue<string>()))
            {
                StoryBeatTitleEditor = selected.Mutation["title"]!.GetValue<string>();
            }
            StoryBeatCutsceneTriggerEditor = true;
            StoryStatus = "AI cutscene suggestion staged. Review beat details, then explicitly approve.";
            return;
        }

        StoryBeatIdEditor = selected.Mutation["beat_id"]?.GetValue<string>() ?? $"beat_{Guid.NewGuid():N}";
        StoryBeatTitleEditor = selected.Mutation["title"]?.GetValue<string>() ?? "AI Suggested Beat";
        StoryBeatSummaryEditor = selected.Mutation["summary"]?.GetValue<string>() ?? "Suggested by AI co-creator.";
        StoryBeatCompletedEditor = false;
        StoryBeatCutsceneTriggerEditor = selected.Mutation["cutscene_trigger"]?.GetValue<bool>() ?? false;
        StoryStatus = "AI suggestion staged in beat editor. Edit if needed, then explicitly approve.";
    }

    public async Task AcceptStoryBeatSuggestionAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedStoryBeatSuggestion;
        if (selected is null)
        {
            StoryStatus = "Select an AI beat suggestion first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(StoryBeatIdEditor))
        {
            StoryStatus = "Beat id is required before approval.";
            return;
        }

        var mutationType = selected.Mutation["type"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(mutationType, "story_mark_cutscene", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(StoryBeatIdEditor))
            {
                StoryStatus = "Select a target beat before approving cutscene generation.";
                return;
            }

            StoryBeatCutsceneTriggerEditor = true;
        }

        await UpsertStoryBeatAsync(cancellationToken);
        RemoveStorySuggestionById(selected.Id);
        CoCreatorStatus = "AI story beat approved by user and applied.";
        StoryStatus = $"Approved AI beat '{StoryBeatIdEditor.Trim()}' and saved to scene.";
    }

    public void RejectStoryBeatSuggestion()
    {
        var selected = SelectedStoryBeatSuggestion;
        if (selected is null)
        {
            StoryStatus = "Select an AI beat suggestion first.";
            return;
        }

        RemoveStorySuggestionById(selected.Id);
        StoryStatus = "AI beat suggestion rejected.";
    }

    public async Task QueueStoryEventAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Story event queued",
            root =>
            {
                if (string.IsNullOrWhiteSpace(StoryEventIdEditor))
                {
                    return false;
                }
                var story = root["story"] as JsonObject ?? new JsonObject();
                root["story"] = story;
                var pending = story["pending_events"] as JsonArray ?? new JsonArray();
                story["pending_events"] = pending;
                var eventNode = new JsonObject
                {
                    ["event_id"] = StoryEventIdEditor.Trim(),
                    ["beat_id"] = StoryEventBeatIdEditor.Trim(),
                    ["title"] = string.IsNullOrWhiteSpace(StoryEventTitleEditor) ? StoryEventIdEditor.Trim() : StoryEventTitleEditor.Trim(),
                    ["summary"] = StoryEventTitleEditor.Trim(),
                    ["narrator_line"] = StoryNarratorLineEditor.Trim(),
                    ["applied"] = false,
                    ["ripples"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = StoryRippleTypeEditor.Trim(),
                            ["target_id"] = StoryRippleTargetEditor.Trim(),
                            ["dimension"] = StoryRippleDimensionEditor.Trim(),
                            ["value"] = StoryRippleValueEditor,
                            ["reason"] = "story_event",
                        },
                    },
                };
                pending.Add(eventNode);
                StoryStatus = $"Queued story event '{StoryEventIdEditor.Trim()}' with ripple + narrator line.";
                return true;
            },
            cancellationToken);

    public async Task RefreshCoCreatorSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            CoCreatorStatus = "Generate a prototype before requesting live suggestions.";
            return;
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(scenePath, cancellationToken)) as JsonObject;
        var dayProgress = root?["day_progress"]?.GetValue<float>() ?? 0.25f;
        var currentWeather = root?["weather"]?["current_weather"]?.GetValue<string>() ?? "sunny";
        var projectRoot = ResolveRepositoryRoot();
        var recentActionsJson = JsonSerializer.Serialize(_coCreatorRecentActions.TakeLast(8).ToArray());
        var economyPayload = root?["economy"]?.ToJsonString() ?? "{}";
        var startInfo = AiOrchestrationPanel.CreateOrchestratorStartInfo(
            projectRoot,
            "co-creator-tick",
            scenePath,
            string.IsNullOrWhiteSpace(BiomeEditor) ? "temperate" : BiomeEditor.Trim(),
            string.IsNullOrWhiteSpace(WorldStyleGuideEditor) ? "grounded stylized frontier" : WorldStyleGuideEditor.Trim(),
            dayProgress.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            recentActionsJson,
            economyPayload,
            currentWeather);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            CoCreatorStatus = "Failed to launch co-creator tick.";
            return;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            CoCreatorStatus = $"Co-creator tick failed: {stderr.Trim()}";
            return;
        }

        var parsed = CoCreatorSuggestion.ParseSuggestions(stdout);
        _coCreatorSuggestions.Clear();
        foreach (var suggestion in parsed)
        {
            _coCreatorSuggestions.Add(suggestion);
        }
        SyncStoryBeatSuggestions();
        OnPropertyChanged(nameof(CoCreatorSuggestions));
        SelectedCoCreatorSuggestion = _coCreatorSuggestions.FirstOrDefault();
        CoCreatorStatus = _coCreatorSuggestions.Count == 0
            ? "No suggestions right now. Try editing world context."
            : $"Generated {_coCreatorSuggestions.Count} context-aware suggestions.";
    }

    public async Task AcceptCoCreatorSuggestionAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedCoCreatorSuggestion;
        if (selected is null)
        {
            CoCreatorStatus = "Select a suggestion first.";
            return;
        }

        var mutationType = selected.Mutation["type"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(mutationType, "story_add_beat", StringComparison.Ordinal))
        {
            StageStorySuggestionForReview(selected);
            CoCreatorStatus = "Story beat suggestion staged. Review and explicitly approve in Story tab.";
            return;
        }

        await ApplySceneMutationAsync(
            $"AI Co-Creator accepted: {selected.Label}",
            root =>
            {
                if (string.Equals(mutationType, "add_entity", StringComparison.Ordinal))
                {
                    var entity = selected.Mutation["entity"] as JsonObject;
                    if (entity is null)
                    {
                        return false;
                    }
                    var entities = root["entities"] as JsonArray ?? new JsonArray();
                    root["entities"] = entities;
                    entities.Add(entity.DeepClone());
                    var navmesh = root["navmesh"] as JsonObject ?? new JsonObject();
                    root["navmesh"] = navmesh;
                    navmesh["dirty"] = true;
                    return true;
                }
                if (string.Equals(mutationType, "set_day_progress", StringComparison.Ordinal))
                {
                    var value = selected.Mutation["value"]?.GetValue<float>() ?? 0.25f;
                    root["day_progress"] = Math.Clamp(value, 0f, 1f);
                    return true;
                }
                if (string.Equals(mutationType, "dialog_add_branch", StringComparison.Ordinal))
                {
                    var npcId = selected.Mutation["npc_id"]?.GetValue<ulong>() ?? 0UL;
                    var branchText = selected.Mutation["branch_text"]?.GetValue<string>() ?? "I have new thoughts to share.";
                    var choiceText = selected.Mutation["choice_text"]?.GetValue<string>() ?? "What's changed?";
                    var triggerEvent = selected.Mutation["trigger_event"]?.GetValue<string>() ?? "co_creator_update";
                    var requiredFactionId = selected.Mutation["required_faction_id"]?.GetValue<string>() ?? string.Empty;
                    var minRequiredRep = selected.Mutation["min_required_reputation"]?.GetValue<float>() ?? -100f;
                    var relationshipDelta = selected.Mutation["choice_relationship_delta"]?.GetValue<float>() ?? 0.5f;
                    var requiredRelationshipDimension = selected.Mutation["required_relationship_dimension"]?.GetValue<string>() ?? string.Empty;
                    var minRequiredRelationship = selected.Mutation["min_required_relationship"]?.GetValue<float>() ?? -100f;
                    return TryAppendDialogBranch(
                        root,
                        npcId,
                        branchText,
                        choiceText,
                        triggerEvent,
                        requiredFactionId,
                        minRequiredRep,
                        relationshipDelta,
                        requiredRelationshipDimension,
                        minRequiredRelationship);
                }
                if (string.Equals(mutationType, "story_add_event", StringComparison.Ordinal))
                {
                    var story = root["story"] as JsonObject ?? new JsonObject();
                    root["story"] = story;
                    var pending = story["pending_events"] as JsonArray ?? new JsonArray();
                    story["pending_events"] = pending;
                    var rippleType = selected.Mutation["ripple_type"]?.GetValue<string>() ?? "faction_reputation";
                    var rippleTarget = selected.Mutation["ripple_target"]?.GetValue<string>() ?? "guild_builders";
                    var rippleValue = selected.Mutation["ripple_value"]?.GetValue<float>() ?? 4f;
                    pending.Add(new JsonObject
                    {
                        ["event_id"] = selected.Mutation["event_id"]?.GetValue<string>() ?? $"event_{Guid.NewGuid():N}",
                        ["beat_id"] = selected.Mutation["beat_id"]?.GetValue<string>() ?? string.Empty,
                        ["title"] = selected.Mutation["title"]?.GetValue<string>() ?? "AI Story Event",
                        ["summary"] = selected.Mutation["summary"]?.GetValue<string>() ?? "AI suggested event pending user approval.",
                        ["applied"] = false,
                        ["ripples"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = rippleType,
                                ["target_id"] = rippleTarget,
                                ["dimension"] = selected.Mutation["ripple_dimension"]?.GetValue<string>() ?? string.Empty,
                                ["value"] = rippleValue,
                                ["reason"] = "ai_story_suggestion",
                            },
                        },
                    });
                    return true;
                }
                if (string.Equals(mutationType, "living_npc_adjustment", StringComparison.Ordinal) ||
                    string.Equals(mutationType, "living_npc_tweak", StringComparison.Ordinal))
                {
                    return ApplyLivingNpcAdjustment(root, selected.Mutation);
                }
                if (string.Equals(mutationType, "settlement_tweak", StringComparison.Ordinal))
                {
                    var settlement = root["settlement"] as JsonObject ?? new JsonObject();
                    root["settlement"] = settlement;
                    settlement["village_name"] = settlement["village_name"]?.GetValue<string>() ?? "River Town";
                    var morale = ReadSingle(settlement["morale"], 62f);
                    settlement["morale"] = Math.Clamp(morale + ReadSingle(selected.Mutation["morale_delta"], 0f), 0f, 100f);
                    var resources = settlement["shared_resources"] as JsonObject ?? new JsonObject();
                    settlement["shared_resources"] = resources;
                    var resourceKey = selected.Mutation["resource"]?.GetValue<string>() ?? "stockpile";
                    var delta = ReadSingle(selected.Mutation["delta"], 0f);
                    var current = ReadSingle(resources[resourceKey], resourceKey == "food" ? 80f : 45f);
                    resources[resourceKey] = Math.Max(0f, current + delta);
                    return true;
                }
                return false;
            },
            cancellationToken);

        _coCreatorSuggestions.Remove(selected);
        SyncStoryBeatSuggestions();
        OnPropertyChanged(nameof(CoCreatorSuggestions));
        SelectedCoCreatorSuggestion = _coCreatorSuggestions.FirstOrDefault();
        CoCreatorStatus = "Suggestion accepted and applied to scene.";
    }

    public void RejectCoCreatorSuggestion()
    {
        var selected = SelectedCoCreatorSuggestion;
        if (selected is null)
        {
            CoCreatorStatus = "Select a suggestion first.";
            return;
        }

        _coCreatorSuggestions.Remove(selected);
        SyncStoryBeatSuggestions();
        OnPropertyChanged(nameof(CoCreatorSuggestions));
        SelectedCoCreatorSuggestion = _coCreatorSuggestions.FirstOrDefault();
        CoCreatorStatus = "Suggestion removed.";
    }

    public void SetCoCreatorLive(bool enabled)
    {
        if (enabled == CoCreatorLiveEnabled)
        {
            return;
        }

        CoCreatorLiveEnabled = enabled;
        _coCreatorLiveCts?.Cancel();
        _coCreatorLiveCts?.Dispose();
        _coCreatorLiveCts = null;
        if (!enabled)
        {
            CoCreatorStatus = "Live mode paused.";
            return;
        }

        var cts = new CancellationTokenSource();
        _coCreatorLiveCts = cts;
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await RefreshCoCreatorSuggestionsAsync(cts.Token);
                    await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CoCreatorStatus = $"Live tick warning: {ex.Message}";
                    break;
                }
            }
        }, cts.Token);
    }

    public void ReloadSystemPanelsFromScene()
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(scenePath)) as JsonObject;
            if (root is null)
            {
                return;
            }

            _dayNight = DayNightPanelState.FromScene(root);
            _buildings = BuildingPanelState.FromScene(root);
            _inventoryRecipes = InventoryRecipesPanelState.FromScene(root);
            _dialogs = DialogPanelState.FromScene(root);
            _storyPanel = StoryPanelState.FromScene(root);
            _weather = WeatherPanelState.FromScene(root);
            _livingNpcs = LivingNpcsPanelState.FromScene(root);
            CombatModeEnabledEditor = root["combat"]?["active"]?.GetValue<bool>() ?? false;
            BiomeEditor = root["biome"]?.GetValue<string>() ?? BiomeEditor;
            WorldStyleGuideEditor = root["world_style_guide"]?.GetValue<string>() ?? WorldStyleGuideEditor;
            if (root["story"] is JsonObject story)
            {
                StoryLoreEditor = FlattenBibleEntries(story["lore_entries"] as JsonArray);
                StoryNpcEditor = FlattenBibleEntries(story["major_npcs"] as JsonArray);
                StoryEventsEditor = FlattenBibleEntries(story["key_events"] as JsonArray);
                StoryFactionNotesEditor = FlattenBibleEntries(story["faction_notes"] as JsonArray);
            }
            if (root["narrator"] is JsonObject narrator)
            {
                NarratorEnabledEditor = narrator["enabled"]?.GetValue<bool>() ?? true;
                NarratorVoiceEditor = narrator["voice_id"]?.GetValue<string>() ?? "default";
                if (narrator["voice_profile"] is JsonObject narratorVoice)
                {
                    CharacterVoiceGenderEditor = narratorVoice["gender"]?.GetValue<string>() ?? CharacterVoiceGenderEditor;
                    CharacterVoiceBuildEditor = narratorVoice["build"]?.GetValue<string>() ?? CharacterVoiceBuildEditor;
                    CharacterVoicePersonalityEditor = narratorVoice["personality"]?.GetValue<string>() ?? CharacterVoicePersonalityEditor;
                    CharacterVoiceStyleEditor = narratorVoice["style"]?.GetValue<string>() ?? CharacterVoiceStyleEditor;
                    CharacterVoicePitchEditor = narratorVoice["pitch"]?.GetValue<float>() ?? CharacterVoicePitchEditor;
                    CharacterVoiceRateEditor = narratorVoice["rate"]?.GetValue<float>() ?? CharacterVoiceRateEditor;
                    CharacterVoiceVolumeEditor = narratorVoice["volume"]?.GetValue<float>() ?? CharacterVoiceVolumeEditor;
                }
            }
            if (root["player_reputation"] is JsonObject reputation)
            {
                var lines = reputation
                    .Select(entry => $"{entry.Key}: {(entry.Value?.GetValue<float>() ?? 0f):0}")
                    .ToArray();
                FactionStatusSummary = lines.Length == 0 ? "No faction reputation yet." : string.Join(Environment.NewLine, lines);
                if (lines.Length > 0 && string.IsNullOrWhiteSpace(SelectedFactionIdEditor))
                {
                    SelectedFactionIdEditor = reputation.First().Key;
                }
            }
            else
            {
                FactionStatusSummary = "No faction reputation yet.";
            }
            if (root["relationships"] is JsonObject relationships && relationships.Count > 0)
            {
                var relationshipLines = relationships.Select(entry =>
                {
                    var node = entry.Value as JsonObject;
                    var trust = node?["trust"]?.GetValue<float>() ?? 0f;
                    var respect = node?["respect"]?.GetValue<float>() ?? 0f;
                    var grudge = node?["grudge"]?.GetValue<float>() ?? 0f;
                    var debt = node?["debt"]?.GetValue<float>() ?? 0f;
                    var loyalty = node?["loyalty"]?.GetValue<float>() ?? 0f;
                    var affinity = (trust * 0.36f) + (respect * 0.26f) + (loyalty * 0.30f) - (grudge * 0.32f) - Math.Max(0f, debt) * 0.12f;
                    return $"NPC {entry.Key}: trust={trust:0} respect={respect:0} grudge={grudge:0} debt={debt:0} loyalty={loyalty:0} affinity={affinity:0}";
                }).ToArray();
                RelationshipStatusSummary = string.Join(Environment.NewLine, relationshipLines);
                var selectedId = SelectedRelationshipNpcId;
                if ((selectedId == 0 || !relationships.ContainsKey(selectedId.ToString(System.Globalization.CultureInfo.InvariantCulture))) &&
                    ulong.TryParse(relationships.First().Key, out var parsedId))
                {
                    SelectedRelationshipNpcId = parsedId;
                }
                var selectedKey = SelectedRelationshipNpcId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (relationships[selectedKey] is JsonObject selectedProfile)
                {
                    RelationshipTrustEditor = selectedProfile["trust"]?.GetValue<float>() ?? 0f;
                    RelationshipRespectEditor = selectedProfile["respect"]?.GetValue<float>() ?? 0f;
                    RelationshipGrudgeEditor = selectedProfile["grudge"]?.GetValue<float>() ?? 0f;
                    RelationshipDebtEditor = selectedProfile["debt"]?.GetValue<float>() ?? 0f;
                    RelationshipLoyaltyEditor = selectedProfile["loyalty"]?.GetValue<float>() ?? 0f;
                }
            }
            else
            {
                RelationshipStatusSummary = "No relationship data yet.";
            }

            if (root["economy"] is JsonObject economy)
            {
                if (economy["price_table"] is JsonObject prices && prices.Count > 0)
                {
                    EconomySummary = string.Join(", ", prices.Select(entry => $"{entry.Key}:{(entry.Value?.GetValue<float>() ?? 0f):0.0}"));
                }
                else
                {
                    EconomySummary = "No market prices yet.";
                }

                if (economy["trade_routes"] is JsonArray routes && routes.Count > 0)
                {
                    var lines = routes.OfType<JsonObject>()
                        .Select(route =>
                        {
                            var routeId = route["route_id"]?.GetValue<string>() ?? "route";
                            var resource = route["resource"]?.GetValue<string>() ?? "resource";
                            var risk = route["risk"]?.GetValue<float>() ?? 0f;
                            var deaths = route["trader_deaths"]?.GetValue<int>() ?? 0;
                            return $"{routeId}: {resource} risk={risk:0.00} deaths={deaths}";
                        })
                        .ToArray();
                    TradeRouteSummary = string.Join(Environment.NewLine, lines);
                }
                else
                {
                    TradeRouteSummary = "No trade routes loaded.";
                }
            }
            else
            {
                EconomySummary = "Economy unavailable.";
                TradeRouteSummary = "No trade routes loaded.";
            }

            if (SelectedBuildableEntityId == 0 && _buildings.Buildables.Count > 0)
            {
                SelectedBuildableEntityId = _buildings.Buildables[0].EntityId;
            }
            else
            {
                _selectedBuildableEntity = _buildings.Buildables.FirstOrDefault(item => item.EntityId == SelectedBuildableEntityId);
                OnPropertyChanged(nameof(SelectedBuildableEntity));
            }

            if (SelectedDialogEntityId == 0 && _dialogs.Npcs.Count > 0)
            {
                SelectedDialogEntityId = _dialogs.Npcs[0].EntityId;
            }
            else
            {
                _selectedDialogNpc = _dialogs.Npcs.FirstOrDefault(item => item.EntityId == SelectedDialogEntityId);
                OnPropertyChanged(nameof(SelectedDialogNpc));
            }
            SyncSelectedNpcVoiceEditors(root);
            if (SelectedRelationshipNpcId == 0 && SelectedDialogEntityId != 0)
            {
                SelectedRelationshipNpcId = SelectedDialogEntityId;
            }

            OnPropertyChanged(nameof(DayCycleSpeedEditor));
            OnPropertyChanged(nameof(DayProgressEditor));
            OnPropertyChanged(nameof(DayCountEditor));
            OnPropertyChanged(nameof(BuildableEntities));
            OnPropertyChanged(nameof(PlayerInventorySummary));
            OnPropertyChanged(nameof(Recipes));
            OnPropertyChanged(nameof(DialogNpcs));
            OnPropertyChanged(nameof(StoryBeats));
            OnPropertyChanged(nameof(WeatherCurrentEditor));
            OnPropertyChanged(nameof(WeatherTargetEditor));
            OnPropertyChanged(nameof(WeatherIntensityEditor));
            OnPropertyChanged(nameof(WeatherTransitionSecondsEditor));
            OnPropertyChanged(nameof(WeatherNextTransitionSecondsEditor));
            OnPropertyChanged(nameof(LivingNpcsFreeWillEnabledEditor));
            OnPropertyChanged(nameof(LivingNpcsLlmEnabledEditor));
            OnPropertyChanged(nameof(LivingNpcsSparkChancePerSecondEditor));
            OnPropertyChanged(nameof(LivingNpcsMaxSparksPerNpcPerDayEditor));
            OnPropertyChanged(nameof(LivingNpcsModelPathEditor));
            OnPropertyChanged(nameof(LivingNpcsSparksToday));
            OnPropertyChanged(nameof(LivingNpcsRecentSparks));
            OnPropertyChanged(nameof(SettlementVillageNameEditor));
            OnPropertyChanged(nameof(SettlementPopulation));
            OnPropertyChanged(nameof(SettlementMoraleEditor));
            OnPropertyChanged(nameof(SettlementFoodEditor));
            OnPropertyChanged(nameof(SettlementStockpileEditor));
            SyncStoryBeatSuggestions();
            OnPropertyChanged(nameof(FactionStatusSummary));
            OnPropertyChanged(nameof(RelationshipStatusSummary));
            OnPropertyChanged(nameof(EconomySummary));
            OnPropertyChanged(nameof(TradeRouteSummary));
            RefreshOptimizationSnapshot();
        }
        catch
        {
            // Keep existing system panel state if scene parse fails.
        }
    }

    public async Task EvolveSelectedNpcDialogAsync(CancellationToken cancellationToken = default)
        => await ApplySceneMutationAsync(
            "Dialog evolved from Co-Creator tab",
            root =>
            {
                var npcId = SelectedDialogEntityId;
                if (npcId == 0 && _dialogs.Npcs.Count > 0)
                {
                    npcId = _dialogs.Npcs[0].EntityId;
                }

                var factionId = string.IsNullOrWhiteSpace(SelectedFactionIdEditor) ? "guild_builders" : SelectedFactionIdEditor.Trim();
                var rep = 0f;
                if (root["player_reputation"] is JsonObject playerRep)
                {
                    rep = playerRep[factionId]?.GetValue<float>() ?? 0f;
                }
                var tone = rep >= 45f ? "warm" : rep <= -25f ? "guarded" : "neutral";
                var branchText =
                    $"({tone}) The {BiomeEditor} rumors remember your last choices. It now shapes this {WorldStyleGuideEditor} exchange.";
                var choiceText = tone == "guarded"
                    ? "Can we repair trust?"
                    : "Any new developments from recent events?";
                return TryAppendDialogBranch(root, npcId, branchText, choiceText, "editor_evolve_button", factionId, tone == "guarded" ? -10f : -100f, 1f, string.Empty, -100f);
            },
            cancellationToken);

    private static bool ApplyLivingNpcAdjustment(JsonObject root, JsonObject mutation)
    {
        if (root["entities"] is not JsonArray entities)
        {
            return false;
        }

        var npcId = ReadUlong(mutation["npc_id"], 0);
        if (npcId == 0)
        {
            return false;
        }

        foreach (var entity in entities.OfType<JsonObject>())
        {
            if (ReadUlong(entity["id"], 0) != npcId)
            {
                continue;
            }

            var schedule = entity["schedule"] as JsonObject ?? new JsonObject();
            entity["schedule"] = schedule;
            var needs = entity["needs"] as JsonObject ?? new JsonObject();
            entity["needs"] = needs;

            if (mutation["home"] is JsonObject home && home["x"] is not null && home["y"] is not null)
            {
                schedule["home_position"] = CreatePosition(ReadSingle(home["x"], 0f), ReadSingle(home["y"], 0f));
            }

            if (mutation["work"] is JsonObject work && work["x"] is not null && work["y"] is not null)
            {
                schedule["workplace_position"] = CreatePosition(ReadSingle(work["x"], 0f), ReadSingle(work["y"], 0f));
            }

            if (mutation["needs_modifiers"] is JsonObject needsModifiers)
            {
                foreach (var need in new[] { "hunger", "energy", "social", "fun" })
                {
                    if (needsModifiers[need] is null)
                    {
                        continue;
                    }

                    var current = ReadSingle(needs[need], DefaultNeedValue(need));
                    needs[need] = ClampPercent(current + ReadSingle(needsModifiers[need], 0f));
                }
            }

            return true;
        }

        return false;
    }

    private static JsonObject CreatePosition(float x, float y)
        => new()
        {
            ["x"] = x,
            ["y"] = 0f,
            ["z"] = y,
        };

    private static ulong TryParseEntityId(string? entityId)
        => ulong.TryParse(entityId, out var parsed) ? parsed : 0UL;

    private static float ClampPercent(float value)
        => Math.Clamp(value, 0f, 100f);

    private static float DefaultNeedValue(string needKey)
        => needKey switch
        {
            "hunger" => 20f,
            "energy" => 80f,
            "social" => 60f,
            "fun" => 55f,
            _ => 50f,
        };

    private static float ReadSingle(JsonNode? value, float fallback)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<float>(out var floatValue))
            {
                return floatValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return (float)doubleValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }
        }

        return fallback;
    }

    private static ulong ReadUlong(JsonNode? value, ulong fallback)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<ulong>(out var ulongValue))
            {
                return ulongValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue) && intValue >= 0)
            {
                return (ulong)intValue;
            }
        }

        return fallback;
    }

    private static JsonArray ParseSimpleBibleEntries(string rawText, string idPrefix)
    {
        var output = new JsonArray();
        var lines = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            output.Add(new JsonObject
            {
                ["id"] = $"{idPrefix}_{index + 1}",
                ["title"] = line,
                ["summary"] = line,
                ["tags"] = new JsonArray(),
            });
        }
        return output;
    }

    private static string FlattenBibleEntries(JsonArray? entries)
    {
        if (entries is null)
        {
            return string.Empty;
        }
        var lines = entries.OfType<JsonObject>()
            .Select(node => node["title"]?.GetValue<string>() ?? node["summary"]?.GetValue<string>() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return lines.Length == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private void SyncStoryBeatSuggestions()
    {
        _storyBeatSuggestions.Clear();
        foreach (var suggestion in _coCreatorSuggestions.Where(IsStoryAddBeatSuggestion))
        {
            _storyBeatSuggestions.Add(suggestion);
        }

        SelectedStoryBeatSuggestion = _storyBeatSuggestions.FirstOrDefault();
        OnPropertyChanged(nameof(StoryBeatSuggestions));
    }

    private static bool IsStoryAddBeatSuggestion(CoCreatorSuggestion suggestion)
    {
        var type = suggestion.Mutation["type"]?.GetValue<string>() ?? string.Empty;
        return string.Equals(type, "story_add_beat", StringComparison.Ordinal)
            || string.Equals(type, "story_mark_cutscene", StringComparison.Ordinal);
    }

    private void StageStorySuggestionForReview(CoCreatorSuggestion suggestion)
    {
        if (_coCreatorSuggestions.Any(existing => string.Equals(existing.Id, suggestion.Id, StringComparison.Ordinal)))
        {
            SyncStoryBeatSuggestions();
            StoryStatus = "AI beat suggestion requires explicit review in Story tab (Edit / Accept / Reject).";
            return;
        }

        _coCreatorSuggestions.Add(suggestion);
        OnPropertyChanged(nameof(CoCreatorSuggestions));
        SyncStoryBeatSuggestions();
        StoryStatus = "AI beat suggestion requires explicit review in Story tab (Edit / Accept / Reject).";
    }

    private void RemoveStorySuggestionById(string suggestionId)
    {
        for (var index = _coCreatorSuggestions.Count - 1; index >= 0; index--)
        {
            if (string.Equals(_coCreatorSuggestions[index].Id, suggestionId, StringComparison.Ordinal))
            {
                _coCreatorSuggestions.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(CoCreatorSuggestions));
        SyncStoryBeatSuggestions();
        SelectedCoCreatorSuggestion = _coCreatorSuggestions.FirstOrDefault();
    }

    private void ReloadSelectedNpcVoiceEditorsFromScene()
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(scenePath)) as JsonObject;
        if (root is null)
        {
            return;
        }

        SyncSelectedNpcVoiceEditors(root);
    }

    private void SyncSelectedNpcVoiceEditors(JsonObject root)
    {
        if (SelectedDialogEntityId == 0 || root["entities"] is not JsonArray entities)
        {
            return;
        }

        var target = entities
            .OfType<JsonObject>()
            .FirstOrDefault(node => node["id"]?.GetValue<ulong>() == SelectedDialogEntityId);
        var voice = target?["voice_profile"] as JsonObject;
        if (voice is null)
        {
            CharacterVoiceProfileIdEditor = "auto";
            CharacterVoiceGenderEditor = "neutral";
            CharacterVoiceBuildEditor = "average";
            CharacterVoicePersonalityEditor = "neutral";
            CharacterVoiceStyleEditor = "neutral";
            CharacterVoiceBaseVoiceEditor = "auto";
            CharacterVoicePitchEditor = 0f;
            CharacterVoiceRateEditor = 0f;
            CharacterVoiceVolumeEditor = 1f;
            return;
        }

        CharacterVoiceProfileIdEditor = voice["profile_id"]?.GetValue<string>() ?? "auto";
        CharacterVoiceGenderEditor = voice["gender"]?.GetValue<string>() ?? "neutral";
        CharacterVoiceBuildEditor = voice["build"]?.GetValue<string>() ?? "average";
        CharacterVoicePersonalityEditor = voice["personality"]?.GetValue<string>() ?? "neutral";
        CharacterVoiceStyleEditor = voice["style"]?.GetValue<string>() ?? "neutral";
        CharacterVoiceBaseVoiceEditor = voice["base_voice_id"]?.GetValue<string>() ?? "auto";
        CharacterVoicePitchEditor = voice["pitch"]?.GetValue<float>() ?? 0f;
        CharacterVoiceRateEditor = voice["rate"]?.GetValue<float>() ?? 0f;
        CharacterVoiceVolumeEditor = voice["volume"]?.GetValue<float>() ?? 1f;
    }

    private static bool TryAppendDialogBranch(
        JsonObject root,
        ulong npcId,
        string branchText,
        string choiceText,
        string triggerEvent,
        string requiredFactionId,
        float minRequiredRep,
        float relationshipDelta,
        string requiredRelationshipDimension,
        float minRequiredRelationship)
    {
        if (root["entities"] is not JsonArray entities)
        {
            return false;
        }

        foreach (var entity in entities.OfType<JsonObject>())
        {
            var id = entity["id"]?.GetValue<ulong>() ?? 0UL;
            if (id != npcId || entity["dialog"] is not JsonObject dialog)
            {
                continue;
            }

            if (dialog["nodes"] is not JsonArray nodes || nodes.Count == 0)
            {
                return false;
            }

            var branchId = $"evolved_{nodes.Count + 1}";
            var newNode = new JsonObject
            {
                ["id"] = branchId,
                ["text"] = branchText,
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = "I understand.",
                        ["effect"] = new JsonObject
                        {
                            ["relationship_delta"] = relationshipDelta,
                        },
                    },
                },
            };
            nodes.Add(newNode);

            var startNodeId = dialog["start_node_id"]?.GetValue<string>()
                ?? (nodes[0] as JsonObject)?["id"]?.GetValue<string>()
                ?? string.Empty;
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (!string.Equals(node["id"]?.GetValue<string>(), startNodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                var choices = node["choices"] as JsonArray ?? new JsonArray();
                node["choices"] = choices;
                var branchChoice = new JsonObject
                {
                    ["text"] = choiceText,
                    ["next_node_id"] = branchId,
                    ["effect"] = new JsonObject(),
                };
                if (!string.IsNullOrWhiteSpace(requiredFactionId))
                {
                    branchChoice["required_faction_id"] = requiredFactionId;
                    branchChoice["min_required_reputation"] = minRequiredRep;
                }
                if (!string.IsNullOrWhiteSpace(requiredRelationshipDimension))
                {
                    branchChoice["required_relationship_dimension"] = requiredRelationshipDimension;
                    branchChoice["min_required_relationship"] = minRequiredRelationship;
                }
                choices.Add(branchChoice);
                break;
            }

            var worldEvents = dialog["world_events"] as JsonArray ?? new JsonArray();
            dialog["world_events"] = worldEvents;
            worldEvents.Add(triggerEvent);
            while (worldEvents.Count > 24)
            {
                worldEvents.RemoveAt(0);
            }
            return true;
        }

        return false;
    }

    private void RefreshOptimizationSnapshot()
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            _recentOptimizationChanges.Clear();
            _optimizationSuggestions.Clear();
            PerformanceHealthSummary = "Performance health unavailable (no prototype scene yet).";
            ProjectHealthScore = 50;
            SetHealthBand();
            return;
        }

        try
        {
            var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(scenePath) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var changesPath = Path.Combine(projectRoot, "changes.log.json");
            _recentOptimizationChanges.Clear();
            if (File.Exists(changesPath))
            {
                var changesRoot = JsonNode.Parse(File.ReadAllText(changesPath)) as JsonObject;
                var entries = changesRoot?["entries"] as JsonArray;
                if (entries is not null)
                {
                    foreach (var entry in entries.OfType<JsonObject>().Reverse().Take(8))
                    {
                        _recentOptimizationChanges.Add(new OptimizationChangeEntry(
                            entry["action_type"]?.GetValue<string>() ?? "unknown",
                            entry["summary_text"]?.GetValue<string>() ?? "(no summary)",
                            FormatPerformanceDelta(entry["performance_delta"])));
                    }
                }
            }

            var historyPath = Path.Combine(projectRoot, "performance_history.json");
            var summary = "Performance history unavailable.";
            var score = 50;
            if (File.Exists(historyPath))
            {
                var historyRoot = JsonNode.Parse(File.ReadAllText(historyPath)) as JsonObject;
                var snapshots = historyRoot?["snapshots"] as JsonArray;
                if (snapshots is not null && snapshots.Count > 0)
                {
                    var latest = snapshots[^1] as JsonObject;
                    var previous = snapshots.Count > 1 ? snapshots[^2] as JsonObject : null;
                    var latestMetrics = latest?["metrics"] as JsonObject;
                    var previousMetrics = previous?["metrics"] as JsonObject;
                    var target = latest?["target_hardware_profile"]?.GetValue<string>() ?? "unknown";
                    var targetFps = string.Equals(target, "high_fidelity", StringComparison.OrdinalIgnoreCase) ? 60d
                        : string.Equals(target, "potato", StringComparison.OrdinalIgnoreCase) ? 30d
                        : 45d;
                    var fps = latestMetrics?["fps_avg"]?.GetValue<double>() ?? 0d;
                    var vram = latestMetrics?["vram_usage_mb"]?.GetValue<double>() ?? 0d;
                    var drawCalls = latestMetrics?["draw_calls"]?.GetValue<int>() ?? 0;
                    var fpsPrev = previousMetrics?["fps_avg"]?.GetValue<double>() ?? fps;
                    var fpsDelta = fps - fpsPrev;
                    score = 65;
                    score += fps >= targetFps ? 15 : -15;
                    score += fpsDelta >= 0 ? 5 : -5;
                    score += drawCalls < 450 ? 8 : -8;
                    score += vram <= 0 || vram <= 4096 ? 7 : -7;
                    score = Math.Clamp(score, 0, 100);
                    summary = $"Target {target} • FPS {fps:0.0} ({fpsDelta:+0.0;-0.0;0}) • Draw {drawCalls} • VRAM {vram:0} MB";
                }
            }

            PerformanceHealthSummary = summary;
            ProjectHealthScore = score;
            SetHealthBand();
            OnPropertyChanged(nameof(RecentOptimizationChanges));
            OnPropertyChanged(nameof(OptimizationSuggestions));
        }
        catch
        {
            PerformanceHealthSummary = "Performance health unavailable.";
            ProjectHealthScore = 50;
            SetHealthBand();
        }
    }

    private void SetHealthBand()
    {
        if (ProjectHealthScore >= 75)
        {
            ProjectHealthBand = "Green";
            ProjectHealthBrush = Brushes.LimeGreen;
            return;
        }

        if (ProjectHealthScore >= 45)
        {
            ProjectHealthBand = "Yellow";
            ProjectHealthBrush = Brushes.Goldenrod;
            return;
        }

        ProjectHealthBand = "Red";
        ProjectHealthBrush = Brushes.IndianRed;
    }

    private static string FormatPerformanceDelta(JsonNode? deltaNode)
    {
        if (deltaNode is not JsonObject delta || delta.Count == 0)
        {
            return "Δ n/a";
        }

        var fps = delta["fps_avg"]?.GetValue<double?>();
        var vram = delta["vram_usage_mb"]?.GetValue<double?>();
        var parts = new List<string>();
        if (fps is double fpsValue)
        {
            parts.Add($"FPS {fpsValue:+0.0;-0.0;0}");
        }
        if (vram is double vramValue)
        {
            parts.Add($"VRAM {vramValue:+0;-0;0}MB");
        }

        return parts.Count == 0 ? "Δ tracked" : $"Δ {string.Join(" | ", parts)}";
    }

    private void ApplyOptimizationPayload(string stdout)
    {
        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(stdout) as JsonObject;
        }
        catch (JsonException)
        {
            OptimizationStatus = "Optimization critique returned invalid JSON.";
            return;
        }

        if (payload is null)
        {
            return;
        }

        _optimizationSuggestions.Clear();
        if (payload["suggestions"] is JsonArray suggestions)
        {
            foreach (var suggestionNode in suggestions.OfType<JsonObject>())
            {
                var id = suggestionNode["id"]?.GetValue<string>() ?? $"sg-{_optimizationSuggestions.Count + 1}";
                var patch = new List<OptimizationPatchOperation>();
                if (suggestionNode["patch"] is JsonArray patchNodes)
                {
                    foreach (var patchNode in patchNodes.OfType<JsonObject>())
                    {
                        patch.Add(new OptimizationPatchOperation(
                            patchNode["op"]?.GetValue<string>() ?? "set",
                            patchNode["path"]?.GetValue<string>() ?? "/",
                            patchNode["value"]?.DeepClone()));
                    }
                }

                _optimizationSuggestions.Add(new OptimizationSuggestion(
                    id,
                    suggestionNode["title"]?.GetValue<string>() ?? "Optimization Suggestion",
                    suggestionNode["summary"]?.GetValue<string>() ?? string.Empty,
                    suggestionNode["preview"]?.GetValue<string>() ?? "No preview available.",
                    string.Equals(suggestionNode["safety"]?.GetValue<string>(), "safe", StringComparison.OrdinalIgnoreCase),
                    patch,
                    suggestionNode["confidence"]?.GetValue<double>() ?? 0d,
                    suggestionNode["impact"]?.GetValue<string>() ?? "unknown",
                    SummarizeEstimatedWin(suggestionNode["estimated_win"])));
            }
        }

        if (payload["recent_changes"] is JsonArray recentChanges)
        {
            _recentOptimizationChanges.Clear();
            foreach (var recentNode in recentChanges.OfType<JsonObject>())
            {
                _recentOptimizationChanges.Add(new OptimizationChangeEntry(
                    recentNode["action_type"]?.GetValue<string>() ?? "unknown",
                    recentNode["summary"]?.GetValue<string>() ?? "(no summary)",
                    FormatPerformanceDelta(recentNode["performance_delta"])));
            }
        }

        ProjectHealthScore = payload["health_score"]?.GetValue<int>() ?? ProjectHealthScore;
        SetHealthBand();
        var summaryNode = payload["health_summary"] as JsonObject;
        if (summaryNode is not null)
        {
            PerformanceHealthSummary = $"Target {summaryNode["target_profile"]?.GetValue<string>() ?? "unknown"} • FPS {summaryNode["fps_avg"]?.GetValue<double>() ?? 0:0.0} • VRAM {summaryNode["vram_usage_mb"]?.GetValue<double>() ?? 0:0} MB";
        }

        if (_optimizationSuggestions.Count > 0)
        {
            PreviewOptimizationSuggestion(_optimizationSuggestions[0].Id);
        }

        var sourceModel = payload["source_model"]?.GetValue<string>() ?? "heuristic-fallback";
        OptimizationStatus = $"Optimization critique source: {sourceModel}. Suggestions: {_optimizationSuggestions.Count}.";

        OnPropertyChanged(nameof(RecentOptimizationChanges));
        OnPropertyChanged(nameof(OptimizationSuggestions));
    }

    private static string SummarizeEstimatedWin(JsonNode? estimatedWinNode)
    {
        if (estimatedWinNode is not JsonObject estimated || estimated.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var kvp in estimated)
        {
            if (kvp.Value is null)
            {
                continue;
            }

            parts.Add($"{kvp.Key}={kvp.Value.ToJsonString()}");
        }

        return string.Join(", ", parts);
    }

    private static bool ApplyOptimizationPatchOperations(JsonObject root, IReadOnlyList<OptimizationPatchOperation> operations)
    {
        var applied = false;
        foreach (var operation in operations)
        {
            var segments = operation.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            JsonObject current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var key = segments[i];
                if (current[key] is not JsonObject next)
                {
                    next = new JsonObject();
                    current[key] = next;
                }

                current = next;
            }

            var leaf = segments[^1];
            if (string.Equals(operation.Op, "remove", StringComparison.OrdinalIgnoreCase))
            {
                applied |= current.Remove(leaf);
            }
            else
            {
                current[leaf] = operation.Value?.DeepClone();
                applied = true;
            }
        }

        return applied;
    }

    private async Task ApplySceneMutationAsync(string label, Func<JsonObject, bool> mutate, CancellationToken cancellationToken = default)
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            StatusMessage = "Generate a prototype before editing systems.";
            return;
        }

        var beforeContent = await File.ReadAllTextAsync(scenePath, cancellationToken);
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(beforeContent) as JsonObject;
        }
        catch (JsonException)
        {
            StatusMessage = "Scene scaffold parse failed.";
            ShowToast("Scene parse failed.");
            return;
        }

        if (root is null || !mutate(root))
        {
            StatusMessage = "No applicable scene changes were produced.";
            return;
        }

        var afterContent = JsonSerializer.Serialize(root, JsonSerializerOptionsIndented);
        await WriteSceneAndRelaunchAsync(scenePath, beforeContent, afterContent, label, cancellationToken);
        _coCreatorRecentActions.Add(label);
        if (_coCreatorRecentActions.Count > 24)
        {
            _coCreatorRecentActions.RemoveRange(0, _coCreatorRecentActions.Count - 24);
        }
        ReloadSystemPanelsFromScene();
    }

    public sealed record OptimizationChangeEntry(string ActionType, string Summary, string Delta);

    public sealed record OptimizationPatchOperation(string Op, string Path, JsonNode? Value);

    public sealed record OptimizationSuggestion(
        string Id,
        string Title,
        string Summary,
        string Preview,
        bool IsSafeToAutoApply,
        IReadOnlyList<OptimizationPatchOperation> PatchOperations,
        double Confidence,
        string Impact,
        string EstimatedWinSummary);
}
