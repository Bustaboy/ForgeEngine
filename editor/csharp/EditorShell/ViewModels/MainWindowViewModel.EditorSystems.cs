using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameForge.Editor.EditorShell.EditorSystems;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string SystemTabDayNight = "DayNight";
    private const string SystemTabBuildings = "Buildings";
    private const string SystemTabInventoryRecipes = "InventoryRecipes";
    private const string SystemTabDialogs = "Dialogs";
    private const string SystemTabAi = "AI";

    private string _activeSystemTab = SystemTabDayNight;
    private DayNightPanelState _dayNight = new();
    private BuildingPanelState _buildings = new();
    private InventoryRecipesPanelState _inventoryRecipes = new();
    private DialogPanelState _dialogs = new();
    private readonly List<string> _aiCommandLog = [];
    private string _recipeNameEditor = "NewRecipe";
    private string _recipeInputsEditor = "wood:2,stone:1";
    private string _recipeOutputEditor = "crafted_item";
    private int _recipeQuantityEditor = 1;
    private ulong _selectedBuildableEntityId;
    private string _selectedBuildableType = "SmallHouse";
    private int _selectedBuildableGridX = 2;
    private int _selectedBuildableGridY = 2;
    private ulong _selectedDialogEntityId;
    private string _dialogStartNodeEditor = "intro";
    private string _dialogNodeIdEditor = "intro";
    private string _dialogNodeTextEditor = "Hello there.";
    private string _dialogChoiceTextEditor = "Continue";
    private string _dialogChoiceNextNodeEditor = "";
    private string _dialogEffectItemEditor = "coin";
    private int _dialogEffectInventoryDelta;
    private float _dialogEffectRelationshipDelta;
    private string _aiPromptEditor = "Add 3 Houses";

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
        }
    }

    public bool IsDayNightTabActive => string.Equals(ActiveSystemTab, SystemTabDayNight, StringComparison.Ordinal);
    public bool IsBuildingsTabActive => string.Equals(ActiveSystemTab, SystemTabBuildings, StringComparison.Ordinal);
    public bool IsInventoryRecipesTabActive => string.Equals(ActiveSystemTab, SystemTabInventoryRecipes, StringComparison.Ordinal);
    public bool IsDialogsTabActive => string.Equals(ActiveSystemTab, SystemTabDialogs, StringComparison.Ordinal);
    public bool IsAiTabActive => string.Equals(ActiveSystemTab, SystemTabAi, StringComparison.Ordinal);

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
                _selectedBuildableType = selected.Type;
                _selectedBuildableGridX = selected.GridX;
                _selectedBuildableGridY = selected.GridY;
                OnPropertyChanged(nameof(SelectedBuildableType));
                OnPropertyChanged(nameof(SelectedBuildableGridX));
                OnPropertyChanged(nameof(SelectedBuildableGridY));
            }

            OnPropertyChanged();
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
            OnPropertyChanged();
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
    public string AiCommandLog => _aiCommandLog.Count == 0 ? "No AI hook commands run yet." : string.Join(Environment.NewLine, _aiCommandLog.TakeLast(8));

    public void SetSystemTab(string tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        ActiveSystemTab = tab;
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

    public async Task RunAiHookAsync(string command, params string[] args)
    {
        var scenePath = GetScenePath();
        if (scenePath is null || !File.Exists(scenePath))
        {
            StatusMessage = "Generate a prototype before running AI hooks.";
            return;
        }

        var projectRoot = ResolveRepositoryRoot();
        var finalArgs = new List<string> { command };
        finalArgs.AddRange(args);

        if (string.Equals(command, "modify-scene", StringComparison.Ordinal))
        {
            finalArgs.Add(scenePath);
            finalArgs.Add(string.IsNullOrWhiteSpace(AiPromptEditor) ? "Add 3 Houses" : AiPromptEditor);
        }

        if (string.Equals(command, "add-npc", StringComparison.Ordinal) && args.Length == 0)
        {
            finalArgs.Add("Generated NPC");
            finalArgs.Add("villager");
        }

        var startInfo = AiOrchestrationPanel.CreateOrchestratorStartInfo(projectRoot, finalArgs.ToArray());
        var oneLine = $"{startInfo.FileName} {string.Join(" ", startInfo.ArgumentList.Select(item => item.Contains(' ') ? $"\"{item}\"" : item))}";
        _aiCommandLog.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {oneLine}");
        OnPropertyChanged(nameof(AiCommandLog));

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            StatusMessage = "Failed to start orchestrator.py";
            return;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
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
        else
        {
            LoadViewportEntitiesFromScene(PrototypeRoot);
            ReloadSystemPanelsFromScene();
        }

        StatusMessage = string.IsNullOrWhiteSpace(stderr)
            ? $"AI hook complete: {command}"
            : $"AI hook complete with warnings: {stderr.Trim()}";
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

            if (SelectedBuildableEntityId == 0 && _buildings.Buildables.Count > 0)
            {
                SelectedBuildableEntityId = _buildings.Buildables[0].EntityId;
            }

            if (SelectedDialogEntityId == 0 && _dialogs.Npcs.Count > 0)
            {
                SelectedDialogEntityId = _dialogs.Npcs[0].EntityId;
            }

            OnPropertyChanged(nameof(DayCycleSpeedEditor));
            OnPropertyChanged(nameof(DayProgressEditor));
            OnPropertyChanged(nameof(DayCountEditor));
            OnPropertyChanged(nameof(BuildableEntities));
            OnPropertyChanged(nameof(PlayerInventorySummary));
            OnPropertyChanged(nameof(Recipes));
            OnPropertyChanged(nameof(DialogNpcs));
        }
        catch
        {
            // Keep existing system panel state if scene parse fails.
        }
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
        ReloadSystemPanelsFromScene();
    }

}
