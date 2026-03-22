using System.Text.Json;
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
            panel => Assert.Equal("Asset Browser", panel.DisplayName),
            panel => Assert.Equal("Hierarchy", panel.DisplayName),
            panel => Assert.Equal("Inspector", panel.DisplayName),
            panel => Assert.Equal("Viewport", panel.DisplayName));
    }

    [Fact]
    public void AssetBrowserFilter_ReturnsTagAndCategoryMatches()
    {
        var snapshot = new EditorProjectSnapshot
        {
            ProjectName = "Asset Test",
            Scope = "single-player baseline",
            Rendering = "vulkan-first",
            Platforms = ["windows", "ubuntu"],
            SceneObjects = [],
            Assets =
            [
                new AssetCatalogEntry
                {
                    AssetId = "asset-0001",
                    DisplayName = "Forest Archer Portrait",
                    Category = "ui",
                    Tags = ["portrait", "character", "manual-upload"],
                    LicenseId = "cc0-1.0",
                    SourceType = "manual-upload",
                    RelativePath = "assets/library/asset-0001.png",
                    ImportedAtUtc = "2026-03-22T00:00:00Z",
                },
                new AssetCatalogEntry
                {
                    AssetId = "asset-0002",
                    DisplayName = "Forest Ambience Loop",
                    Category = "audio",
                    Tags = ["ambient", "loop", "ai-generated"],
                    LicenseId = "cc-by-4.0",
                    SourceType = "ai-generated",
                    RelativePath = "assets/library/asset-0002.ogg",
                    ImportedAtUtc = "2026-03-22T00:00:00Z",
                },
            ],
        };

        var workspace = new EditorWorkspace(snapshot);
        var filtered = workspace.QueryAssets(new AssetBrowserFilter
        {
            Query = "forest",
            Category = "audio",
            RequiredTags = ["ai-generated"],
        });

        Assert.Single(filtered.Results);
        Assert.Equal("asset-0002", filtered.Results[0].AssetId);
    }

    [Fact]
    public async Task UndoRedoTimeline_SupportsMultiStepRollbackAndReapply()
    {
        var workspace = await LoadWorkspaceAsync();
        Assert.True(workspace.SelectObject("main-camera"));

        var first = workspace.ApplyAiMutation(new AiMutationRequest
        {
            MutationId = "minor-camera-zoom",
            TargetObjectId = "main-camera",
            Summary = "Adjust camera zoom for cozy framing",
            ImpactLevel = AiEditImpactLevel.Minor,
            PropertyChanges = new Dictionary<string, JsonElement>
            {
                ["zoom"] = JsonSerializer.SerializeToElement(1.3),
            },
        });

        var second = workspace.ApplyAiMutation(new AiMutationRequest
        {
            MutationId = "minor-camera-mode",
            TargetObjectId = "main-camera",
            Summary = "Set camera mode to strategic",
            ImpactLevel = AiEditImpactLevel.Minor,
            PropertyChanges = new Dictionary<string, JsonElement>
            {
                ["mode"] = JsonSerializer.SerializeToElement("strategic"),
            },
        });

        Assert.Equal(AiMutationApplyStatus.Applied, first.Status);
        Assert.Equal(AiMutationApplyStatus.Applied, second.Status);
        Assert.Equal(2, workspace.Timeline.Count);
        Assert.Equal("strategic", workspace.AiContext!.Properties["mode"]);

        Assert.True(workspace.Undo());
        Assert.Equal("follow_player", workspace.AiContext.Properties["mode"]);

        Assert.True(workspace.Undo());
        Assert.False(workspace.AiContext.Properties.ContainsKey("zoom"));
        Assert.False(workspace.Undo());

        Assert.True(workspace.Redo());
        Assert.Equal("1.3", workspace.AiContext.Properties["zoom"]);
        Assert.True(workspace.Redo());
        Assert.Equal("strategic", workspace.AiContext.Properties["mode"]);
        Assert.False(workspace.Redo());
    }

    [Fact]
    public async Task MajorMutation_RequiresPreviewAndSupportsRollback()
    {
        var workspace = await LoadWorkspaceAsync();
        Assert.True(workspace.SelectObject("player-spawn"));
        var originalMode = workspace.AiContext!.Properties["mode"];

        var result = workspace.ApplyAiMutation(new AiMutationRequest
        {
            MutationId = "major-player-spawn-overhaul",
            TargetObjectId = "player-spawn",
            Summary = "Overhaul player spawn behavior",
            ImpactLevel = AiEditImpactLevel.Major,
            PropertyChanges = new Dictionary<string, JsonElement>
            {
                ["mode"] = JsonSerializer.SerializeToElement("auto_navigation"),
                ["safe_radius"] = JsonSerializer.SerializeToElement(8),
            },
        });

        Assert.Equal(AiMutationApplyStatus.PreviewRequired, result.Status);
        Assert.NotNull(result.Preview);
        Assert.NotNull(workspace.PendingPreview);
        Assert.Equal(originalMode, workspace.AiContext.Properties["mode"]);

        Assert.True(workspace.ConfirmPendingMajorMutation());
        Assert.Null(workspace.PendingPreview);
        Assert.Equal("auto_navigation", workspace.AiContext.Properties["mode"]);
        Assert.Equal("8", workspace.AiContext.Properties["safe_radius"]);

        Assert.True(workspace.Undo());
        Assert.Equal(originalMode, workspace.AiContext.Properties["mode"]);
        Assert.False(workspace.AiContext.Properties.ContainsKey("safe_radius"));
    }

    [Fact]
    public async Task MajorMutationConfirmation_IsBlockedIfWorkspaceStateChanged()
    {
        var workspace = await LoadWorkspaceAsync();
        Assert.True(workspace.SelectObject("main-camera"));

        var majorResult = workspace.ApplyAiMutation(new AiMutationRequest
        {
            MutationId = "major-camera-overhaul",
            TargetObjectId = "main-camera",
            Summary = "Major camera overhaul",
            ImpactLevel = AiEditImpactLevel.Major,
            PropertyChanges = new Dictionary<string, JsonElement>
            {
                ["mode"] = JsonSerializer.SerializeToElement("cinematic"),
            },
        });

        Assert.Equal(AiMutationApplyStatus.PreviewRequired, majorResult.Status);
        Assert.NotNull(workspace.PendingPreview);

        var minorResult = workspace.ApplyAiMutation(new AiMutationRequest
        {
            MutationId = "minor-camera-fov",
            TargetObjectId = "main-camera",
            Summary = "Adjust camera zoom for near framing",
            ImpactLevel = AiEditImpactLevel.Minor,
            PropertyChanges = new Dictionary<string, JsonElement>
            {
                ["zoom"] = JsonSerializer.SerializeToElement(1.1),
            },
        });

        Assert.Equal(AiMutationApplyStatus.Applied, minorResult.Status);
        Assert.Null(workspace.PendingPreview);
        Assert.Equal("1.1", workspace.AiContext!.Properties["zoom"]);
        Assert.False(workspace.ConfirmPendingMajorMutation());
        Assert.Equal("follow_player", workspace.AiContext.Properties["mode"]);
    }

    private static async Task<EditorWorkspace> LoadWorkspaceAsync()
    {
        var projectRoot = ResolveProjectRoot();
        var samplePath = Path.Combine(projectRoot, "app", "samples", "generated-prototype", "cozy-colony-tales");
        var snapshot = await EditorProjectLoader.LoadGeneratedProjectAsync(samplePath);
        return new EditorWorkspace(snapshot);
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
