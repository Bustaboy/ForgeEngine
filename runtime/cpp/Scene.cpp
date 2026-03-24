#include "Scene.h"

#include "BuildingSystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "NavmeshSystem.h"
#include "AnimationSystem.h"
#include "SceneLoader.h"
#include "templates/generated_gameplay.h"

#include <algorithm>
#include <cmath>
#include <cstddef>

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

}  // namespace

void Scene::Update(float dt_seconds) {
    FactionSystem::EnsureSceneFactions(*this);
    EconomySystem::EnsureDefaults(*this);
    constexpr float kMaxTimeStepSeconds = 0.25F;
    const float safe_dt = std::clamp(dt_seconds, 0.0F, kMaxTimeStepSeconds);
    elapsed_seconds += safe_dt;

    day_cycle_speed = std::max(0.0F, day_cycle_speed);
    day_progress += safe_dt * day_cycle_speed;
    while (day_progress >= 1.0F) {
        day_progress -= 1.0F;
        ++day_count;
    }
    day_progress = Clamp01(day_progress);
    day_count = std::max(1U, day_count);

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
    directional_light.intensity = 0.15F + daylight * 1.15F;

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

    UpdateGameplay(*this, safe_dt);
    EconomySystem::Update(*this, safe_dt);
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


void Scene::MarkNavmeshDirty() {
    NavmeshSystem::MarkDirty(*this);
}
