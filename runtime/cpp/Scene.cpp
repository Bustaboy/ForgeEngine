#include "Scene.h"

#include "BuildingSystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "NavmeshSystem.h"
#include "AnimationSystem.h"
#include "NPCController.h"
#include "LivingNpcSystem.h"
#include "FreeWillSystem.h"
#include "ScriptedBehaviorSystem.h"
#include "RAGSystem.h"
#include "RelationshipSystem.h"
#include "NarratorSystem.h"
#include "SceneLoader.h"
#include "StorySystem.h"
#include "CutsceneSystem.h"
#include "VoiceSystem.h"
#include "WeatherSystem.h"
#include "SettlementSystem.h"
#include "CombatSystem.h"
#include "RealTimeCombatSystem.h"
#include "AudioSystem.h"
#include "templates/generated_gameplay.h"
#include "Logger.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <unordered_set>
#include <nlohmann/json.hpp>

#include <glm/gtc/constants.hpp>

namespace {
float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

glm::vec3 Lerp(const glm::vec3& start, const glm::vec3& end, float t) {
    return start + (end - start) * Clamp01(t);
}

float SegmentT(float progress, float start, float end) {
    if (end <= start) {
        return 0.0F;
    }
    return Clamp01((progress - start) / (end - start));
}

glm::vec3 SampleSkyColor(float day_progress) {
    constexpr glm::vec3 kNightSky{0.03F, 0.05F, 0.12F};
    constexpr glm::vec3 kSunriseSky{0.95F, 0.48F, 0.20F};
    constexpr glm::vec3 kDaySky{0.70F, 0.85F, 1.00F};
    constexpr glm::vec3 kSunsetSky{0.98F, 0.38F, 0.25F};

    if (day_progress < 0.25F) {
        return Lerp(kNightSky, kSunriseSky, SegmentT(day_progress, 0.0F, 0.25F));
    }
    if (day_progress < 0.50F) {
        return Lerp(kSunriseSky, kDaySky, SegmentT(day_progress, 0.25F, 0.50F));
    }
    if (day_progress < 0.75F) {
        return Lerp(kDaySky, kSunsetSky, SegmentT(day_progress, 0.50F, 0.75F));
    }
    return Lerp(kSunsetSky, kNightSky, SegmentT(day_progress, 0.75F, 1.0F));
}

void SyncLegacyTimeFieldsFromWorldTime(Scene& scene) {
    scene.elapsed_seconds = scene.world_time.elapsed_seconds;
    scene.day_progress = scene.world_time.day_progress;
    scene.day_cycle_speed = scene.world_time.day_cycle_speed;
    scene.day_count = scene.world_time.day_count;
}

void SyncWorldTimeFromLegacyFields(Scene& scene) {
    scene.world_time.elapsed_seconds = scene.elapsed_seconds;
    scene.world_time.day_progress = scene.day_progress;
    scene.world_time.day_cycle_speed = scene.day_cycle_speed;
    scene.world_time.day_count = scene.day_count;
}

template <typename TContainer>
void TrimFront(TContainer& container, std::size_t max_size) {
    if (container.size() <= max_size) {
        return;
    }
    container.erase(container.begin(), container.begin() + static_cast<std::ptrdiff_t>(container.size() - max_size));
}

void ApplyMemoryGuardrails(Scene& scene) {
    TrimFront(scene.recent_actions, SceneLimits::kRecentActionsCap);
    TrimFront(scene.narrator.spoken_history, SceneLimits::kRecentActionsCap);

    if (scene.free_will.daily_spark_count.size() > SceneLimits::kFreeWillMapCap) {
        std::size_t to_remove = scene.free_will.daily_spark_count.size() - SceneLimits::kFreeWillMapCap;
        for (auto it = scene.free_will.daily_spark_count.begin();
             it != scene.free_will.daily_spark_count.end() && to_remove > 0;
             ) {
            it = scene.free_will.daily_spark_count.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.last_spark_line_by_npc.size() > SceneLimits::kFreeWillMapCap) {
        std::size_t to_remove = scene.free_will.last_spark_line_by_npc.size() - SceneLimits::kFreeWillMapCap;
        for (auto it = scene.free_will.last_spark_line_by_npc.begin();
             it != scene.free_will.last_spark_line_by_npc.end() && to_remove > 0;
             ) {
            it = scene.free_will.last_spark_line_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.last_spark_source_by_npc.size() > SceneLimits::kFreeWillMapCap) {
        std::size_t to_remove = scene.free_will.last_spark_source_by_npc.size() - SceneLimits::kFreeWillMapCap;
        for (auto it = scene.free_will.last_spark_source_by_npc.begin();
             it != scene.free_will.last_spark_source_by_npc.end() && to_remove > 0;
             ) {
            it = scene.free_will.last_spark_source_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.rag_hits_by_npc.size() > SceneLimits::kFreeWillMapCap) {
        std::size_t to_remove = scene.free_will.rag_hits_by_npc.size() - SceneLimits::kFreeWillMapCap;
        for (auto it = scene.free_will.rag_hits_by_npc.begin();
             it != scene.free_will.rag_hits_by_npc.end() && to_remove > 0;
             ) {
            it = scene.free_will.rag_hits_by_npc.erase(it);
            --to_remove;
        }
    }
    if (scene.free_will.rag_misses_by_npc.size() > SceneLimits::kFreeWillMapCap) {
        std::size_t to_remove = scene.free_will.rag_misses_by_npc.size() - SceneLimits::kFreeWillMapCap;
        for (auto it = scene.free_will.rag_misses_by_npc.begin();
             it != scene.free_will.rag_misses_by_npc.end() && to_remove > 0;
             ) {
            it = scene.free_will.rag_misses_by_npc.erase(it);
            --to_remove;
        }
    }

    if (scene.combat.units.size() > SceneLimits::kCombatStateCap) {
        scene.combat.units.resize(SceneLimits::kCombatStateCap);
    }
    if (scene.combat.turn_order.size() > SceneLimits::kCombatStateCap) {
        scene.combat.turn_order.resize(SceneLimits::kCombatStateCap);
    }
    if (scene.rag.spark_cache.size() > SceneLimits::kRagEntriesCap) {
        scene.rag.spark_cache.erase(
            scene.rag.spark_cache.begin(),
            scene.rag.spark_cache.begin() + static_cast<std::ptrdiff_t>(scene.rag.spark_cache.size() - SceneLimits::kRagEntriesCap));
    }
    if (scene.rag.narrative_cache.size() > SceneLimits::kRagEntriesCap) {
        scene.rag.narrative_cache.erase(
            scene.rag.narrative_cache.begin(),
            scene.rag.narrative_cache.begin() + static_cast<std::ptrdiff_t>(scene.rag.narrative_cache.size() - SceneLimits::kRagEntriesCap));
    }
    if (scene.compressed_event_log.size() > SceneLimits::kLegacyEventLogCap) {
        scene.compressed_event_log.erase(
            scene.compressed_event_log.begin(),
            scene.compressed_event_log.begin() + static_cast<std::ptrdiff_t>(scene.compressed_event_log.size() - SceneLimits::kLegacyEventLogCap));
    }
}


void EmitQualityGuardrailWarnings(const Scene& scene) {
    static bool vram_warned = false;
    static bool sprite_warned = false;
    const bool vram_high = scene.quality_metadata.estimated_vram_mb > static_cast<float>(scene.quality_metadata.vram_warning_threshold_mb);
    const bool sprites_high = scene.quality_metadata.sprite_count > scene.quality_metadata.sprite_warning_threshold;

    if (vram_high && !vram_warned) {
        GF_LOG_WARN(
            "QualityGuardrail: estimated_vram_mb=" + std::to_string(scene.quality_metadata.estimated_vram_mb) +
            " exceeds threshold_mb=" + std::to_string(scene.quality_metadata.vram_warning_threshold_mb));
        vram_warned = true;
    } else if (!vram_high) {
        vram_warned = false;
    }

    if (sprites_high && !sprite_warned) {
        GF_LOG_WARN(
            "QualityGuardrail: sprite_count=" + std::to_string(scene.quality_metadata.sprite_count) +
            " exceeds threshold=" + std::to_string(scene.quality_metadata.sprite_warning_threshold));
        sprite_warned = true;
    } else if (!sprites_high) {
        sprite_warned = false;
    }
}

void EmitRuntimeOptimizationWarnings(const Scene& scene) {
    if (!scene.optimization_overrides.runtime.enabled || !scene.optimization_overrides.runtime.memory_guardrails_enabled) {
        return;
    }

    static bool entity_warned = false;
    static bool texture_warned = false;
    static bool vram_warned = false;
    const int entity_count = static_cast<int>(scene.entities.size());
    std::unordered_set<std::string> unique_assets{};
    unique_assets.reserve(scene.render_2d.sprites.size());
    for (const SceneSprite2D& sprite : scene.render_2d.sprites) {
        if (!sprite.asset_id.empty()) {
            unique_assets.insert(sprite.asset_id);
        }
    }
    const int texture_count = static_cast<int>(unique_assets.size());
    const int vram_mb = static_cast<int>(std::round(scene.quality_metadata.estimated_vram_mb));

    if (entity_count > scene.optimization_overrides.runtime.safe_entity_count && !entity_warned) {
        GF_LOG_WARN(
            "RuntimeGuardrail: entity_count=" + std::to_string(entity_count) +
            " exceeds safe_entity_count=" + std::to_string(scene.optimization_overrides.runtime.safe_entity_count));
        entity_warned = true;
    } else if (entity_count <= scene.optimization_overrides.runtime.safe_entity_count) {
        entity_warned = false;
    }

    if (texture_count > scene.optimization_overrides.runtime.safe_texture_count && !texture_warned) {
        GF_LOG_WARN(
            "RuntimeGuardrail: unique_texture_assets=" + std::to_string(texture_count) +
            " exceeds safe_texture_count=" + std::to_string(scene.optimization_overrides.runtime.safe_texture_count));
        texture_warned = true;
    } else if (texture_count <= scene.optimization_overrides.runtime.safe_texture_count) {
        texture_warned = false;
    }

    if (vram_mb > scene.optimization_overrides.runtime.safe_vram_mb && !vram_warned) {
        GF_LOG_WARN(
            "RuntimeGuardrail: estimated_vram_mb=" + std::to_string(vram_mb) +
            " exceeds safe_vram_mb=" + std::to_string(scene.optimization_overrides.runtime.safe_vram_mb));
        vram_warned = true;
    } else if (vram_mb <= scene.optimization_overrides.runtime.safe_vram_mb) {
        vram_warned = false;
    }
}

void EmitProjectHealthGuardrails(const Scene& scene) {
    static bool soft_warned = false;
    static bool hard_warned = false;
    const int score = std::clamp(scene.optimization_overrides.project_health_score, 0, 100);
    const int soft_threshold = std::clamp(scene.optimization_overrides.guardrails.soft_warning_threshold, 20, 95);
    const int hard_threshold = std::clamp(scene.optimization_overrides.guardrails.hard_block_threshold, 10, 90);
    const bool soft_violation = score <= soft_threshold;
    const bool hard_violation = scene.optimization_overrides.guardrails.hard_block_enabled && score <= hard_threshold;

    if (soft_violation && !soft_warned) {
        GF_LOG_WARN(
            "ProjectHealthGuardrail: score=" + std::to_string(score) +
            " is below soft_warning_threshold=" + std::to_string(soft_threshold));
        soft_warned = true;
    } else if (!soft_violation) {
        soft_warned = false;
    }

    if (hard_violation && !hard_warned) {
        GF_LOG_WARN(
            "ProjectHealthGuardrail: score=" + std::to_string(score) +
            " is below hard_block_threshold=" + std::to_string(hard_threshold) +
            " (hard_block_enabled=true)");
        hard_warned = true;
    } else if (!hard_violation) {
        hard_warned = false;
    }
}

}  // namespace

void Scene::Update(float dt_seconds) {
    SyncWorldTimeFromLegacyFields(*this);
    FactionSystem::EnsureSceneFactions(*this);
    EconomySystem::EnsureDefaults(*this);
    RelationshipSystem::EnsureSceneRelationships(*this);
    WeatherSystem::EnsureDefaults(*this);
    SettlementSystem::EnsureDefaults(*this);
    LivingNpcSystem::EnsureDefaults(*this);
    ScriptedBehaviorSystem::EnsureDefaults(*this);
    FreeWillSystem::EnsureDefaults(*this);
    RAGSystem::EnsureDefaults(*this);
    CombatSystem::EnsureDefaults(*this);
    RealTimeCombatSystem::EnsureDefaults(*this);
    AudioSystem::EnsureDefaults(*this);
    constexpr float kMaxTimeStepSeconds = 0.25F;
    const float safe_dt = std::clamp(dt_seconds, 0.0F, kMaxTimeStepSeconds);
    world_time.elapsed_seconds += safe_dt;

    world_time.day_cycle_speed = std::max(0.0F, world_time.day_cycle_speed);
    world_time.day_progress += safe_dt * world_time.day_cycle_speed;
    while (world_time.day_progress >= 1.0F) {
        world_time.day_progress -= 1.0F;
        ++world_time.day_count;
    }
    world_time.day_progress = Clamp01(world_time.day_progress);
    world_time.day_count = std::max(1U, world_time.day_count);
    world_time.minutes_per_day = std::max(60U, world_time.minutes_per_day);
    SyncLegacyTimeFieldsFromWorldTime(*this);
    WeatherSystem::Update(*this, safe_dt);

    if (active_dialog_npc_id != 0) {
        const auto active_it = std::find_if(entities.begin(), entities.end(), [&](const Entity& entity) {
            return entity.id == active_dialog_npc_id;
        });
        if (active_it == entities.end() || !active_it->dialog.in_progress) {
            active_dialog_npc_id = 0;
        }
    }

    const float sun_angle = day_progress * glm::two_pi<float>();
    const glm::vec3 sun_direction = glm::normalize(glm::vec3(std::cos(sun_angle), std::sin(sun_angle), 0.25F));
    const float daylight = std::max(0.0F, sun_direction.y);
    const glm::vec3 sky_color = SampleSkyColor(day_progress);

    directional_light.direction = sun_direction;
    directional_light.color = sky_color;
    directional_light.intensity = (0.15F + daylight * 1.15F) * std::clamp(weather.light_multiplier, 0.55F, 1.05F);

    NavmeshSystem::Update(*this, safe_dt);

    for (std::size_t i = 0; i < entities.size(); ++i) {
        Entity& entity = entities[i];
        if (entity.buildable.IsValid()) {
            continue;
        }

        const bool has_navigation = npc_navigation.find(entity.id) != npc_navigation.end();
        entity.transform.pos += entity.velocity * safe_dt;
        const bool realtime_controlled = realtime_combat.active && entity.realtime_combat.enabled;

        if (!has_navigation && !realtime_controlled) {
            entity.transform.pos.y += std::sin((elapsed_seconds * 1.35F) + static_cast<float>(i) * 0.85F) * 0.35F * safe_dt;
            entity.transform.rot.z = elapsed_seconds * (0.3F + static_cast<float>(i) * 0.15F);

            if (entity.transform.pos.x > 1.2F) {
                entity.transform.pos.x = -1.2F;
            }
        }

        if (!realtime_controlled) {
            const float pulse_r = 0.5F + 0.5F * std::sin(elapsed_seconds * (0.9F + static_cast<float>(i) * 0.1F));
            const float pulse_g = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.1F + static_cast<float>(i) * 0.07F));
            const float pulse_b = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.3F + static_cast<float>(i) * 0.05F));

            entity.renderable.color.r = 0.25F + 0.75F * pulse_r;
            entity.renderable.color.g = 0.25F + 0.75F * pulse_g;
            entity.renderable.color.b = 0.25F + 0.75F * pulse_b;
            entity.renderable.color.a = 1.0F;
        }
    }

    AnimationSystem::Update(*this, safe_dt);
    LivingNpcSystem::Update(*this, safe_dt);
    FreeWillSystem::Update(*this, safe_dt);
    RAGSystem::Update(*this, safe_dt);

    UpdateGameplay(*this, safe_dt);
    StorySystem::Update(*this, safe_dt);
    CutsceneSystem::Update(*this, safe_dt);
    NarratorSystem::Update(*this, safe_dt);
    VoiceSystem::Update(*this, safe_dt);
    EconomySystem::Update(*this, safe_dt);
    SettlementSystem::Update(*this, safe_dt);
    RelationshipSystem::Update(*this, safe_dt);
    CombatSystem::Update(*this, safe_dt);
    RealTimeCombatSystem::Update(*this, safe_dt);
    AudioSystem::Update(*this, safe_dt);
    if (!realtime_combat.active && realtime_combat.animation_preview.empty()) {
        realtime_combat.animation_preview = "idle";
    }
    ApplyMemoryGuardrails(*this);
    EmitQualityGuardrailWarnings(*this);
    EmitRuntimeOptimizationWarnings(*this);
    EmitProjectHealthGuardrails(*this);
}

bool Scene::ToggleBuildMode() {
    build_mode_enabled = !build_mode_enabled;
    if (!build_mode_enabled) {
        build_ghost_preview.reset();
    }
    return build_mode_enabled;
}

std::optional<Entity> Scene::GetBuildGhostPreviewFromRay(const glm::vec3& ray_origin, const glm::vec3& ray_direction) const {
    return GetGhostPreview(*this, ray_origin, ray_direction);
}

bool Scene::TryPlaceBuildingFromRay(const glm::vec3& ray_origin, const glm::vec3& ray_direction) {
    return ::TryPlaceBuildingFromRay(*this, ray_origin, ray_direction);
}

bool Scene::Save(const std::string& path) const {
    return SceneLoader::Save(path, *this);
}

bool Scene::Load(const std::string& path) {
    return SceneLoader::Load(path, *this);
}

bool Scene::ApplyPatch(const std::string& patch_json) {
    using json = nlohmann::json;

    json patch_payload;
    try {
        patch_payload = json::parse(patch_json);
    } catch (...) {
        return false;
    }

    const json changes = patch_payload.value("changes", json::object());
    if (!changes.is_object()) {
        return false;
    }

    const json render_patch = changes.value("render_2d", json::object());
    if (!render_patch.is_object()) {
        return false;
    }

    render_2d.enabled = render_patch.value("enabled", render_2d.enabled);
    const std::string next_render_mode = render_patch.value("render_mode", render_2d.render_mode);
    if (next_render_mode == "2D" || next_render_mode == "3D") {
        render_2d.render_mode = next_render_mode;
    }

    if (render_patch.contains("entity_sprite_map_set") && render_patch["entity_sprite_map_set"].is_object()) {
        for (const auto& [entity_type, asset_id_node] : render_patch["entity_sprite_map_set"].items()) {
            if (!asset_id_node.is_string()) {
                continue;
            }
            render_2d.entity_sprite_map[entity_type] = asset_id_node.get<std::string>();
        }
    }

    if (render_patch.contains("sprites_add") && render_patch["sprites_add"].is_array()) {
        for (const json& sprite_node : render_patch["sprites_add"]) {
            if (!sprite_node.is_object()) {
                continue;
            }
            SceneSprite2D sprite{};
            sprite.asset_id = sprite_node.value("asset_id", "");
            sprite.entity_type = sprite_node.value("entity_type", "");
            if (sprite_node.contains("position") && sprite_node["position"].is_object()) {
                sprite.position.x = sprite_node["position"].value("x", sprite.position.x);
                sprite.position.y = sprite_node["position"].value("y", sprite.position.y);
            }
            if (sprite_node.contains("size") && sprite_node["size"].is_object()) {
                sprite.size.x = sprite_node["size"].value("x", sprite.size.x);
                sprite.size.y = sprite_node["size"].value("y", sprite.size.y);
            }
            if (sprite_node.contains("tint") && sprite_node["tint"].is_object()) {
                sprite.tint.r = sprite_node["tint"].value("r", sprite.tint.r);
                sprite.tint.g = sprite_node["tint"].value("g", sprite.tint.g);
                sprite.tint.b = sprite_node["tint"].value("b", sprite.tint.b);
                sprite.tint.a = sprite_node["tint"].value("a", sprite.tint.a);
            }
            sprite.rotation_radians = sprite_node.value("rotation_radians", sprite.rotation_radians);
            sprite.layer = sprite_node.value("layer", sprite.layer);
            if (!sprite.asset_id.empty()) {
                render_2d.sprites.push_back(sprite);
            }
        }
    }

    if (render_patch.contains("sprites_update") && render_patch["sprites_update"].is_array()) {
        for (const json& update_node : render_patch["sprites_update"]) {
            if (!update_node.is_object()) {
                continue;
            }
            const json match_node = update_node.value("match", json::object());
            const json set_node = update_node.value("set", json::object());
            if (!match_node.is_object() || !set_node.is_object()) {
                continue;
            }
            const std::string match_entity_type = match_node.value("entity_type", "");
            const std::string match_asset_id = match_node.value("asset_id", "");
            const int limit = update_node.value("limit", 1000000);
            int touched = 0;
            for (SceneSprite2D& sprite : render_2d.sprites) {
                if (!match_entity_type.empty() && sprite.entity_type != match_entity_type) {
                    continue;
                }
                if (!match_asset_id.empty() && sprite.asset_id != match_asset_id) {
                    continue;
                }
                if (set_node.contains("tint") && set_node["tint"].is_object()) {
                    sprite.tint.r = set_node["tint"].value("r", sprite.tint.r);
                    sprite.tint.g = set_node["tint"].value("g", sprite.tint.g);
                    sprite.tint.b = set_node["tint"].value("b", sprite.tint.b);
                    sprite.tint.a = set_node["tint"].value("a", sprite.tint.a);
                }
                ++touched;
                if (touched >= std::max(0, limit)) {
                    break;
                }
            }
        }
    }

    const json optimization_patch = changes.value("optimization_overrides", json::object());
    if (optimization_patch.is_object()) {
        optimization_overrides.project_health_score = std::clamp(
            optimization_patch.value("project_health_score", optimization_overrides.project_health_score), 0, 100);
        const std::string next_mode = optimization_patch.value("lightweight_mode", optimization_overrides.lightweight_mode);
        if (next_mode == "performance" || next_mode == "balanced" || next_mode == "quality") {
            optimization_overrides.lightweight_mode = next_mode;
        }
        const json guardrails_patch = optimization_patch.value("guardrails", json::object());
        if (guardrails_patch.is_object()) {
            optimization_overrides.guardrails.hard_block_enabled =
                guardrails_patch.value("hard_block_enabled", optimization_overrides.guardrails.hard_block_enabled);
            optimization_overrides.guardrails.soft_warning_threshold = std::clamp(
                guardrails_patch.value("soft_warning_threshold", optimization_overrides.guardrails.soft_warning_threshold), 20, 95);
            optimization_overrides.guardrails.hard_block_threshold = std::clamp(
                guardrails_patch.value("hard_block_threshold", optimization_overrides.guardrails.hard_block_threshold), 10, 90);
        }
        const json runtime_patch = optimization_patch.value("runtime", json::object());
        if (runtime_patch.is_object()) {
            optimization_overrides.runtime.enabled =
                runtime_patch.value("enabled", optimization_overrides.runtime.enabled);
            optimization_overrides.runtime.lod_distance_culling_enabled =
                runtime_patch.value("lod_distance_culling_enabled", optimization_overrides.runtime.lod_distance_culling_enabled);
            optimization_overrides.runtime.draw_call_batching_enabled =
                runtime_patch.value("draw_call_batching_enabled", optimization_overrides.runtime.draw_call_batching_enabled);
            optimization_overrides.runtime.shader_variant_cache_enabled =
                runtime_patch.value("shader_variant_cache_enabled", optimization_overrides.runtime.shader_variant_cache_enabled);
            optimization_overrides.runtime.memory_guardrails_enabled =
                runtime_patch.value("memory_guardrails_enabled", optimization_overrides.runtime.memory_guardrails_enabled);
            optimization_overrides.runtime.texture_atlas_enabled =
                runtime_patch.value("texture_atlas_enabled", optimization_overrides.runtime.texture_atlas_enabled);
            optimization_overrides.runtime.texture_compression_enabled =
                runtime_patch.value("texture_compression_enabled", optimization_overrides.runtime.texture_compression_enabled);
            optimization_overrides.runtime.lod_near_distance_m =
                std::max(0.0F, runtime_patch.value("lod_near_distance_m", optimization_overrides.runtime.lod_near_distance_m));
            optimization_overrides.runtime.lod_far_distance_m =
                std::max(optimization_overrides.runtime.lod_near_distance_m,
                    runtime_patch.value("lod_far_distance_m", optimization_overrides.runtime.lod_far_distance_m));
            optimization_overrides.runtime.sprite_cull_distance_m =
                std::max(0.0F, runtime_patch.value("sprite_cull_distance_m", optimization_overrides.runtime.sprite_cull_distance_m));
            optimization_overrides.runtime.mesh_cull_distance_m =
                std::max(0.0F, runtime_patch.value("mesh_cull_distance_m", optimization_overrides.runtime.mesh_cull_distance_m));
        }
    }

    const json audio_patch = changes.value("audio", json::object());
    if (audio_patch.is_object()) {
        audio.reverb_zone_type = audio_patch.value("reverb_zone_type", audio.reverb_zone_type);
        audio.runtime_reverb_preset = audio_patch.value("runtime_reverb_preset", audio.runtime_reverb_preset);
        audio.current_music_track = audio_patch.value("current_music_track", audio.current_music_track);
        audio.ambient_track = audio_patch.value("ambient_track", audio.ambient_track);
        audio.exploration_music_track = audio_patch.value("exploration_music_track", audio.exploration_music_track);
        audio.combat_music_track = audio_patch.value("combat_music_track", audio.combat_music_track);
        audio.master_volume = std::clamp(audio_patch.value("master_volume", audio.master_volume), 0.0F, 1.0F);
        audio.music_volume = std::clamp(audio_patch.value("music_volume", audio.music_volume), 0.0F, 1.0F);
        audio.ambient_volume = std::clamp(audio_patch.value("ambient_volume", audio.ambient_volume), 0.0F, 1.0F);
        audio.ui_volume = std::clamp(audio_patch.value("ui_volume", audio.ui_volume), 0.0F, 1.0F);
        audio.sfx_volume = std::clamp(audio_patch.value("sfx_volume", audio.sfx_volume), 0.0F, 1.0F);
        audio.weather_influence = std::clamp(audio_patch.value("weather_influence", audio.weather_influence), 0.0F, 1.0F);
        audio.combat_ducking_strength = std::clamp(audio_patch.value("combat_ducking_strength", audio.combat_ducking_strength), 0.0F, 1.0F);
        audio.ui_ducking_strength = std::clamp(audio_patch.value("ui_ducking_strength", audio.ui_ducking_strength), 0.0F, 1.0F);
        audio.procedural_intensity = std::clamp(audio_patch.value("procedural_intensity", audio.procedural_intensity), 0.0F, 1.0F);
        audio.reverb_wet_mix = std::clamp(audio_patch.value("reverb_wet_mix", audio.reverb_wet_mix), 0.0F, 1.0F);
        audio.combat_music_override = audio_patch.value("combat_music_override", audio.combat_music_override);
        audio.music_enabled = audio_patch.value("music_enabled", audio.music_enabled);
        audio.ambient_enabled = audio_patch.value("ambient_enabled", audio.ambient_enabled);
        audio.spatial_audio_enabled = audio_patch.value("spatial_audio_enabled", audio.spatial_audio_enabled);
        audio.ducking_enabled = audio_patch.value("ducking_enabled", audio.ducking_enabled);
        audio.reverb_enabled = audio_patch.value("reverb_enabled", audio.reverb_enabled);
        audio.procedural_audio_enabled = audio_patch.value("procedural_audio_enabled", audio.procedural_audio_enabled);
        audio.disable_distant_spatial_in_performance_mode = audio_patch.value(
            "disable_distant_spatial_in_performance_mode",
            audio.disable_distant_spatial_in_performance_mode);
        audio.max_spatial_voices = std::clamp(audio_patch.value("max_spatial_voices", audio.max_spatial_voices), 4, 64);
        audio.performance_spatial_voices =
            std::clamp(audio_patch.value("performance_spatial_voices", audio.performance_spatial_voices), 2, audio.max_spatial_voices);
        audio.spatial_voice_hard_limit = std::clamp(audio_patch.value("spatial_voice_hard_limit", audio.spatial_voice_hard_limit), 4, 96);
        audio.spatial_max_distance = std::clamp(audio_patch.value("spatial_max_distance", audio.spatial_max_distance), 6.0F, 120.0F);
        audio.performance_spatial_max_distance = std::clamp(
            audio_patch.value("performance_spatial_max_distance", audio.performance_spatial_max_distance),
            4.0F,
            audio.spatial_max_distance);
        audio.ui_duck_timer_seconds = std::max(0.0F, audio_patch.value("ui_duck_timer_seconds", audio.ui_duck_timer_seconds));
    }

    return true;
}


void Scene::MarkNavmeshDirty() {
    NavmeshSystem::MarkDirty(*this);
}

std::uint32_t Scene::MinuteOfDay() const {
    const std::uint32_t minutes_per_day = std::max(1U, world_time.minutes_per_day);
    const float minute_float = Clamp01(world_time.day_progress) * static_cast<float>(minutes_per_day);
    const auto minute = static_cast<std::uint32_t>(minute_float);
    return std::min(minutes_per_day - 1, minute);
}
