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
