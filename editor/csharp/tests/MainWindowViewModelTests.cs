using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameForge.Editor.EditorShell.EditorSystems;
using GameForge.Editor.EditorShell.Services;
using GameForge.Editor.EditorShell.ViewModels;
using Moq;

namespace GameForge.Editor.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"gf-vm-tests-{Guid.NewGuid():N}");

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task ApplyAndSavePreferences_PersistsSettingsAndUpdatesLiveState()
    {
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var settingsPath = Path.Combine(_tempRoot, ".soulloom", "settings.json");
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object, settingsPath);

        await viewModel.ApplyAndSavePreferencesAsync(new EditorPreferences
        {
            General = new EditorPreferences.GeneralPreferences
            {
                Theme = "Light",
                AutosaveEnabled = false,
            },
            Runtime = new EditorPreferences.RuntimePreferences
            {
                VulkanResolution = "2560x1440",
                FpsLimit = 120,
            },
            Editor = new EditorPreferences.EditorPanePreferences
            {
                IconSize = 64,
                HistoryLength = 140,
                DefaultTemplateId = "rpg-quest",
                CreatorModeEnabled = false,
            },
        });

        Assert.False(viewModel.IsAutosaveEnabled);
        Assert.Equal("Light", viewModel.ThemePreference);
        Assert.Equal("Autosave: Off", viewModel.AutosaveStatusLabel);
        Assert.Equal("2560x1440 @ 120 FPS cap • Audio music_exploration", viewModel.RuntimePreferencesSummary);
        Assert.Equal("rpg-quest", viewModel.EditorDefaultTemplateId);
        Assert.False(viewModel.IsCreatorModeEnabled);
        Assert.True(File.Exists(settingsPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.Equal("Light", document.RootElement.GetProperty("General").GetProperty("Theme").GetString());
        Assert.False(document.RootElement.GetProperty("General").GetProperty("AutosaveEnabled").GetBoolean());
        Assert.False(document.RootElement.GetProperty("Editor").GetProperty("CreatorModeEnabled").GetBoolean());
    }

    [Fact]
    public async Task SetCreatorModeEnabledAsync_UpdatesWorkspacePreference()
    {
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var settingsPath = Path.Combine(_tempRoot, ".soulloom", "settings.json");
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object, settingsPath);

        Assert.True(viewModel.IsCreatorModeEnabled);

        await viewModel.SetCreatorModeEnabledAsync(false);

        Assert.False(viewModel.IsCreatorModeEnabled);
        Assert.Equal("Pro Mode", viewModel.WorkspaceModeLabel);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.False(document.RootElement.GetProperty("Editor").GetProperty("CreatorModeEnabled").GetBoolean());
    }

    [Fact]
    public async Task GenerateAndPlay_UpdatesStatusAndPidFromPipelineResponse()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();

        orchestrator
            .Setup(client => client.CreateBriefFromChatPrompt("Build a cozy village sim"))
            .Returns(Path.Combine(_tempRoot, "brief.json"));
        orchestrator
            .Setup(client => client.RunGenerationPipelineAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineRunResponse
            {
                ExitCode = 0,
                Stdout = "ok",
                Stderr = string.Empty,
                Result = new PipelineExecutionEnvelope
                {
                    Status = "Completed",
                    RuntimeLaunchStatus = "Running",
                    RuntimeLaunchPid = 4242,
                    PrototypeRoot = prototypeRoot,
                },
            });

        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object)
        {
            ChatPrompt = "Build a cozy village sim",
        };

        await viewModel.GenerateFromBriefAsync(launchRuntime: true);

        Assert.Equal(4242, viewModel.RuntimePid);
        Assert.Equal("Running", viewModel.RuntimeLaunchStatus);
        Assert.Contains("Runtime PID: 4242", viewModel.StatusMessage);
        Assert.Equal(prototypeRoot, viewModel.PrototypeRoot);

        orchestrator.VerifyAll();
    }

    [Fact]
    public async Task CreateProjectFromTemplate_GeneratesPrototypeAndLaunchesRuntime()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var template = MainWindowViewModel.GetProjectTemplatePresets().Single(entry => entry.Id == "cozy-colony");

        orchestrator
            .Setup(client => client.CreateBriefFromTemplate(template, "Moonlight Colony", "A gentle night market economy"))
            .Returns(Path.Combine(_tempRoot, "template-brief.json"));
        orchestrator
            .Setup(client => client.RunGenerationPipelineAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineRunResponse
            {
                ExitCode = 0,
                Stdout = "ok",
                Stderr = string.Empty,
                Result = new PipelineExecutionEnvelope
                {
                    Status = "Completed",
                    RuntimeLaunchStatus = "Running",
                    RuntimeLaunchPid = 5252,
                    PrototypeRoot = prototypeRoot,
                },
            });

        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object);

        await viewModel.CreateProjectFromTemplateAsync(template, "Moonlight Colony", "A gentle night market economy");

        Assert.Equal(prototypeRoot, viewModel.PrototypeRoot);
        Assert.Equal(5252, viewModel.RuntimePid);
        Assert.Equal("Running", viewModel.RuntimeLaunchStatus);
        Assert.Contains("Moonlight Colony", viewModel.ChatPrompt);
        Assert.Contains("Gather -> Build -> Care", viewModel.ChatPrompt);
        orchestrator.VerifyAll();
    }

    [Fact]
    public async Task SaveCodeEdits_RunsConfigureBuildAndRelaunch()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var sceneCppPath = Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp");
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();

        runtime
            .SetupSequence(service => service.RunProcessAsync("cmake", It.IsAny<string>(), prototypeRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainWindowViewModel.ProcessResult(0, "configure-ok", string.Empty))
            .ReturnsAsync(new MainWindowViewModel.ProcessResult(0, "build-ok", string.Empty));
        runtime
            .Setup(service => service.LaunchGeneratedRunner(Path.Combine(prototypeRoot, "generated", "build")))
            .Returns(new MainWindowViewModel.LaunchResult(true, 5151, string.Empty));

        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        viewModel.MonacoEditorContent = "// edited runtime code";

        await viewModel.SaveCodeEditsAsync();

        Assert.Equal("// edited runtime code", await File.ReadAllTextAsync(sceneCppPath));
        Assert.Equal(5151, viewModel.RuntimePid);
        Assert.Contains("Save & Recompile complete", viewModel.StatusMessage);

        runtime.Verify(service => service.RunProcessAsync("cmake", It.IsAny<string>(), prototypeRoot, It.IsAny<CancellationToken>()), Times.Exactly(2));
        runtime.Verify(service => service.LaunchGeneratedRunner(Path.Combine(prototypeRoot, "generated", "build")), Times.Once);
    }

    [Fact]
    public async Task SaveThenOpenProjectState_PreservesEditorSession()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var settingsPath = Path.Combine(_tempRoot, ".soulloom", "settings.json");
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot, settingsPath);
        var projectPath = Path.Combine(_tempRoot, "alpha-project.gfproj.json");

        viewModel.ChatPrompt = "Local-first polished editor";
        viewModel.IsCodeMode = true;

        var saved = await viewModel.SaveProjectStateAsync(projectPath);

        Assert.True(saved);
        Assert.True(File.Exists(projectPath));
        Assert.Equal(projectPath, viewModel.ActiveProjectFilePath);

        var reloaded = new MainWindowViewModel(orchestrator.Object, runtime.Object, settingsPath);
        var opened = await reloaded.OpenProjectStateAsync(projectPath);

        Assert.True(opened);
        Assert.Equal(projectPath, reloaded.ActiveProjectFilePath);
        Assert.Equal(prototypeRoot, reloaded.PrototypeRoot);
        Assert.Equal("Local-first polished editor", reloaded.ChatPrompt);
        Assert.True(reloaded.IsCodeMode);
        Assert.NotEmpty(reloaded.ViewportEntities);
    }

    [Fact]
    public async Task DeleteShortcut_RequiresSecondPressBeforeDeleting()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        viewModel.SelectSingleEntity("prop_01");

        await viewModel.HandleDeleteShortcutAsync();

        Assert.Contains(viewModel.ViewportEntities, entity => entity.Id == "prop_01");
        Assert.Contains("Press Delete again", viewModel.StatusToastMessage);

        await viewModel.HandleDeleteShortcutAsync();

        Assert.DoesNotContain(viewModel.ViewportEntities, entity => entity.Id == "prop_01");
    }

    [Fact]
    public async Task CommitDrag_UpdatesSceneAndTriggersRelaunch()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();

        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        Assert.True(viewModel.BeginDragForEntity("prop_01"));
        Assert.True(viewModel.PreviewDragPosition("prop_01", 9f, 4f));
        await viewModel.CommitDragAsync();

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var moved = document.RootElement.GetProperty("entities")[0];
        Assert.Equal(9f, moved.GetProperty("x").GetSingle());
        Assert.Equal(4f, moved.GetProperty("y").GetSingle());

        runtime.Verify(service => service.LaunchGeneratedRunner(Path.Combine(prototypeRoot, "generated", "build")), Times.Exactly(2));
    }

    [Fact]
    public void PreviewDragPosition_SyncsInspectorEditorsLive()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        Assert.True(viewModel.BeginDragForEntity("prop_01"));
        Assert.True(viewModel.PreviewDragPosition("prop_01", 5.5f, -2.25f));

        Assert.Equal("5.500", viewModel.SelectedEntityPositionXEditor);
        Assert.Equal("-2.250", viewModel.SelectedEntityPositionYEditor);
    }

    [Fact]
    public async Task UndoRedoStack_SupportsAddMoveDeleteSequence()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        await InvokePrivateAsync(viewModel, "AddEntityAndRelaunchAsync", "prop");
        Assert.Contains(viewModel.ViewportEntities, entity => entity.Id == "prop_01");

        Assert.True(viewModel.BeginDragForEntity("prop_01"));
        Assert.True(viewModel.PreviewDragPosition("prop_01", 6f, 2f));
        await viewModel.CommitDragAsync();

        viewModel.SelectedViewportEntity = viewModel.ViewportEntities.Single(entity => entity.Id == "prop_01");
        await InvokePrivateAsync(viewModel, "DeleteSelectedEntityAsync");
        Assert.DoesNotContain(viewModel.ViewportEntities, entity => entity.Id == "prop_01");
        Assert.True(viewModel.CanUndo);

        await InvokePrivateAsync(viewModel, "UndoAsync");
        Assert.Contains(viewModel.ViewportEntities, entity => entity.Id == "prop_01");

        await InvokePrivateAsync(viewModel, "UndoAsync");
        var movedEntity = viewModel.ViewportEntities.Single(entity => entity.Id == "prop_01");
        Assert.Equal(0f, movedEntity.X);
        Assert.Equal(0f, movedEntity.Y);

        await InvokePrivateAsync(viewModel, "RedoAsync");
        movedEntity = viewModel.ViewportEntities.Single(entity => entity.Id == "prop_01");
        Assert.Equal(6f, movedEntity.X);
        Assert.Equal(2f, movedEntity.Y);
    }

    [Fact]
    public async Task ApplySelectionProperties_UpdatesMultipleEntities()
    {
        var prototypeRoot = CreatePrototypeRootWithTwoEntities();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        viewModel.SelectSingleEntity("prop_01");
        viewModel.ToggleEntitySelection("prop_02");
        viewModel.SelectedEntityScaleEditor = "1.8";
        viewModel.SelectedEntityColorEditor = "#112233";

        await InvokePrivateAsync(viewModel, "ApplySelectionPropertiesAsync");

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var entities = document.RootElement.GetProperty("entities");
        Assert.All(entities.EnumerateArray(), entity =>
        {
            Assert.Equal(1.8f, entity.GetProperty("scale").GetSingle());
            Assert.Equal("#112233", entity.GetProperty("color").GetString());
        });
    }

    [Fact]
    public void BeginDirectPropertyEditForEntity_SelectsEntityAndUpdatesHint()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        var result = viewModel.BeginDirectPropertyEditForEntity("prop_01");

        Assert.True(result);
        Assert.NotNull(viewModel.SelectedViewportEntity);
        Assert.Equal("prop_01", viewModel.SelectedViewportEntity!.Id);
        Assert.Contains("Direct edit ready", viewModel.SelectionInteractionHint);
    }

    [Fact]
    public async Task Relaunch_CleansUpPreviousPidBeforeStartingNewRuntime()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object)
        {
            ChatPrompt = "Generate prototype",
        };

        orchestrator
            .Setup(client => client.CreateBriefFromChatPrompt(It.IsAny<string>()))
            .Returns(Path.Combine(_tempRoot, "brief.json"));
        orchestrator
            .Setup(client => client.RunGenerationPipelineAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineRunResponse
            {
                ExitCode = 0,
                Stdout = "ok",
                Stderr = string.Empty,
                Result = new PipelineExecutionEnvelope
                {
                    Status = "Completed",
                    RuntimeLaunchStatus = "Running",
                    RuntimeLaunchPid = 7001,
                    PrototypeRoot = prototypeRoot,
                },
            });

        await viewModel.GenerateFromBriefAsync(launchRuntime: true);
        await viewModel.PlayRuntimeAsync();

        runtime.Verify(service => service.TryStopRuntimeProcessAsync(7001, prototypeRoot, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(7070, viewModel.RuntimePid);
        Assert.Equal("Running", viewModel.RuntimeLaunchStatus);
    }

    [Fact]
    public async Task ImportAsset_PersistsAssetCatalogEntry()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        var texturePath = Path.Combine(_tempRoot, "tree.png");
        await File.WriteAllBytesAsync(texturePath, [137, 80, 78, 71]);

        var imported = await viewModel.ImportAssetAsync(texturePath);

        Assert.True(imported);
        Assert.Single(viewModel.ImportedAssets);
        Assert.Equal("texture", viewModel.ImportedAssets[0].Kind);

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        Assert.True(document.RootElement.TryGetProperty("imported_assets", out var importedAssets));
        Assert.Single(importedAssets.EnumerateArray());
    }

    [Fact]
    public async Task PlaceImportedAssetInScene_AddsEntityWithAssetMetadata()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        var texturePath = Path.Combine(_tempRoot, "ground.png");
        await File.WriteAllBytesAsync(texturePath, [137, 80, 78, 71]);
        await viewModel.ImportAssetAsync(texturePath);

        var placed = await viewModel.PlaceImportedAssetInSceneAsync(viewModel.ImportedAssets[0].Id, 3.5f, -1.25f);

        Assert.True(placed);
        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var entity = document.RootElement.GetProperty("entities")[0];
        Assert.Equal(viewModel.ImportedAssets[0].Id, entity.GetProperty("asset_id").GetString());
        Assert.Equal(texturePath, entity.GetProperty("asset_path").GetString());
        Assert.Equal(3.5f, entity.GetProperty("x").GetSingle());
        Assert.Equal(-1.25f, entity.GetProperty("y").GetSingle());
    }

    [Fact]
    public async Task AssetBrowser_FilterSupportsMultiTermSearchAndUpdatesRefreshLabel()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        var texturePath = Path.Combine(_tempRoot, "forest_floor.png");
        var modelPath = Path.Combine(_tempRoot, "forest_rock.obj");
        await File.WriteAllBytesAsync(texturePath, [137, 80, 78, 71]);
        await File.WriteAllTextAsync(modelPath, "o ForestRock");

        await viewModel.ImportAssetAsync(texturePath);
        await viewModel.ImportAssetAsync(modelPath);

        viewModel.AssetSearchText = "forest obj";

        Assert.Single(viewModel.FilteredImportedAssets);
        Assert.Equal("OBJ", viewModel.FilteredImportedAssets[0].ThumbnailBadge);
        Assert.StartsWith("Updated ", viewModel.AssetLastRefreshLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunExportChecklist_CreatesZipAndCompletesChecklist()
    {
        var prototypeRoot = CreatePrototypeRoot(withEntity: true);
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        await viewModel.RunExportChecklistAsync();

        Assert.Equal(4, viewModel.ExportChecklistTotalCount);
        Assert.Equal(4, viewModel.ExportChecklistCompletedCount);
        Assert.Equal(100, viewModel.ExportChecklistProgressPercent);
        Assert.True(File.Exists(viewModel.ExportOutputPath));
        Assert.True(File.Exists(viewModel.ExportPackagePath));
        Assert.True(Directory.Exists(viewModel.ExportFolderPath));
        Assert.Contains(".zip", viewModel.ExportOutputPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectStoryBeatForEditing_LoadsBeatIntoStoryEditors()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"),
            """
            {
              "player_spawn": { "x": 0, "y": 0 },
              "entities": [],
              "story": {
                "campaign_beats": [
                  { "id": "beat_intro", "title": "Arrival", "summary": "The caravan reaches town.", "completed": true }
                ]
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");

        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        var beat = Assert.Single(viewModel.StoryBeats);
        viewModel.SelectStoryBeatForEditing(beat);

        Assert.Equal("beat_intro", viewModel.StoryBeatIdEditor);
        Assert.Equal("Arrival", viewModel.StoryBeatTitleEditor);
        Assert.Equal("The caravan reaches town.", viewModel.StoryBeatSummaryEditor);
        Assert.True(viewModel.StoryBeatCompletedEditor);
    }

    [Fact]
    public async Task AcceptStoryBeatSuggestion_RequiresSignOffAndPersistsBeat()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        var suggestion = new CoCreatorSuggestion(
            "sg_story_1",
            "Bridge Ambush",
            "Escalates faction tension.",
            new JsonObject
            {
                ["type"] = "story_add_beat",
                ["beat_id"] = "beat_bridge_ambush",
                ["title"] = "Bridge Ambush",
                ["summary"] = "Raiders block the crossing and demand tribute.",
            });

        var suggestionsField = typeof(MainWindowViewModel).GetField("_coCreatorSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field _coCreatorSuggestions not found.");
        var suggestions = (System.Collections.ObjectModel.ObservableCollection<CoCreatorSuggestion>)suggestionsField.GetValue(viewModel)!;
        suggestions.Add(suggestion);
        viewModel.ReloadSystemPanelsFromScene();
        viewModel.SelectedStoryBeatSuggestion = suggestion;

        viewModel.StageSelectedStorySuggestionForEdit();
        viewModel.StoryBeatTitleEditor = "Bridge Ambush (Player Revised)";
        await viewModel.AcceptStoryBeatSuggestionAsync();

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var savedBeat = document.RootElement.GetProperty("story").GetProperty("campaign_beats")
            .EnumerateArray()
            .Single(node => node.GetProperty("id").GetString() == "beat_bridge_ambush");
        Assert.Equal("Bridge Ambush (Player Revised)", savedBeat.GetProperty("title").GetString());
        Assert.Equal("Raiders block the crossing and demand tribute.", savedBeat.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task SaveLivingNpcSettings_PersistsAndReloadsFreeWillState()
    {
        var prototypeRoot = CreatePrototypeRoot();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        viewModel.LivingNpcsFreeWillEnabledEditor = false;
        viewModel.LivingNpcsLlmEnabledEditor = false;
        viewModel.LivingNpcsSparkChancePerSecondEditor = 0.0125f;
        viewModel.LivingNpcsMaxSparksPerNpcPerDayEditor = 9;
        viewModel.LivingNpcsModelPathEditor = "models/custom-living.gguf";

        await viewModel.SaveLivingNpcsSettingsAsync();
        viewModel.ReloadSystemPanelsFromScene();

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var freeWill = document.RootElement.GetProperty("free_will");
        Assert.False(freeWill.GetProperty("enabled").GetBoolean());
        Assert.False(freeWill.GetProperty("llm_enabled").GetBoolean());
        Assert.Equal(0.0125f, freeWill.GetProperty("spark_chance_per_second").GetSingle());
        Assert.Equal(9, freeWill.GetProperty("max_sparks_per_npc_per_day").GetInt32());
        Assert.Equal("models/custom-living.gguf", freeWill.GetProperty("model_path").GetString());

        Assert.False(viewModel.LivingNpcsFreeWillEnabledEditor);
        Assert.False(viewModel.LivingNpcsLlmEnabledEditor);
        Assert.Equal(0.0125f, viewModel.LivingNpcsSparkChancePerSecondEditor);
        Assert.Equal(9, viewModel.LivingNpcsMaxSparksPerNpcPerDayEditor);
        Assert.Equal("models/custom-living.gguf", viewModel.LivingNpcsModelPathEditor);
    }

    [Fact]
    public async Task AssignScriptedBehaviorToSelection_PersistsStateAndPreview()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));
        await File.WriteAllTextAsync(
            Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"),
            """
            {
              "player_spawn": { "x": 0, "y": 0 },
              "scripted_behavior": { "definitions_path": "scripted_behaviors.json" },
              "entities": [
                { "id": 7, "type": "npc", "name": "Pella", "x": 2, "y": 1, "z": 0 }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(prototypeRoot, "scripted_behaviors.json"),
            """
            {
              "states": [
                { "name": "guard", "activity": "guard", "location": "work", "duration_hours": 0.5, "complex": false }
              ]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");

        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        viewModel.SelectedDialogEntityId = 7;
        viewModel.ScriptedBehaviorStateEditor = "guard";
        viewModel.ScriptedBehaviorParamsEditor = "duration_hours=0.75";

        await viewModel.AssignScriptedBehaviorToSelectionAsync();
        viewModel.ReloadSystemPanelsFromScene();

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var scripted = document.RootElement.GetProperty("entities")[0].GetProperty("scripted_behavior");
        Assert.True(scripted.GetProperty("enabled").GetBoolean());
        Assert.Equal("guard", scripted.GetProperty("current_state").GetString());
        Assert.Equal(0.75f, scripted.GetProperty("parameters").GetProperty("duration_hours").GetSingle());
        Assert.Contains("guard", viewModel.AvailableScriptedBehaviorStates);
        Assert.Contains(viewModel.ScriptedBehaviorNpcPreview, line => line.Contains("Pella") && line.Contains("guard"));
    }

    [Fact]
    public async Task AssignScriptedBehaviorToSelection_WarnsOnComplexStateInPerformanceMode()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));
        await File.WriteAllTextAsync(
            Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"),
            """
            {
              "player_spawn": { "x": 0, "y": 0 },
              "optimization_overrides": { "lightweight_mode": "performance" },
              "scripted_behavior": { "definitions_path": "scripted_behaviors.json" },
              "entities": [
                { "id": 9, "type": "npc", "name": "Garr", "x": 2, "y": 1, "z": 0 }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(prototypeRoot, "scripted_behaviors.json"),
            """
            {
              "states": [
                { "name": "socialize", "activity": "socialize", "location": "town", "duration_hours": 0.2, "complex": true }
              ]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");

        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        viewModel.ReloadSystemPanelsFromScene();
        viewModel.SelectedDialogEntityId = 9;
        viewModel.ScriptedBehaviorStateEditor = "socialize";

        await viewModel.AssignScriptedBehaviorToSelectionAsync();

        Assert.Contains("Lightweight mode is set to performance", viewModel.ScriptedBehaviorStatus);
    }

    [Fact]
    public async Task AcceptCoCreatorSuggestion_AppliesLivingNpcAdjustments()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"),
            """
            {
              "player_spawn": { "x": 0, "y": 0 },
              "entities": [
                { "id": 11, "type": "npc", "name": "Mara", "x": 2, "y": 3, "z": 0 }
              ]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");

        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);
        var suggestion = new CoCreatorSuggestion(
            "sg_living_1",
            "Mara schedule tweak",
            "Adds clearer home/work anchors and need pressure.",
            new JsonObject
            {
                ["type"] = "living_npc_adjustment",
                ["npc_id"] = 11,
                ["home"] = new JsonObject { ["x"] = 6, ["y"] = 4 },
                ["work"] = new JsonObject { ["x"] = 12, ["y"] = -1 },
                ["needs_modifiers"] = new JsonObject { ["social"] = 8, ["energy"] = -15 },
            });

        var suggestionsField = typeof(MainWindowViewModel).GetField("_coCreatorSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field _coCreatorSuggestions not found.");
        var suggestions = (System.Collections.ObjectModel.ObservableCollection<CoCreatorSuggestion>)suggestionsField.GetValue(viewModel)!;
        suggestions.Add(suggestion);
        viewModel.SelectedCoCreatorSuggestion = suggestion;

        await viewModel.AcceptCoCreatorSuggestionAsync();

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var npc = document.RootElement.GetProperty("entities")[0];
        Assert.Equal(6f, npc.GetProperty("schedule").GetProperty("home_position").GetProperty("x").GetSingle());
        Assert.Equal(4f, npc.GetProperty("schedule").GetProperty("home_position").GetProperty("z").GetSingle());
        Assert.Equal(12f, npc.GetProperty("schedule").GetProperty("workplace_position").GetProperty("x").GetSingle());
        Assert.Equal(-1f, npc.GetProperty("schedule").GetProperty("workplace_position").GetProperty("z").GetSingle());
        Assert.Equal(68f, npc.GetProperty("needs").GetProperty("social").GetSingle());
        Assert.Equal(65f, npc.GetProperty("needs").GetProperty("energy").GetSingle());
    }

    [Fact]
    public void DownloadProgress_OnboardingStageUpdatesStatusBeforeTransferStarts()
    {
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object);

        var handled = InvokePrivate<bool>(
            viewModel,
            "TryHandleModelProgressLine",
            """{"event":"onboarding_stage","stage":"benchmark_complete"}""");

        Assert.True(handled);
        Assert.Equal("Preparing ForgeGuard", viewModel.DownloadProgressTitle);
        Assert.Equal("Hardware benchmark complete. Preparing ForgeGuard download...", viewModel.DownloadProgressSummary);
        Assert.Equal("Benchmark finished. Contacting the model host...", viewModel.DownloadProgressCurrentFile);
        Assert.True(viewModel.IsDownloadProgressIndeterminate);
    }

    [Fact]
    public void HierarchyTree_BuildsNestedEntityNodesFromScene()
    {
        var prototypeRoot = CreatePrototypeRootWithHierarchy();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        var sceneRoot = Assert.Single(viewModel.HierarchyRoots);
        var playerNode = sceneRoot.Children.Single(node => node.Label == "Player");
        Assert.Equal("player_spawn", playerNode.EntityId);
        var miscGroup = sceneRoot.Children.Single(node => node.Label == "Groups");
        var squad = Assert.Single(miscGroup.Children.Where(node => node.EntityId == "group_01"));
        var squadChild = Assert.Single(squad.Children.Where(node => node.EntityId == "npc_01"));
        Assert.Equal("Prop Barrel", Assert.Single(squadChild.Children).Label);
        Assert.True(sceneRoot.IsExpanded);
        Assert.True(miscGroup.IsExpanded);
        Assert.Equal(4, viewModel.HierarchyEntityCount);
    }

    [Fact]
    public async Task SelectingHierarchyNode_SyncsViewportSelectionAndSupportsReparent()
    {
        var prototypeRoot = CreatePrototypeRootWithHierarchy();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        var sceneRoot = Assert.Single(viewModel.HierarchyRoots);
        var propNode = FindHierarchyEntityNode(sceneRoot, "prop_01");
        Assert.NotNull(propNode);

        viewModel.SelectedHierarchyNode = propNode;

        Assert.Equal("prop_01", viewModel.SelectedViewportEntity?.Id);
        Assert.Equal("🎯 Prop Barrel", viewModel.HierarchySelectionBadge);

        var groupsNode = sceneRoot.Children.Single(node => node.Label == "Groups");
        groupsNode.IsExpanded = false;

        var reparented = await viewModel.ReparentEntityAsync("prop_01", "group_01");
        Assert.True(reparented);
        Assert.Equal("group_01", viewModel.ViewportEntities.Single(entity => entity.Id == "prop_01").ParentId);

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var entity = document.RootElement.GetProperty("entities")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "prop_01");
        Assert.Equal("group_01", entity.GetProperty("parent_id").GetString());

        var rebuiltRoot = Assert.Single(viewModel.HierarchyRoots);
        var rebuiltGroupsNode = rebuiltRoot.Children.Single(node => node.Label == "Groups");
        Assert.False(rebuiltGroupsNode.IsExpanded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private MainWindowViewModel CreateGeneratedViewModel(
        Mock<MainWindowViewModel.IOrchestratorGateway> orchestrator,
        Mock<MainWindowViewModel.IRuntimeSupervisor> runtime,
        string prototypeRoot,
        string? settingsPath = null)
    {
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object, settingsPath)
        {
            ChatPrompt = "test prompt",
        };

        orchestrator
            .Setup(client => client.CreateBriefFromChatPrompt(It.IsAny<string>()))
            .Returns(Path.Combine(_tempRoot, "brief.json"));
        orchestrator
            .Setup(client => client.RunGenerationPipelineAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineRunResponse
            {
                ExitCode = 0,
                Stdout = "ok",
                Stderr = string.Empty,
                Result = new PipelineExecutionEnvelope
                {
                    Status = "Completed",
                    RuntimeLaunchStatus = "Running",
                    RuntimeLaunchPid = 6001,
                    PrototypeRoot = prototypeRoot,
                },
            });

        viewModel.GenerateFromBriefAsync(launchRuntime: true).GetAwaiter().GetResult();
        return viewModel;
    }

    private Mock<MainWindowViewModel.IRuntimeSupervisor> CreateRuntimeSupervisorMock()
    {
        var runtime = new Mock<MainWindowViewModel.IRuntimeSupervisor>(MockBehavior.Strict);

        runtime
            .Setup(service => service.TryStopRuntimeProcessAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainWindowViewModel.RuntimeStopResult(true, string.Empty));
        runtime
            .Setup(service => service.RunProcessAsync("cmake", It.Is<string>(args => args.Contains("-S ")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainWindowViewModel.ProcessResult(0, "configure-ok", string.Empty));
        runtime
            .Setup(service => service.RunProcessAsync("cmake", It.Is<string>(args => args.Contains("--build")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainWindowViewModel.ProcessResult(0, "build-ok", string.Empty));
        runtime
            .Setup(service => service.LaunchGeneratedRunner(It.IsAny<string>()))
            .Returns(new MainWindowViewModel.LaunchResult(true, 7070, string.Empty));

        return runtime;
    }

    private string CreatePrototypeRoot(bool withEntity = false)
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));

        var scene = withEntity
            ? """
              {
                "player_spawn": { "x": 0, "y": 0 },
                "entities": [
                  { "id": "prop_01", "type": "prop", "x": 0, "y": 0, "z": 0 }
                ]
              }
              """
            : """
              {
                "player_spawn": { "x": 0, "y": 0 },
                "entities": []
              }
              """;

        File.WriteAllText(Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"), scene);
        File.WriteAllText(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");
        return prototypeRoot;
    }

    private string CreatePrototypeRootWithTwoEntities()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));

        const string scene = """
                             {
                               "player_spawn": { "x": 0, "y": 0 },
                               "entities": [
                                 { "id": "prop_01", "type": "prop", "x": 0, "y": 0, "z": 0 },
                                 { "id": "prop_02", "type": "prop", "x": 3, "y": 1, "z": 0 }
                               ]
                             }
                             """;
        File.WriteAllText(Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"), scene);
        File.WriteAllText(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");
        return prototypeRoot;
    }

    private string CreatePrototypeRootWithHierarchy()
    {
        var prototypeRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "scene"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "cpp"));
        Directory.CreateDirectory(Path.Combine(prototypeRoot, "generated", "build"));

        const string scene = """
                             {
                               "player_spawn": { "x": 0, "y": 0 },
                               "entities": [
                                 { "id": "group_01", "type": "group", "name": "Squad Root", "x": 0, "y": 0, "z": 0 },
                                 { "id": "npc_01", "type": "npc", "name": "Village Guard", "x": 2, "y": 1, "z": 0, "parent_id": "group_01" },
                                 { "id": "prop_01", "type": "prop", "name": "Prop Barrel", "x": 1, "y": -1, "z": 0, "parent_id": "npc_01" },
                                 { "id": "prop_02", "type": "prop", "name": "Loose Crate", "x": -3, "y": 1, "z": 0 }
                               ]
                             }
                             """;
        File.WriteAllText(Path.Combine(prototypeRoot, "scene", "scene_scaffold.json"), scene);
        File.WriteAllText(Path.Combine(prototypeRoot, "generated", "cpp", "scene.cpp"), "// runtime scene code");
        return prototypeRoot;
    }

    private static MainWindowViewModel.HierarchyNode? FindHierarchyEntityNode(MainWindowViewModel.HierarchyNode root, string entityId)
    {
        if (string.Equals(root.EntityId, entityId, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = FindHierarchyEntityNode(child, entityId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static async Task InvokePrivateAsync(MainWindowViewModel viewModel, string methodName, params object[] arguments)
    {
        var method = typeof(MainWindowViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} not found");

        var task = (Task?)method.Invoke(viewModel, arguments);
        if (task is not null)
        {
            await task;
        }
    }

    private static T InvokePrivate<T>(MainWindowViewModel viewModel, string methodName, params object[] arguments)
    {
        var method = typeof(MainWindowViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} not found");
        var result = method.Invoke(viewModel, arguments);
        return result is T typed
            ? typed
            : throw new InvalidOperationException($"Method {methodName} did not return {typeof(T).Name}.");
    }
}
