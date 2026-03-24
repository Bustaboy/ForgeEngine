#include "FreeWillSystem.h"

#include "Logger.h"
#include "NPCController.h"
#include "Scene.h"

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

    std::optional<SparkDirective> directive = TryRunLlama(scene, *npc, rng);
    if (!directive.has_value()) {
        directive = DeterministicFallback(*npc, rng);
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

    scene.free_will.last_spark_line_by_npc[request.npc_id] = directive->line;
    scene.recent_actions.push_back("NPC " + std::to_string(request.npc_id) + " free-will: " + directive->line);
    if (scene.recent_actions.size() > 64U) {
        scene.recent_actions.erase(scene.recent_actions.begin());
    }

    GF_LOG_INFO("FreeWill spark npc=" + std::to_string(request.npc_id) + " activity=" + directive->activity +
                " location=" + directive->location + " note=\"" + directive->line + "\"");
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

    const float chance = std::clamp(scene.free_will.spark_chance_per_second * safe_dt, 0.0F, 0.2F);
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
        if (roll(rng) <= chance) {
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
