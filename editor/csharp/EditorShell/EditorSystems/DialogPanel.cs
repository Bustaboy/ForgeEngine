using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record DialogNpcRow(ulong EntityId, string Label, string StartNodeId, int NodeCount);

public sealed class DialogPanelState
{
    public IReadOnlyList<DialogNpcRow> Npcs { get; private set; } = [];

    public static DialogPanelState FromScene(JsonObject root)
    {
        var rows = new List<DialogNpcRow>();
        if (root["entities"] is not JsonArray entities)
        {
            return new DialogPanelState { Npcs = rows };
        }

        foreach (var entity in entities.OfType<JsonObject>())
        {
            if (entity["dialog"] is not JsonObject dialog)
            {
                continue;
            }

            var id = ReadUlong(entity["id"], 0);
            var name = entity["name"]?.GetValue<string>() ?? $"NPC {id}";
            var nodes = dialog["nodes"] as JsonArray;
            rows.Add(new DialogNpcRow(
                id,
                name,
                dialog["start_node_id"]?.GetValue<string>() ?? string.Empty,
                nodes?.Count ?? 0));
        }

        return new DialogPanelState { Npcs = rows };
    }

    public static bool TrySetDialogForEntity(JsonObject root, ulong entityId, JsonObject dialogPayload)
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

            entity["dialog"] = dialogPayload;
            return true;
        }

        return false;
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
