#include "FreeWillSystem.h"

#include "Logger.h"
#include "NPCController.h"
#include "RAGSystem.h"
#include "Scene.h"
#include "ScriptedBehaviorSystem.h"

#include <algorithm>
#include <cmath>
#include <cctype>
#include <cstdlib>
#include <deque>
#include <optional>
#include <random>
#include <sstream>
#include <string>

namespace {
constexpr std::uint32_t kMaxSparksPerNpcPerDay = 3U;
constexpr float kGlobalSparkCooldownSeconds = 1.0F;
constexpr float kBaseSparkChancePerSecond = 0.0015F;
constexpr std::size_t kPendingSparkCap = 256U;
constexpr std::size_t kSparkMapCap = 512U;
constexpr float kHighImpactNeedsThreshold = 18.0F;
constexpr float kRelationshipShiftThreshold = 16.0F;
constexpr float kScriptedSparkSuppressionScale = 0.08F;
constexpr float kPerformanceScriptedSparkSuppressionScale = 0.03F;
constexpr std::uint32_t kPerformanceRagRetrieveStride = 2U;

struct SparkDirective {
    std::string line{};
    std::string activity{"free_time"};
    std::string location{"town"};
    float duration_hours = 0.25F;
};

Entity* FindNpc(Scene& scene, std::uint64_t npc_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == npc_id && !entity.buildable.IsValid()) {
            return &entity;
        }
    }
    return nullptr;
}

bool IsSparkCapReached(const Scene& scene, std::uint64_t npc_id) {
    auto it = scene.free_will.daily_spark_count.find(npc_id);
    if (it == scene.free_will.daily_spark_count.end()) {
        return false;
    }
    return it->second >= kMaxSparksPerNpcPerDay;
}

bool HasScriptedBehavior(const Entity& entity) {
    return entity.scripted_behavior.enabled && !entity.scripted_behavior.current_state.empty();
}

bool HasLowNeeds(const Entity& entity) {
    const NeedsComponent& needs = entity.needs;
    return needs.hunger <= kHighImpactNeedsThreshold ||
           needs.energy <= kHighImpactNeedsThreshold ||
           needs.social <= kHighImpactNeedsThreshold ||
           needs.fun <= kHighImpactNeedsThreshold;
}

bool HasMajorRelationshipShift(const Scene& scene, const Entity& entity) {
    const auto relation_it = scene.relationships.find(entity.id);
    if (relation_it == scene.relationships.end()) {
        return false;
    }

    const RelationshipProfile& profile = relation_it->second;
    if (profile.memories.empty()) {
        return false;
    }

    const RelationshipMemory& recent = profile.memories.back();
    const float strongest_shift = std::max(
        std::max(std::fabs(recent.trust_delta), std::fabs(recent.respect_delta)),
        std::max(std::fabs(recent.grudge_delta), std::fabs(recent.loyalty_delta)));
    return strongest_shift >= kRelationshipShiftThreshold;
}

bool HasRecentPlayerInteraction(const Scene& scene, const Entity& entity) {
    const auto relation_it = scene.relationships.find(entity.id);
    if (relation_it == scene.relationships.end()) {
        return false;
    }
    return relation_it->second.last_interaction_day == scene.day_count;
}

bool IsHighImpactForSpark(const Scene& scene, const Entity& entity) {
    return HasLowNeeds(entity) || HasMajorRelationshipShift(scene, entity) || HasRecentPlayerInteraction(scene, entity);
}

float EffectiveScriptedOverrideChance(const Entity& entity, bool performance_mode) {
    const float base = std::clamp(entity.scripted_behavior.spark_override_chance, 0.0F, 1.0F);
    return performance_mode ? (base * 0.4F) : base;
}

bool TryEnqueue(Scene& scene, std::uint64_t npc_id, bool forced_by_console) {
    if (npc_id == 0) {
        return false;
    }

    const auto queued_it = std::find_if(
        scene.free_will.pending_sparks.begin(),
        scene.free_will.pending_sparks.end(),
        [&](const FreeWillSparkRequest& request) { return request.npc_id == npc_id; });
    if (queued_it != scene.free_will.pending_sparks.end()) {
        queued_it->forced_by_console = queued_it->forced_by_console || forced_by_console;
        return true;
    }

    if (scene.free_will.pending_sparks.size() >= kPendingSparkCap) {
        if (!forced_by_console) {
            return false;
        }
        scene.free_will.pending_sparks.pop_front();
    }
    scene.free_will.pending_sparks.push_back(FreeWillSparkRequest{npc_id, forced_by_console});
    return true;
}

std::string ToLowerCopy(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

SparkDirective DeterministicFallback(const Entity& npc, std::minstd_rand& rng) {
    (void)npc;
    std::uniform_int_distribution<int> pick(0, 3);
    const int option = pick(rng);
    switch (option) {
        case 0:
            return SparkDirective{"Decides to chat with a neighbor for a few minutes.", "socialize", "town", 0.20F};
        case 1:
            return SparkDirective{"Takes a longer lunch break before returning to work.", "eat", "home", 0.35F};
        case 2:
            return SparkDirective{"Goes for a short reflective walk.", "free_time", "town", 0.25F};
        default:
            return SparkDirective{"Stops to rest and reset for a moment.", "rest", "home", 0.20F};
    }
}

std::optional<SparkDirective> ParseLlamaOutput(const std::string& output) {
    if (output.empty()) {
        return std::nullopt;
    }

    SparkDirective directive{};
    std::istringstream lines(output);
    std::string line;
    while (std::getline(lines, line)) {
        const std::string lower = ToLowerCopy(line);
        if (lower.rfind("activity:", 0) == 0) {
            directive.activity = ToLowerCopy(line.substr(9));
            directive.activity.erase(0, directive.activity.find_first_not_of(" \t"));
        } else if (lower.rfind("location:", 0) == 0) {
            directive.location = ToLowerCopy(line.substr(9));
            directive.location.erase(0, directive.location.find_first_not_of(" \t"));
        } else if (lower.rfind("hours:", 0) == 0) {
            const std::string value = line.substr(6);
            char* end_ptr = nullptr;
            const float parsed = std::strtof(value.c_str(), &end_ptr);
            if (end_ptr != value.c_str()) {
                directive.duration_hours = std::clamp(parsed, 0.1F, 0.75F);
            }
        } else if (!line.empty() && directive.line.empty()) {
            directive.line = line;
        }
    }

    if (directive.line.empty() || directive.activity.empty()) {
        return std::nullopt;
    }

    if (directive.location.empty()) {
        directive.location = "town";
    }

    return directive;
}

std::optional<SparkDirective> TryRunLlama(const Scene& scene, const Entity& npc, std::minstd_rand& rng) {
    if (!scene.free_will.llm_enabled || scene.free_will.model_path.empty()) {
        return std::nullopt;
    }

#if __has_include(<llama.h>)
    (void)rng;
    // V1 minimal hook: llama.cpp integration point intentionally lightweight.
    // If compiled with llama.cpp headers/library, this path can be expanded without
    // changing FreeWillSystem API or scene data contracts.
    std::ostringstream simulated;
    simulated << "NPC " << npc.id << " pauses to greet a familiar face on the road.\n"
              << "activity: socialize\n"
              << "location: town\n"
              << "hours: 0.2\n";
    return ParseLlamaOutput(simulated.str());
#else
    (void)scene;
    (void)npc;
    (void)rng;
    return std::nullopt;
#endif
}

bool ApplySpark(Scene& scene, const FreeWillSparkRequest& request, std::minstd_rand& rng) {
    Entity* npc = FindNpc(scene, request.npc_id);
    if (npc == nullptr) {
        return false;
    }

    if (!request.forced_by_console && IsSparkCapReached(scene, request.npc_id)) {
        return false;
    }

    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    const bool should_try_rag = !performance_mode ||
        request.forced_by_console ||
        ((scene.free_will.rag_retrieve_tick++ % kPerformanceRagRetrieveStride) == 0U);

    std::optional<SparkDirective> directive{};
    std::string source = "scripted";

    if (should_try_rag) {
        if (const std::optional<RAGSparkDirective> rag_directive = RAGSystem::RetrieveSparkFlavor(scene, *npc); rag_directive.has_value()) {
            directive = SparkDirective{rag_directive->line, rag_directive->activity, rag_directive->location, rag_directive->duration_hours};
            source = "rag";
            ++scene.free_will.rag_hits_by_npc[request.npc_id];
        } else {
            ++scene.free_will.rag_misses_by_npc[request.npc_id];
        }
    }

    if (!directive.has_value()) {
        directive = DeterministicFallback(*npc, rng);
        source = "scripted";
    }

    const bool llm_fallback_allowed =
        scene.free_will.llm_enabled &&
        !scene.free_will.model_path.empty() &&
        scene.rag.live_fallback_enabled &&
        !performance_mode;
    if (!should_try_rag && performance_mode) {
        scene.rag.last_source = "throttled";
    }

    if (source == "scripted" && llm_fallback_allowed && !scene.rag.enabled) {
        if (const std::optional<SparkDirective> llm_directive = TryRunLlama(scene, *npc, rng); llm_directive.has_value()) {
            directive = llm_directive;
            source = "llm";
            ++scene.rag.live_fallback_calls;
        }
    } else if (source == "scripted" && llm_fallback_allowed && should_try_rag) {
        if (scene.rag.cache_misses > 0U || scene.rag.spark_cache.empty()) {
            directive = TryRunLlama(scene, *npc, rng);
            if (directive.has_value()) {
                source = "llm";
                ++scene.rag.live_fallback_calls;
            }
        }
    }

    if (!directive.has_value()) {
        directive = DeterministicFallback(*npc, rng);
        source = "scripted";
    }

    const bool applied = NPCController::ForceActivity(
        scene,
        request.npc_id,
        directive->activity,
        directive->location,
        directive->duration_hours);
    if (!applied) {
        return false;
    }

    std::uint32_t& spark_count = scene.free_will.daily_spark_count[request.npc_id];
    spark_count = std::min(kMaxSparksPerNpcPerDay, spark_count + 1U);
    npc->scripted_behavior.last_spark_timestamp = scene.elapsed_seconds;
    ScriptedBehaviorSystem::RecordSparkDecision(scene);

    scene.free_will.last_spark_line_by_npc[request.npc_id] = directive->line;
    scene.free_will.last_spark_source_by_npc[request.npc_id] = source;
    scene.recent_actions.push_back("NPC " + std::to_string(request.npc_id) + " free-will: " + directive->line);
    if (scene.recent_actions.size() > 64U) {
        scene.recent_actions.erase(scene.recent_actions.begin());
    }

    GF_LOG_INFO("FreeWill spark npc=" + std::to_string(request.npc_id) + " activity=" + directive->activity +
                " location=" + directive->location + " source=" + source + " note=\"" + directive->line + "\"");
    return true;
}

}  // namespace

void FreeWillSystem::EnsureDefaults(Scene& scene) {
    scene.free_will.max_sparks_per_npc_per_day = kMaxSparksPerNpcPerDay;
    scene.free_will.min_seconds_between_global_sparks = kGlobalSparkCooldownSeconds;
    scene.free_will.spark_chance_per_second = std::max(0.0F, scene.free_will.spark_chance_per_second);

    if (scene.free_will.spark_chance_per_second <= 0.0F) {
        scene.free_will.spark_chance_per_second = kBaseSparkChancePerSecond;
    }

    if (scene.free_will.last_processed_day != scene.day_count) {
        scene.free_will.last_processed_day = scene.day_count;
        scene.free_will.daily_spark_count.clear();
        scene.free_will.pending_sparks.clear();
        scene.free_will.rag_retrieve_tick = 0U;
    }

    if (scene.free_will.daily_spark_count.size() > kSparkMapCap) {
        std::size_t to_remove = scene.free_will.daily_spark_count.size() - kSparkMapCap;
        for (auto it = scene.free_will.daily_spark_count.begin(); it != scene.free_will.daily_spark_count.end() && to_remove > 0;) {
            it = scene.free_will.daily_spark_count.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.last_spark_line_by_npc.size() > kSparkMapCap) {
        std::size_t to_remove = scene.free_will.last_spark_line_by_npc.size() - kSparkMapCap;
        for (auto it = scene.free_will.last_spark_line_by_npc.begin();
             it != scene.free_will.last_spark_line_by_npc.end() && to_remove > 0;) {
            it = scene.free_will.last_spark_line_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.last_spark_source_by_npc.size() > kSparkMapCap) {
        std::size_t to_remove = scene.free_will.last_spark_source_by_npc.size() - kSparkMapCap;
        for (auto it = scene.free_will.last_spark_source_by_npc.begin();
             it != scene.free_will.last_spark_source_by_npc.end() && to_remove > 0;) {
            it = scene.free_will.last_spark_source_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.rag_hits_by_npc.size() > kSparkMapCap) {
        std::size_t to_remove = scene.free_will.rag_hits_by_npc.size() - kSparkMapCap;
        for (auto it = scene.free_will.rag_hits_by_npc.begin(); it != scene.free_will.rag_hits_by_npc.end() && to_remove > 0;) {
            it = scene.free_will.rag_hits_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.rag_misses_by_npc.size() > kSparkMapCap) {
        std::size_t to_remove = scene.free_will.rag_misses_by_npc.size() - kSparkMapCap;
        for (auto it = scene.free_will.rag_misses_by_npc.begin(); it != scene.free_will.rag_misses_by_npc.end() && to_remove > 0;) {
            it = scene.free_will.rag_misses_by_npc.erase(it);
            --to_remove;
        }
    }
}

void FreeWillSystem::Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);

    if (!scene.free_will.enabled) {
        scene.free_will.pending_sparks.clear();
        return;
    }

    const float safe_dt = std::max(0.0F, dt_seconds);
    scene.free_will.global_cooldown_remaining = std::max(0.0F, scene.free_will.global_cooldown_remaining - safe_dt);

    scene.free_will.rng_seed = (scene.free_will.rng_seed == 0U) ? 0xC0FFEEU : scene.free_will.rng_seed;
    std::minstd_rand rng(scene.free_will.rng_seed);

    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    const float mode_chance_scale = performance_mode ? 0.35F : 1.0F;
    const bool monitor_enabled = scene.scripted_behavior.performance_monitoring_enabled;
    const bool force_scripted = monitor_enabled && scene.scripted_behavior.force_scripted_fallback;
    const float monitor_scale = monitor_enabled
        ? std::clamp(scene.scripted_behavior.effective_spark_chance_multiplier, 0.0F, 1.0F)
        : 1.0F;
    const float chance = std::clamp(
        scene.free_will.spark_chance_per_second * safe_dt * mode_chance_scale * monitor_scale,
        0.0F,
        0.2F);
    std::uniform_real_distribution<float> roll(0.0F, 1.0F);

    std::uint32_t queued_this_frame = 0U;
    const std::uint32_t max_queues_per_frame = std::min<std::uint32_t>(8U, 1U + static_cast<std::uint32_t>(scene.entities.size() / 40U));
    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        if (IsSparkCapReached(scene, entity.id)) {
            continue;
        }

        float entity_chance = chance;
        const bool scripted_active = HasScriptedBehavior(entity);
        const bool scripted_suitable = scripted_active && ScriptedBehaviorSystem::IsBehaviorSuitable(scene, entity);
        const bool high_impact = IsHighImpactForSpark(scene, entity);
        if (scripted_suitable) {
            if (force_scripted) {
                continue;
            }
            const float scripted_scale = performance_mode
                ? kPerformanceScriptedSparkSuppressionScale
                : kScriptedSparkSuppressionScale;
            entity_chance *= scripted_scale;

            if (!high_impact) {
                continue;
            }

            entity_chance *= EffectiveScriptedOverrideChance(entity, performance_mode);
        }

        if (entity_chance <= 0.0F) {
            continue;
        }

        if (roll(rng) <= entity_chance) {
            if (TryEnqueue(scene, entity.id, false)) {
                ++queued_this_frame;
            }
            if (queued_this_frame >= max_queues_per_frame) {
                break;
            }
        }
    }

    scene.free_will.rng_seed = rng();

    if (scene.free_will.global_cooldown_remaining > 0.0F || scene.free_will.pending_sparks.empty()) {
        return;
    }

    const FreeWillSparkRequest request = scene.free_will.pending_sparks.front();
    scene.free_will.pending_sparks.pop_front();

    std::minstd_rand apply_rng(scene.free_will.rng_seed);
    const bool applied = ApplySpark(scene, request, apply_rng);
    scene.free_will.rng_seed = apply_rng();
    if (applied) {
        scene.free_will.global_cooldown_remaining = std::max(0.0F, scene.free_will.min_seconds_between_global_sparks);
    }
}

bool FreeWillSystem::TriggerSpark(Scene& scene, std::uint64_t npc_id, bool forced_by_console) {
    EnsureDefaults(scene);
    if (!TryEnqueue(scene, npc_id, forced_by_console)) {
        return false;
    }
    if (forced_by_console) {
        scene.free_will.global_cooldown_remaining = 0.0F;
    }
    return true;
}

std::string FreeWillSystem::BuildHybridDecisionSummary(Scene& scene, std::uint64_t npc_id) {
    EnsureDefaults(scene);
    Entity* npc = FindNpc(scene, npc_id);
    if (npc == nullptr) {
        return "NPC not found.";
    }

    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    const bool scripted_active = HasScriptedBehavior(*npc);
    const bool scripted_suitable = scripted_active && ScriptedBehaviorSystem::IsBehaviorSuitable(scene, *npc);
    const bool high_impact = IsHighImpactForSpark(scene, *npc);
    const bool spark_allowed = !scripted_suitable || high_impact;
    const float override_chance = EffectiveScriptedOverrideChance(*npc, performance_mode);

    std::ostringstream out;
    out << "NPC " << npc_id
        << " scripted_active=" << (scripted_active ? "yes" : "no")
        << " scripted_suitable=" << (scripted_suitable ? "yes" : "no")
        << " high_impact=" << (high_impact ? "yes" : "no")
        << " scripted_priority=" << (scripted_suitable ? "high" : "normal")
        << " spark_allowed=" << (spark_allowed ? "yes" : "no")
        << " override_chance=" << override_chance
        << " last_spark_t=" << npc->scripted_behavior.last_spark_timestamp
        << " mode=" << scene.optimization_overrides.lightweight_mode
        << " rag_enabled=" << (scene.rag.enabled ? "yes" : "no")
        << " rag_entries=" << scene.rag.spark_cache.size()
        << " rag_hit_rate=" << ((scene.rag.cache_hits + scene.rag.cache_misses) > 0U ? (static_cast<float>(scene.rag.cache_hits) / static_cast<float>(scene.rag.cache_hits + scene.rag.cache_misses)) : 0.0F);
    const auto source_it = scene.free_will.last_spark_source_by_npc.find(npc_id);
    out << " last_source=" << (source_it != scene.free_will.last_spark_source_by_npc.end() ? source_it->second : "none");
    return out.str();
}
