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

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Generated project manifest missing.", manifestPath);
        }

        if (!File.Exists(scenePath))
        {
            throw new FileNotFoundException("Generated scene scaffold missing.", scenePath);
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifestDoc = await JsonDocument.ParseAsync(manifestStream);

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
        var sceneDoc = await JsonDocument.ParseAsync(sceneStream);
        var sceneRoot = sceneDoc.RootElement;

        var sceneObjects = BuildSceneObjects(sceneRoot);

        return new EditorProjectSnapshot
        {
            ProjectName = projectName,
            Scope = scope,
            Rendering = rendering,
            Platforms = platforms,
            SceneObjects = sceneObjects,
        };
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
