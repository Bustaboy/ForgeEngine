using GameForge.Editor.EditorShell;

namespace GameForge.Editor.Tests;

public sealed class EditorShellTests
{
    [Fact]
    public async Task GeneratedProject_LoadsIntoWorkspaceWithoutCrash()
    {
        var projectRoot = ResolveProjectRoot();
        var samplePath = Path.Combine(projectRoot, "app", "samples", "generated-prototype", "cozy-colony-tales");

        var snapshot = await EditorProjectLoader.LoadGeneratedProjectAsync(samplePath);

        Assert.Equal("Cozy Colony Tales", snapshot.ProjectName);
        Assert.Equal("vulkan-first", snapshot.Rendering);
        Assert.Contains("windows", snapshot.Platforms);
        Assert.Contains("ubuntu", snapshot.Platforms);
        Assert.NotEmpty(snapshot.SceneObjects);
    }

    [Fact]
    public async Task Selection_UpdatesInspectorAndAiContext()
    {
        var projectRoot = ResolveProjectRoot();
        var samplePath = Path.Combine(projectRoot, "app", "samples", "generated-prototype", "cozy-colony-tales");
        var snapshot = await EditorProjectLoader.LoadGeneratedProjectAsync(samplePath);
        var workspace = new EditorWorkspace(snapshot);

        var selected = workspace.SelectObject("main-camera");

        Assert.True(selected);
        Assert.NotNull(workspace.Inspector);
        Assert.NotNull(workspace.AiContext);
        Assert.Equal("Main Camera", workspace.Inspector!.ObjectLabel);
        Assert.Contains("Type", workspace.Inspector.SimpleSection.Keys);
        Assert.Equal("Main Camera", workspace.AiContext!.ObjectLabel);
        Assert.Equal("Camera", workspace.AiContext.ObjectType);
        Assert.Contains("follow_player", workspace.AiContext.Properties.Keys);
    }

    [Fact]
    public void DefaultLayout_ContainsRequiredDockedPanels()
    {
        var layout = EditorLayout.CreateDefault();

        Assert.Collection(
            layout.Panels.OrderBy(panel => panel.DisplayName),
            panel => Assert.Equal("AI Copilot Chat", panel.DisplayName),
            panel => Assert.Equal("Hierarchy", panel.DisplayName),
            panel => Assert.Equal("Inspector", panel.DisplayName),
            panel => Assert.Equal("Viewport", panel.DisplayName));
    }

    private static string ResolveProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("..", i))));
            if (File.Exists(Path.Combine(candidate, "GAMEFORGE_V1_BLUEPRINT.md")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root from test base directory.");
    }
}
