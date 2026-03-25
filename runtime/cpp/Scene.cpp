#include "Scene.h"

#include "BuildingSystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "NavmeshSystem.h"
#include "AnimationSystem.h"
#include "NPCController.h"
#include "FreeWillSystem.h"
#include "RelationshipSystem.h"
#include "NarratorSystem.h"
#include "SceneLoader.h"
#include "StorySystem.h"
#include "CutsceneSystem.h"
#include "VoiceSystem.h"
#include "WeatherSystem.h"
#include "SettlementSystem.h"
#include "CombatSystem.h"
#include "templates/generated_gameplay.h"
#include "Logger.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
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

    if (scene.combat.units.size() > SceneLimits::kCombatStateCap) {
        scene.combat.units.resize(SceneLimits::kCombatStateCap);
    }
    if (scene.combat.turn_order.size() > SceneLimits::kCombatStateCap) {
        scene.combat.turn_order.resize(SceneLimits::kCombatStateCap);
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

}  // namespace

void Scene::Update(float dt_seconds) {
    SyncWorldTimeFromLegacyFields(*this);
    FactionSystem::EnsureSceneFactions(*this);
    EconomySystem::EnsureDefaults(*this);
    RelationshipSystem::EnsureSceneRelationships(*this);
    WeatherSystem::EnsureDefaults(*this);
    SettlementSystem::EnsureDefaults(*this);
    NPCController::EnsureDefaults(*this);
    FreeWillSystem::EnsureDefaults(*this);
    CombatSystem::EnsureDefaults(*this);
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

        if (!has_navigation) {
            entity.transform.pos.y += std::sin((elapsed_seconds * 1.35F) + static_cast<float>(i) * 0.85F) * 0.35F * safe_dt;
            entity.transform.rot.z = elapsed_seconds * (0.3F + static_cast<float>(i) * 0.15F);

            if (entity.transform.pos.x > 1.2F) {
                entity.transform.pos.x = -1.2F;
            }
        }

        const float pulse_r = 0.5F + 0.5F * std::sin(elapsed_seconds * (0.9F + static_cast<float>(i) * 0.1F));
        const float pulse_g = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.1F + static_cast<float>(i) * 0.07F));
        const float pulse_b = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.3F + static_cast<float>(i) * 0.05F));

        entity.renderable.color.r = 0.25F + 0.75F * pulse_r;
        entity.renderable.color.g = 0.25F + 0.75F * pulse_g;
        entity.renderable.color.b = 0.25F + 0.75F * pulse_b;
        entity.renderable.color.a = 1.0F;
    }

    AnimationSystem::Update(*this, safe_dt);
    NPCController::Update(*this, safe_dt);
    FreeWillSystem::Update(*this, safe_dt);

    UpdateGameplay(*this, safe_dt);
    StorySystem::Update(*this, safe_dt);
    CutsceneSystem::Update(*this, safe_dt);
    NarratorSystem::Update(*this, safe_dt);
    VoiceSystem::Update(*this, safe_dt);
    EconomySystem::Update(*this, safe_dt);
    SettlementSystem::Update(*this, safe_dt);
    RelationshipSystem::Update(*this, safe_dt);
    CombatSystem::Update(*this, safe_dt);
    ApplyMemoryGuardrails(*this);
    EmitQualityGuardrailWarnings(*this);
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
