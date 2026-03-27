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
    public string VillageName { get; set; } = "River Town";
    public int TotalPopulation { get; private set; }
    public float VillageMorale { get; set; } = 62f;
    public float FoodStockpile { get; set; } = 80f;
    public float SharedStockpile { get; set; } = 45f;
    public bool PerformanceModeActive { get; private set; }
    public bool ForceScriptedFallback { get; private set; }
    public float ScriptedRatio { get; private set; } = 1f;
    public float SparkRatio { get; private set; }
    public float EffectiveSparkMultiplier { get; private set; } = 1f;
    public string PerformanceReason { get; private set; } = "monitoring_off";

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

        var scriptedBehavior = root["scripted_behavior"] as JsonObject;
        if (scriptedBehavior is not null)
        {
            state.PerformanceModeActive = scriptedBehavior["performance_mode_active"]?.GetValue<bool>() ?? false;
            state.ForceScriptedFallback = scriptedBehavior["force_scripted_fallback"]?.GetValue<bool>() ?? false;
            state.ScriptedRatio = Math.Clamp(ReadSingle(scriptedBehavior["monitored_scripted_ratio"], 1f), 0f, 1f);
            state.SparkRatio = Math.Clamp(ReadSingle(scriptedBehavior["monitored_spark_ratio"], 0f), 0f, 1f);
            state.EffectiveSparkMultiplier = Math.Clamp(ReadSingle(scriptedBehavior["effective_spark_chance_multiplier"], 1f), 0f, 1f);
            state.PerformanceReason = scriptedBehavior["performance_reason"]?.GetValue<string>() ?? "monitoring_off";
        }

        var settlement = root["settlement"] as JsonObject;
        state.VillageName = settlement?["village_name"]?.GetValue<string>() ?? "River Town";
        state.TotalPopulation = ReadInt32(settlement?["total_population"], 0);
        state.VillageMorale = Math.Clamp(ReadSingle(settlement?["morale"], 62f), 0f, 100f);
        if (settlement?["shared_resources"] is JsonObject resources)
        {
            state.FoodStockpile = Math.Max(0f, ReadSingle(resources["food"], 80f));
            state.SharedStockpile = Math.Max(0f, ReadSingle(resources["stockpile"], 45f));
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

        var settlement = root["settlement"] as JsonObject ?? new JsonObject();
        root["settlement"] = settlement;
        settlement["village_name"] = string.IsNullOrWhiteSpace(VillageName) ? "River Town" : VillageName.Trim();
        settlement["total_population"] = Math.Max(0, ReadInt32(settlement["total_population"], Math.Max(0, TotalPopulation)));
        settlement["morale"] = Math.Clamp(VillageMorale, 0f, 100f);
        settlement["tick_interval_seconds"] = Math.Clamp(ReadSingle(settlement["tick_interval_seconds"], 6f), 1f, 60f);
        settlement["accumulated_tick_seconds"] = Math.Max(0f, ReadSingle(settlement["accumulated_tick_seconds"], 0f));
        var resources = settlement["shared_resources"] as JsonObject ?? new JsonObject();
        settlement["shared_resources"] = resources;
        resources["food"] = Math.Max(0f, FoodStockpile);
        resources["stockpile"] = Math.Max(0f, SharedStockpile);
        resources["wood"] = Math.Max(0f, ReadSingle(resources["wood"], 20f));
        resources["stone"] = Math.Max(0f, ReadSingle(resources["stone"], 16f));
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
