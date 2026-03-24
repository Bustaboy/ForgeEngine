using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed class DayNightPanelState
{
    public float DayCycleSpeed { get; set; } = 0.01f;
    public float DayProgress { get; set; } = 0.25f;
    public int DayCount { get; set; } = 1;

    public static DayNightPanelState FromScene(JsonObject root)
    {
        return new DayNightPanelState
        {
            DayCycleSpeed = ReadSingle(root["day_cycle_speed"], 0.01f),
            DayProgress = Math.Clamp(ReadSingle(root["day_progress"], 0.25f), 0f, 1f),
            DayCount = Math.Max(1, ReadInt(root["day_count"], 1)),
        };
    }

    public void ApplyToScene(JsonObject root)
    {
        root["day_cycle_speed"] = Math.Max(0f, DayCycleSpeed);
        root["day_progress"] = Math.Clamp(DayProgress, 0f, 1f);
        root["day_count"] = Math.Max(1, DayCount);
    }

    private static float ReadSingle(JsonNode? value, float fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<float>(out var floatValue))
            {
                return floatValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return (float)doubleValue;
            }
        }

        return fallback;
    }

    private static int ReadInt(JsonNode? value, int fallback)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return fallback;
    }
}
