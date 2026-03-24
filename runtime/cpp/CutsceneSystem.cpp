#include "CutsceneSystem.h"

#include "Logger.h"
#include "NarratorSystem.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>

namespace {
float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

glm::vec3 Lerp(const glm::vec3& start, const glm::vec3& end, float t) {
    return start + (end - start) * Clamp01(t);
}

bool HasPlayedBeatCutscene(const Scene& scene, const std::string& beat_id) {
    if (beat_id.empty()) {
        return false;
    }
    return std::find(scene.cutscene.played_cutscene_history.begin(), scene.cutscene.played_cutscene_history.end(), beat_id) !=
        scene.cutscene.played_cutscene_history.end();
}

Entity* FindEntityById(Scene& scene, std::uint64_t entity_id) {
    if (entity_id == 0) {
        return nullptr;
    }
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id) {
            return &entity;
        }
    }
    return nullptr;
}

std::uint64_t FindNearestNpcIdToPlayer(const Scene& scene) {
    std::uint64_t best_id = 0;
    float best_distance = 0.0F;
    bool has_best = false;

    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }
        const bool candidate_npc = !entity.dialog.nodes.empty() || !entity.faction.role.empty();
        if (!candidate_npc) {
            continue;
        }
        const glm::vec3 delta = entity.transform.pos - scene.player_proxy_position;
        const float distance = std::sqrt((delta.x * delta.x) + (delta.z * delta.z));
        if (!has_best || distance < best_distance) {
            has_best = true;
            best_distance = distance;
            best_id = entity.id;
        }
    }

    return best_id;
}
}  // namespace

namespace CutsceneSystem {

void EnsureDefaults(Scene& scene) {
    scene.cutscene.duration_seconds = std::max(2.0F, scene.cutscene.duration_seconds);
    scene.cutscene.elapsed_seconds = std::max(0.0F, scene.cutscene.elapsed_seconds);
}

bool TryStartForBeat(Scene& scene, const StoryBeat& beat, const std::string& source_event_id, const std::string& narrator_line_override) {
    EnsureDefaults(scene);
    if (!scene.cutscene.enabled || scene.cutscene.active || beat.id.empty() || HasPlayedBeatCutscene(scene, beat.id)) {
        return false;
    }

    scene.cutscene.active = true;
    scene.cutscene.elapsed_seconds = 0.0F;
    scene.cutscene.source_beat_id = beat.id;
    scene.cutscene.source_event_id = source_event_id;
    scene.cutscene.target_npc_id = FindNearestNpcIdToPlayer(scene);
    scene.cutscene.target_npc_animation = "gesture";

    const Entity* npc = FindEntityById(scene, scene.cutscene.target_npc_id);
    const glm::vec3 focus = npc == nullptr ? scene.player_proxy_position : npc->transform.pos;
    scene.cutscene.camera_start = focus + glm::vec3(-3.2F, 1.9F, -3.6F);
    scene.cutscene.camera_mid = focus + glm::vec3(-1.4F, 2.3F, -1.9F);
    scene.cutscene.camera_end = focus + glm::vec3(0.1F, 1.8F, -1.2F);

    scene.cutscene.narrator_line = narrator_line_override.empty()
        ? ("Cutscene: " + (beat.title.empty() ? beat.id : beat.title))
        : narrator_line_override;
    NarratorSystem::QueueLine(scene, scene.cutscene.narrator_line, "cutscene:" + beat.id);

    scene.recent_actions.push_back("cutscene_started:" + beat.id);
    GF_LOG_INFO("Cutscene started for beat: " + beat.id);
    return true;
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    if (!scene.cutscene.active) {
        return;
    }

    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.25F);
    scene.cutscene.elapsed_seconds += safe_dt;
    const float progress = Clamp01(scene.cutscene.elapsed_seconds / std::max(0.1F, scene.cutscene.duration_seconds));

    if (Entity* npc = FindEntityById(scene, scene.cutscene.target_npc_id)) {
        const float wave = std::sin(scene.elapsed_seconds * 4.2F);
        npc->transform.rot.y += wave * safe_dt * 0.4F;
    }

    const glm::vec3 camera_a = Lerp(scene.cutscene.camera_start, scene.cutscene.camera_mid, Clamp01(progress * 1.4F));
    const glm::vec3 camera_b = Lerp(scene.cutscene.camera_mid, scene.cutscene.camera_end, Clamp01((progress - 0.35F) / 0.65F));
    scene.player_proxy_position = progress < 0.35F ? camera_a : camera_b;

    if (progress < 1.0F) {
        return;
    }

    scene.cutscene.active = false;
    if (!scene.cutscene.source_beat_id.empty() && !HasPlayedBeatCutscene(scene, scene.cutscene.source_beat_id)) {
        scene.cutscene.played_cutscene_history.push_back(scene.cutscene.source_beat_id);
    }
    scene.recent_actions.push_back("cutscene_completed:" + scene.cutscene.source_beat_id);
    GF_LOG_INFO("Cutscene complete for beat: " + scene.cutscene.source_beat_id);
}

}  // namespace CutsceneSystem
