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
    public int RagCacheSize { get; private set; }
    public float RagHitRate { get; private set; }
    public float NarrativeFlavorHitRate { get; private set; }
    public string LastMsqAdaptationSource { get; private set; } = "none";
    public string LastNarrativeCheckpoint { get; private set; } = "none";
    public int GenerationalMemorySize { get; private set; }
    public float LegacyRecallHitRate { get; private set; }
    public string SparkSourcePreference { get; private set; } = "rag > scripted > llm";
    public bool LightweightPerformanceMode { get; private set; }
    public string LastNpcDaySummary { get; private set; } = "No NPC day orchestration summary yet.";
    public string LastNarrativeCheckpointStatus { get; private set; } = "No narrative checkpoint orchestration status yet.";
    private Dictionary<string, string> NpcDaySummaryByNpc { get; } = [];
    private Dictionary<ulong, string> LastSparkSourceByNpc { get; } = [];
    private Dictionary<ulong, int> RagHitsByNpc { get; } = [];
    private Dictionary<ulong, int> RagMissesByNpc { get; } = [];

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
        if (freeWill["last_spark_source_by_npc"] is JsonObject lastSparkSources)
        {
            foreach (var entry in lastSparkSources)
            {
                if (!ulong.TryParse(entry.Key, out var npcId))
                {
                    continue;
                }

                var source = entry.Value?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    state.LastSparkSourceByNpc[npcId] = source;
                }
            }
        }
        if (freeWill["rag_hits_by_npc"] is JsonObject ragHitsByNpc)
        {
            foreach (var entry in ragHitsByNpc)
            {
                if (ulong.TryParse(entry.Key, out var npcId))
                {
                    state.RagHitsByNpc[npcId] = Math.Max(0, ReadInt32(entry.Value, 0));
                }
            }
        }
        if (freeWill["rag_misses_by_npc"] is JsonObject ragMissesByNpc)
        {
            foreach (var entry in ragMissesByNpc)
            {
                if (ulong.TryParse(entry.Key, out var npcId))
                {
                    state.RagMissesByNpc[npcId] = Math.Max(0, ReadInt32(entry.Value, 0));
                }
            }
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

        var rag = root["rag"] as JsonObject;
        if (rag is not null)
        {
            state.RagCacheSize = rag["spark_cache"] is JsonArray cache ? Math.Max(0, cache.Count) : 0;
            var hits = Math.Max(0, ReadInt32(rag["cache_hits"], 0));
            var misses = Math.Max(0, ReadInt32(rag["cache_misses"], 0));
            state.RagHitRate = hits + misses > 0 ? (float)hits / (hits + misses) : 0f;
            var narrativeHits = Math.Max(0, ReadInt32(rag["narrative_hits"], 0));
            var narrativeMisses = Math.Max(0, ReadInt32(rag["narrative_misses"], 0));
            state.NarrativeFlavorHitRate = narrativeHits + narrativeMisses > 0 ? (float)narrativeHits / (narrativeHits + narrativeMisses) : 0f;
            state.LastMsqAdaptationSource = rag["last_narrative_source"]?.GetValue<string>() ?? "none";
            state.LastNarrativeCheckpoint = rag["last_narrative_checkpoint"]?.GetValue<string>() ?? "none";
            var legacyHits = Math.Max(0, ReadInt32(rag["legacy_hits"], 0));
            var legacyMisses = Math.Max(0, ReadInt32(rag["legacy_misses"], 0));
            state.LegacyRecallHitRate = legacyHits + legacyMisses > 0 ? (float)legacyHits / (legacyHits + legacyMisses) : 0f;
        }
        if (freeWill["orchestration"] is JsonObject freeWillOrchestration)
        {
            state.LastNpcDaySummary = freeWillOrchestration["day_plan_latest_summary"]?.GetValue<string>() ?? state.LastNpcDaySummary;
            if (freeWillOrchestration["day_plan_by_npc"] is JsonObject dayPlanByNpc)
            {
                foreach (var entry in dayPlanByNpc)
                {
                    if (entry.Value is not JsonObject npcPlan)
                    {
                        continue;
                    }

                    var summary = npcPlan["summary"]?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        state.NpcDaySummaryByNpc[entry.Key] = summary;
                    }
                }
            }
        }
        var narrativeOrchestration = root["narrative_orchestration"] as JsonObject;
        if (narrativeOrchestration is not null)
        {
            var checkpoint = narrativeOrchestration["last_checkpoint"]?.GetValue<string>() ?? "none";
            var status = narrativeOrchestration["status"]?.GetValue<string>() ?? "unknown";
            var source = narrativeOrchestration["source"]?.GetValue<string>() ?? "none";
            state.LastNarrativeCheckpointStatus = $"checkpoint={checkpoint} | status={status} | source={source}";
        }
        state.GenerationalMemorySize = root["compressed_event_log"] is JsonArray legacyLog ? Math.Max(0, legacyLog.Count) : 0;
        if (root["optimization_overrides"] is JsonObject optimization
            && string.Equals(optimization["lightweight_mode"]?.GetValue<string>(), "performance", StringComparison.OrdinalIgnoreCase))
        {
            state.LightweightPerformanceMode = true;
            state.SparkSourcePreference = "rag > scripted (llm disabled in performance mode)";
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

    public string SparkSourceForNpc(ulong npcId)
    {
        if (npcId == 0)
        {
            return "none";
        }

        return LastSparkSourceByNpc.TryGetValue(npcId, out var source) ? source : "none";
    }

    public float RagHitRateForNpc(ulong npcId)
    {
        if (npcId == 0)
        {
            return 0f;
        }

        var hits = RagHitsByNpc.TryGetValue(npcId, out var npcHits) ? Math.Max(0, npcHits) : 0;
        var misses = RagMissesByNpc.TryGetValue(npcId, out var npcMisses) ? Math.Max(0, npcMisses) : 0;
        return hits + misses > 0 ? (float)hits / (hits + misses) : 0f;
    }

    public string NpcDaySummaryForNpc(ulong npcId)
    {
        if (npcId == 0)
        {
            return LastNpcDaySummary;
        }

        var key = npcId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return NpcDaySummaryByNpc.TryGetValue(key, out var summary) && !string.IsNullOrWhiteSpace(summary)
            ? summary
            : LastNpcDaySummary;
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

        var rag = root["rag"] as JsonObject ?? new JsonObject();
        root["rag"] = rag;
        rag["enabled"] = rag["enabled"]?.GetValue<bool>() ?? true;
        rag["live_fallback_enabled"] = rag["live_fallback_enabled"]?.GetValue<bool>() ?? true;
        rag["max_entries"] = Math.Clamp(ReadInt32(rag["max_entries"], 256), 16, 2048);
        rag["similarity_threshold"] = Math.Clamp(ReadSingle(rag["similarity_threshold"], 0.78f), 0.35f, 0.99f);
        rag["cache_hits"] = Math.Max(0, ReadInt32(rag["cache_hits"], 0));
        rag["cache_misses"] = Math.Max(0, ReadInt32(rag["cache_misses"], 0));
        rag["spark_cache"] ??= new JsonArray();

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
