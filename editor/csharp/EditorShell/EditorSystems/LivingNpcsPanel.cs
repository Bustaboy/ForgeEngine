using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed class LivingNpcsPanelState
{
    public bool FreeWillEnabled { get; set; } = true;
    public bool LlmEnabled { get; set; } = true;
    public float SparkChancePerSecond { get; set; } = 0.0015f;
    public int MaxSparksPerNpcPerDay { get; set; } = 3;
    public string ModelPath { get; set; } = "models/llm-q4-k.gguf";
    public int SparksToday { get; private set; }
    public IReadOnlyList<string> RecentSparks { get; private set; } = [];

    public static LivingNpcsPanelState FromScene(JsonObject root)
    {
        var freeWill = root["free_will"] as JsonObject;
        if (freeWill is null)
        {
            return new LivingNpcsPanelState();
        }

        var state = new LivingNpcsPanelState
        {
            FreeWillEnabled = freeWill["enabled"]?.GetValue<bool>() ?? true,
            LlmEnabled = freeWill["llm_enabled"]?.GetValue<bool>() ?? true,
            SparkChancePerSecond = Math.Max(0f, ReadSingle(freeWill["spark_chance_per_second"], 0.0015f)),
            MaxSparksPerNpcPerDay = Math.Max(1, ReadInt32(freeWill["max_sparks_per_npc_per_day"], 3)),
            ModelPath = freeWill["model_path"]?.GetValue<string>() ?? "models/llm-q4-k.gguf",
        };

        if (freeWill["daily_spark_count"] is JsonObject dailySparkCount)
        {
            state.SparksToday = dailySparkCount
                .Select(entry => ReadInt32(entry.Value, 0))
                .Sum();
        }

        if (freeWill["last_spark_line_by_npc"] is JsonObject lastSparkLines)
        {
            state.RecentSparks = lastSparkLines
                .Select(entry =>
                {
                    var npcId = entry.Key;
                    var sparkLine = entry.Value?.GetValue<string>() ?? string.Empty;
                    return $"NPC {npcId}: {sparkLine}";
                })
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(12)
                .ToArray();
        }

        return state;
    }

    public void ApplyToScene(JsonObject root)
    {
        var freeWill = root["free_will"] as JsonObject ?? new JsonObject();
        root["free_will"] = freeWill;
        freeWill["enabled"] = FreeWillEnabled;
        freeWill["llm_enabled"] = LlmEnabled;
        freeWill["spark_chance_per_second"] = Math.Max(0f, SparkChancePerSecond);
        freeWill["max_sparks_per_npc_per_day"] = Math.Max(1, MaxSparksPerNpcPerDay);
        freeWill["model_path"] = string.IsNullOrWhiteSpace(ModelPath) ? "models/llm-q4-k.gguf" : ModelPath.Trim();
        freeWill["last_processed_day"] = Math.Max(1, ReadInt32(freeWill["last_processed_day"], 1));
        freeWill["global_cooldown_remaining"] = Math.Max(0f, ReadSingle(freeWill["global_cooldown_remaining"], 0f));
        freeWill["rng_seed"] = Math.Max(0, ReadInt32(freeWill["rng_seed"], 0));
        freeWill["daily_spark_count"] ??= new JsonObject();
        freeWill["last_spark_line_by_npc"] ??= new JsonObject();
    }

    private static float ReadSingle(JsonNode? value, float fallback)
    {
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

    private static int ReadInt32(JsonNode? value, int fallback)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return (int)Math.Clamp(longValue, int.MinValue, int.MaxValue);
            }
        }

        return fallback;
    }
}
