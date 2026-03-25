#include "AnimationSystem.h"

#include "Scene.h"

#include <glm/geometric.hpp>
#include <algorithm>
#include <cmath>

namespace {
bool IsNpcEntity(const Entity& entity) {
    return entity.buildable.IsValid() == false && (!entity.faction.role.empty() || entity.dialog.IsValid());
}

float WrapAngle(float radians) {
    constexpr float kPi = 3.14159265F;
    while (radians > kPi) {
        radians -= 2.0F * kPi;
    }
    while (radians < -kPi) {
        radians += 2.0F * kPi;
    }
    return radians;
}

}  // namespace

namespace AnimationSystem {

void Update(Scene& scene, float dt_seconds) {
    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.25F);

    for (Entity& entity : scene.entities) {
        if (!IsNpcEntity(entity)) {
            continue;
        }

        const auto nav_it = scene.npc_navigation.find(entity.id);
        if (nav_it == scene.npc_navigation.end()) {
            continue;
        }

        NpcNavigationState& nav = nav_it->second;
        if (nav.path.empty() || nav.path_index >= nav.path.size()) {
            entity.velocity.x = 0.0F;
            entity.velocity.z = 0.0F;
        } else {
            const NavPathNode& node = nav.path[nav.path_index];
            const glm::vec3 target = {
                static_cast<float>(scene.navmesh.min_x) + (static_cast<float>(node.x) + 0.5F) * scene.navmesh.cell_size,
                entity.transform.pos.y,
                static_cast<float>(scene.navmesh.min_z) + (static_cast<float>(node.z) + 0.5F) * scene.navmesh.cell_size,
            };

            glm::vec3 to_target = target - entity.transform.pos;
            to_target.y = 0.0F;
            const float distance = glm::length(to_target);
            if (distance <= 0.18F) {
                nav.path_index = std::min(nav.path_index + 1U, nav.path.size());
                entity.velocity.x = 0.0F;
                entity.velocity.z = 0.0F;
            } else {
                const glm::vec3 direction = to_target / std::max(0.001F, distance);
                const float speed = std::clamp(nav.desired_speed, 0.7F, 2.4F);
                entity.velocity.x = direction.x * speed;
                entity.velocity.z = direction.z * speed;
            }
        }

        const float planar_speed = std::sqrt(entity.velocity.x * entity.velocity.x + entity.velocity.z * entity.velocity.z);
        if (entity.realtime_combat.enabled) {
            if (entity.realtime_combat.animation_state.empty()) {
                entity.realtime_combat.animation_state = "idle";
            }

            if (entity.realtime_combat.animation_state == "attacking" ||
                entity.realtime_combat.animation_state.rfind("attack_", 0) == 0) {
                const bool heavy_attack = entity.realtime_combat.animation_state.find("heavy") != std::string::npos ||
                    entity.realtime_combat.animation_state.find("finisher") != std::string::npos;
                entity.animation.motion_phase += safe_dt * (heavy_attack ? 5.1F : 6.4F);
            } else if (entity.realtime_combat.animation_state == "dodging" ||
                       entity.realtime_combat.animation_state.rfind("dodge_", 0) == 0) {
                entity.animation.motion_phase += safe_dt * 8.4F;
            } else if (entity.realtime_combat.animation_state == "hit_reaction") {
                entity.animation.motion_phase += safe_dt * 10.0F;
                const float flinch = std::sin(entity.animation.motion_phase * 2.0F) * 0.06F;
                entity.transform.rot.x = flinch;
                entity.transform.rot.z = -flinch * 0.5F;
                continue;
            }

            if (entity.realtime_combat.animation_state == "dodge_left") {
                entity.transform.rot.z = 0.18F;
            } else if (entity.realtime_combat.animation_state == "dodge_right") {
                entity.transform.rot.z = -0.18F;
            } else if (entity.realtime_combat.animation_state == "attack_finisher") {
                entity.transform.rot.y += safe_dt * 1.1F;
            }
        }
        entity.animation.motion_phase += safe_dt * (2.2F + planar_speed * 3.4F);
        const float gait = std::sin(entity.animation.motion_phase);
        const float opposite_gait = std::sin(entity.animation.motion_phase + 3.14159265F);

        const float idle_noise = std::sin(scene.elapsed_seconds * 0.55F + static_cast<float>(entity.id % 13ULL) * 0.4F) * 0.015F;
        const float bob = planar_speed > 0.05F ? std::abs(gait) * 0.045F : idle_noise;
        entity.animation.left_foot_offset = (planar_speed > 0.05F ? std::max(0.0F, gait) : 0.0F) * 0.09F;
        entity.animation.right_foot_offset = (planar_speed > 0.05F ? std::max(0.0F, opposite_gait) : 0.0F) * 0.09F;

        entity.transform.pos.y = bob;

        if (planar_speed > 0.02F) {
            const float desired_yaw = std::atan2(entity.velocity.x, entity.velocity.z);
            const float yaw_delta = WrapAngle(desired_yaw - entity.transform.rot.y);
            entity.transform.rot.y += yaw_delta * std::min(1.0F, safe_dt * 6.0F);
        }

        entity.transform.rot.x = (entity.animation.left_foot_offset - entity.animation.right_foot_offset) * 0.2F;
    }
}

}  // namespace AnimationSystem
