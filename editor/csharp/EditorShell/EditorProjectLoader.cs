using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public static class EditorProjectLoader
{
    public static async Task<EditorProjectSnapshot> LoadGeneratedProjectAsync(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        var manifestPath = Path.Combine(projectRoot, "prototype-manifest.json");
        var scenePath = Path.Combine(projectRoot, "scene", "scene_scaffold.json");
        var assetCatalogPath = Path.Combine(projectRoot, "assets", "catalog.v1.json");
        var styleLibraryPath = Path.Combine(projectRoot, "config", "style-presets.v1.json");
        var projectStylePath = Path.Combine(projectRoot, "config", "project-style.v1.json");

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Generated project manifest missing.", manifestPath);
        }

        if (!File.Exists(scenePath))
        {
            throw new FileNotFoundException("Generated scene scaffold missing.", scenePath);
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        using var manifestDoc = await JsonDocument.ParseAsync(manifestStream);

        var root = manifestDoc.RootElement;
        var projectName = root.GetProperty("project_name").GetString() ?? "Generated Project";
        var scope = root.GetProperty("scope").GetString() ?? "single-player baseline";
        var rendering = root.GetProperty("rendering").GetString() ?? "vulkan-first";

        var platforms = root.GetProperty("platforms")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        await using var sceneStream = File.OpenRead(scenePath);
        using var sceneDoc = await JsonDocument.ParseAsync(sceneStream);
        var sceneRoot = sceneDoc.RootElement;

        var sceneObjects = BuildSceneObjects(sceneRoot);
        var assets = await BuildAssetCatalogAsync(assetCatalogPath);
        var style = await BuildProjectStyleConfigAsync(styleLibraryPath, projectStylePath);

        return new EditorProjectSnapshot
        {
            ProjectName = projectName,
            Scope = scope,
            Rendering = rendering,
            Platforms = platforms,
            SceneObjects = sceneObjects,
            Assets = assets,
            Style = style,
        };
    }

    private static async Task<IReadOnlyList<AssetCatalogEntry>> BuildAssetCatalogAsync(string assetCatalogPath)
    {
        if (!File.Exists(assetCatalogPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(assetCatalogPath);
        using var doc = await JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("assets", out var assetsNode) || assetsNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var output = new List<AssetCatalogEntry>();
        foreach (var assetNode in assetsNode.EnumerateArray())
        {
            var tags = assetNode.TryGetProperty("tags", out var tagsNode) && tagsNode.ValueKind == JsonValueKind.Array
                ? tagsNode.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList()
                : [];

            output.Add(new AssetCatalogEntry
            {
                AssetId = assetNode.TryGetProperty("asset_id", out var assetId) ? assetId.GetString() ?? string.Empty : string.Empty,
                DisplayName = assetNode.TryGetProperty("display_name", out var displayName) ? displayName.GetString() ?? string.Empty : string.Empty,
                Category = assetNode.TryGetProperty("category", out var category) ? category.GetString() ?? string.Empty : string.Empty,
                Tags = tags,
                LicenseId = assetNode.TryGetProperty("license_id", out var licenseId) ? licenseId.GetString() ?? string.Empty : string.Empty,
                SourceType = assetNode.TryGetProperty("source_type", out var sourceType) ? sourceType.GetString() ?? string.Empty : string.Empty,
                RelativePath = assetNode.TryGetProperty("relative_path", out var relativePath) ? relativePath.GetString() ?? string.Empty : string.Empty,
                ImportedAtUtc = assetNode.TryGetProperty("imported_at_utc", out var importedAtUtc) ? importedAtUtc.GetString() ?? string.Empty : string.Empty,
            });
        }

        return output;
    }

    private static IReadOnlyList<SceneObject> BuildSceneObjects(JsonElement sceneRoot)
    {
        var objects = new List<SceneObject>();

        objects.Add(new SceneObject
        {
            ObjectId = "world-root",
            DisplayName = sceneRoot.TryGetProperty("scene_id", out var sceneId) ? sceneId.GetString() ?? "World" : "World",
            ObjectType = "SceneRoot",
        });

        if (sceneRoot.TryGetProperty("player_spawn", out var playerSpawn))
        {
            objects.Add(new SceneObject
            {
                ObjectId = "player-spawn",
                DisplayName = "Player Spawn",
                ObjectType = "Transform",
                Properties = ExtractProperties(playerSpawn),
            });
        }

        if (sceneRoot.TryGetProperty("camera", out var camera))
        {
            objects.Add(new SceneObject
            {
                ObjectId = "main-camera",
                DisplayName = "Main Camera",
                ObjectType = "Camera",
                Properties = ExtractProperties(camera),
            });
        }

        return objects;
    }

    private static async Task<ProjectStyleConfig> BuildProjectStyleConfigAsync(string styleLibraryPath, string projectStylePath)
    {
        var defaultConfig = ProjectStyleConfig.CreateDefault();
        var presets = defaultConfig.Presets.ToList();

        if (File.Exists(styleLibraryPath))
        {
            await using var presetStream = File.OpenRead(styleLibraryPath);
            using var presetDoc = await JsonDocument.ParseAsync(presetStream);
            if (presetDoc.RootElement.TryGetProperty("custom_presets", out var customNode) && customNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var presetNode in customNode.EnumerateArray())
                {
                    var presetId = presetNode.TryGetProperty("preset_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
                    var displayName = presetNode.TryGetProperty("display_name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(presetId) || string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    presets.Add(new StylePresetDefinition
                    {
                        PresetId = presetId,
                        DisplayName = displayName,
                        ParentPresetId = presetNode.TryGetProperty("parent_preset_id", out var parentNode) ? parentNode.GetString() : null,
                        Source = "user",
                    });
                }
            }
        }

        var activePresetId = defaultConfig.ActivePresetId;
        var helperMode = defaultConfig.HelperMode;
        if (File.Exists(projectStylePath))
        {
            await using var styleStateStream = File.OpenRead(projectStylePath);
            using var styleDoc = await JsonDocument.ParseAsync(styleStateStream);
            var root = styleDoc.RootElement;
            activePresetId = root.TryGetProperty("active_preset_id", out var activeNode)
                ? activeNode.GetString() ?? defaultConfig.ActivePresetId
                : defaultConfig.ActivePresetId;
            helperMode = root.TryGetProperty("helper_mode", out var helperNode)
                ? helperNode.GetString() ?? defaultConfig.HelperMode
                : defaultConfig.HelperMode;
        }

        return new ProjectStyleConfig
        {
            ActivePresetId = activePresetId,
            HelperMode = helperMode,
            Presets = presets,
        };
    }

    private static Dictionary<string, JsonElement> ExtractProperties(JsonElement source)
    {
        var output = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source.EnumerateObject())
        {
            output[property.Name] = property.Value.Clone();
        }

        return output;
    }
}
