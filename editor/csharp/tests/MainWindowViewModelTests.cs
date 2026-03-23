using System.Reflection;
using System.Text.Json;
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
        var settingsPath = Path.Combine(_tempRoot, ".forgeengine", "settings.json");
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
            },
        });

        Assert.False(viewModel.IsAutosaveEnabled);
        Assert.Equal("Light", viewModel.ThemePreference);
        Assert.Equal("Autosave: Off", viewModel.AutosaveStatusLabel);
        Assert.Equal("2560x1440 @ 120 FPS cap", viewModel.RuntimePreferencesSummary);
        Assert.Equal("rpg-quest", viewModel.EditorDefaultTemplateId);
        Assert.True(File.Exists(settingsPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.Equal("Light", document.RootElement.GetProperty("General").GetProperty("Theme").GetString());
        Assert.False(document.RootElement.GetProperty("General").GetProperty("AutosaveEnabled").GetBoolean());
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
    public void HierarchyTree_BuildsNestedEntityNodesFromScene()
    {
        var prototypeRoot = CreatePrototypeRootWithHierarchy();
        var orchestrator = new Mock<MainWindowViewModel.IOrchestratorGateway>(MockBehavior.Strict);
        var runtime = CreateRuntimeSupervisorMock();
        var viewModel = CreateGeneratedViewModel(orchestrator, runtime, prototypeRoot);

        var sceneRoot = Assert.Single(viewModel.HierarchyRoots);
        var miscGroup = sceneRoot.Children.Single(node => node.Label == "Groups");
        var squad = Assert.Single(miscGroup.Children.Where(node => node.EntityId == "group_01"));
        var squadChild = Assert.Single(squad.Children.Where(node => node.EntityId == "npc_01"));
        Assert.Equal("Prop Barrel", Assert.Single(squadChild.Children).Label);
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

        var reparented = await viewModel.ReparentEntityAsync("prop_01", "group_01");
        Assert.True(reparented);
        Assert.Equal("group_01", viewModel.ViewportEntities.Single(entity => entity.Id == "prop_01").ParentId);

        var scenePath = Path.Combine(prototypeRoot, "scene", "scene_scaffold.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scenePath));
        var entity = document.RootElement.GetProperty("entities")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "prop_01");
        Assert.Equal("group_01", entity.GetProperty("parent_id").GetString());
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
        string prototypeRoot)
    {
        var viewModel = new MainWindowViewModel(orchestrator.Object, runtime.Object)
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
}
