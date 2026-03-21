using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public sealed record DockedPanel(string PanelId, string DisplayName, string DockZone, int Order);

public sealed record EditorLayout(IReadOnlyList<DockedPanel> Panels)
{
    public static EditorLayout CreateDefault() => new(
    [
        new DockedPanel("hierarchy", "Hierarchy", "left", 0),
        new DockedPanel("viewport", "Viewport", "center", 0),
        new DockedPanel("inspector", "Inspector", "right", 0),
        new DockedPanel("chat", "AI Copilot Chat", "bottom", 0),
    ]);
}

public sealed record SceneObject
{
    public required string ObjectId { get; init; }

    public required string DisplayName { get; init; }

    public required string ObjectType { get; init; }

    public Dictionary<string, JsonElement> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record InspectorView
{
    public required string ObjectId { get; init; }

    public required string ObjectLabel { get; init; }

    public required IReadOnlyDictionary<string, string> SimpleSection { get; init; }

    public required IReadOnlyDictionary<string, string> AdvancedSection { get; init; }
}

public sealed record AiSelectionContext
{
    public required string ObjectId { get; init; }

    public required string ObjectLabel { get; init; }

    public required string ObjectType { get; init; }

    public required IReadOnlyDictionary<string, string> Properties { get; init; }
}

public sealed record EditorProjectSnapshot
{
    public required string ProjectName { get; init; }

    public required string Scope { get; init; }

    public required string Rendering { get; init; }

    public required IReadOnlyList<string> Platforms { get; init; }

    public required IReadOnlyList<SceneObject> SceneObjects { get; init; }
}
