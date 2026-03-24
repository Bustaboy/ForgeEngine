using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record BuildableEntityRow(ulong EntityId, string Label, string Type, int GridX, int GridY);

public sealed class BuildingPanelState
{
    public IReadOnlyList<BuildableEntityRow> Buildables { get; private set; } = [];

    public static BuildingPanelState FromScene(JsonObject root)
    {
        var rows = new List<BuildableEntityRow>();
        if (root["entities"] is not JsonArray entities)
        {
            return new BuildingPanelState { Buildables = rows };
        }

        foreach (var entity in entities.OfType<JsonObject>())
        {
            if (entity["buildable"] is not JsonObject buildable)
            {
                continue;
            }

            var type = buildable["type"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var id = ReadUlong(entity["id"], 0);
            var name = entity["name"]?.GetValue<string>();
            var grid = buildable["grid_size"] as JsonObject;
            rows.Add(new BuildableEntityRow(
                id,
                string.IsNullOrWhiteSpace(name) ? $"Entity {id}" : name!,
                type,
                Math.Max(1, ReadInt(grid?["x"], 1)),
                Math.Max(1, ReadInt(grid?["y"], 1))));
        }

        return new BuildingPanelState { Buildables = rows };
    }

    public static bool TryApplyBuildableEdit(JsonObject root, ulong entityId, string type, int gridX, int gridY)
    {
        if (root["entities"] is not JsonArray entities)
        {
            return false;
        }

        foreach (var entity in entities.OfType<JsonObject>())
        {
            if (ReadUlong(entity["id"], 0) != entityId)
            {
                continue;
            }

            var buildable = entity["buildable"] as JsonObject ?? new JsonObject();
            entity["buildable"] = buildable;
            buildable["type"] = type;
            buildable["grid_size"] = new JsonObject
            {
                ["x"] = Math.Max(1, gridX),
                ["y"] = Math.Max(1, gridY),
            };
            return true;
        }

        return false;
    }

    private static int ReadInt(JsonNode? value, int fallback)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return fallback;
    }

    private static ulong ReadUlong(JsonNode? value, ulong fallback)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<ulong>(out var ulongValue))
            {
                return ulongValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue) && intValue > 0)
            {
                return (ulong)intValue;
            }
        }

        return fallback;
    }
}
