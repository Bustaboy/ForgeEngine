using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public sealed class EditorWorkspace
{
    private readonly Dictionary<string, SceneObject> _objectIndex;

    public EditorWorkspace(EditorProjectSnapshot project)
    {
        Project = project;
        Layout = EditorLayout.CreateDefault();
        _objectIndex = project.SceneObjects.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
    }

    public EditorProjectSnapshot Project { get; }

    public EditorLayout Layout { get; }

    public SceneObject? SelectedObject { get; private set; }

    public InspectorView? Inspector { get; private set; }

    public AiSelectionContext? AiContext { get; private set; }

    public bool SelectObject(string objectId)
    {
        if (!_objectIndex.TryGetValue(objectId, out var sceneObject))
        {
            return false;
        }

        SelectedObject = sceneObject;
        Inspector = BuildInspector(sceneObject);
        AiContext = BuildAiContext(sceneObject);
        return true;
    }

    private static InspectorView BuildInspector(SceneObject sceneObject)
    {
        var simple = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = sceneObject.DisplayName,
            ["Type"] = sceneObject.ObjectType,
        };

        foreach (var property in sceneObject.Properties)
        {
            if (property.Key is "x" or "y" or "z" or "mode")
            {
                simple[property.Key] = ToDisplayText(property.Value);
            }
        }

        var advanced = sceneObject.Properties.ToDictionary(
            property => property.Key,
            property => ToDisplayText(property.Value),
            StringComparer.OrdinalIgnoreCase);

        return new InspectorView
        {
            ObjectId = sceneObject.ObjectId,
            ObjectLabel = sceneObject.DisplayName,
            SimpleSection = simple,
            AdvancedSection = advanced,
        };
    }

    private static AiSelectionContext BuildAiContext(SceneObject sceneObject)
    {
        var properties = sceneObject.Properties.ToDictionary(
            property => property.Key,
            property => ToDisplayText(property.Value),
            StringComparer.OrdinalIgnoreCase);

        return new AiSelectionContext
        {
            ObjectId = sceneObject.ObjectId,
            ObjectLabel = sceneObject.DisplayName,
            ObjectType = sceneObject.ObjectType,
            Properties = properties,
        };
    }

    private static string ToDisplayText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
        _ => string.Empty,
    };
}
