#include "LivingNpcSystem.h"

#include "NPCController.h"
#include "RAGSystem.h"
#include "ScriptedBehaviorSystem.h"

#include <algorithm>
#include <cstdint>
#include <cmath>
#include <string>

namespace {
std::uint32_t LastSeasonBucket(const Scene& scene) {
    const float progress = std::fmod(std::max(0.0F, scene.day_progress), 1.0F);
    return static_cast<std::uint32_t>(progress * 4.0F);
}

bool HasRelationshipThresholdEvent(const Scene& scene) {
    for (const auto& [npc_id, profile] : scene.relationships) {
        (void)npc_id;
        if (profile.trust >= 65.0F || profile.grudge >= 55.0F || profile.loyalty >= 60.0F) {
            return true;
        }
    }
    return false;
}
}  // namespace

void LivingNpcSystem::EnsureDefaults(Scene& scene) {
    NPCController::EnsureDefaults(scene);
    ScriptedBehaviorSystem::EnsureDefaults(scene);
}

void LivingNpcSystem::Update(Scene& scene, float dt_seconds) {
    static std::uint32_t last_seen_day = 0U;
    static std::uint32_t last_seen_season_bucket = 0U;
    static bool first_update = true;

    if (first_update) {
        last_seen_day = scene.day_count;
        last_seen_season_bucket = LastSeasonBucket(scene);
        first_update = false;
    }

    NPCController::Update(scene, dt_seconds);
    ScriptedBehaviorSystem::Update(scene, dt_seconds);

    if (scene.day_count != last_seen_day) {
        last_seen_day = scene.day_count;
        RAGSystem::EvaluateNarrativeCheckpoint(scene, "workshop_milestone");
        (void)RAGSystem::RetrieveLegacyRecall(scene, "daily continuity generation " + std::to_string(scene.current_generation));
    }

    const std::uint32_t season_bucket = LastSeasonBucket(scene);
    if (season_bucket != last_seen_season_bucket) {
        last_seen_season_bucket = season_bucket;
        RAGSystem::EvaluateNarrativeCheckpoint(scene, "season_change");
    }

    if (HasRelationshipThresholdEvent(scene)) {
        RAGSystem::EvaluateNarrativeCheckpoint(scene, "relationship_threshold");
        (void)RAGSystem::RetrieveLegacyRecall(scene, "relationship legacy arc generation " + std::to_string(scene.current_generation));
    }

    if (!scene.recent_actions.empty()) {
        const std::string& last_action = scene.recent_actions.back();
        if (last_action.find("moral choice") != std::string::npos || last_action.find("moral_choice") != std::string::npos) {
            RAGSystem::EvaluateNarrativeCheckpoint(scene, "moral_choice");
        }
    }
}
