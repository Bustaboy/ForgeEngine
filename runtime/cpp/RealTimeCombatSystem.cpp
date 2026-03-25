#include "RealTimeCombatSystem.h"

#include "Logger.h"
#include "RelationshipSystem.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <glm/geometric.hpp>

namespace {

struct RuntimeState {
    RealTimeCombatSystem::InputFrame input{};
};

RuntimeState& MutableRuntimeState() {
    static RuntimeState state{};
    return state;
}

Entity* FindEntity(Scene& scene, std::uint64_t id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == id) {
            return &entity;
        }
    }
    return nullptr;
}

float WeatherDamageMultiplier(const Scene& scene) {
    const std::string weather = scene.weather.current_weather;
    if (weather == "storm" || weather == "sandstorm") {
        return 0.86F;
    }
    if (weather == "rain" || weather == "snow") {
        return 0.92F;
    }
    return 1.0F;
}

float RelationshipDamageMultiplier(const Scene& scene, const Entity& attacker) {
    const float respect = RelationshipSystem::GetDimension(scene, attacker.id, "respect");
    const float grudge = RelationshipSystem::GetDimension(scene, attacker.id, "grudge");
    return std::clamp(1.0F + respect * 0.0015F + grudge * 0.0025F, 0.75F, 1.35F);
}

float MoraleDamageMultiplier(const Scene& scene) {
    return std::clamp(0.8F + (scene.settlement.morale / 100.0F) * 0.4F, 0.75F, 1.2F);
}

Entity* FindClosestHostile(Scene& scene, const Entity& attacker, float max_range) {
    float best_dist_sq = std::numeric_limits<float>::max();
    Entity* best = nullptr;
    for (Entity& candidate : scene.entities) {
        if (candidate.id == attacker.id || !candidate.realtime_combat.enabled || !candidate.realtime_combat.alive) {
            continue;
        }
        if (candidate.realtime_combat.team_id == attacker.realtime_combat.team_id) {
            continue;
        }
        const glm::vec3 delta = candidate.transform.pos - attacker.transform.pos;
        const float dist_sq = glm::dot(delta, delta);
        if (dist_sq > max_range * max_range) {
            continue;
        }
        if (dist_sq < best_dist_sq) {
            best_dist_sq = dist_sq;
            best = &candidate;
        }
    }
    return best;
}

std::size_t MaxRealtimeActors(const Scene& scene) {
    const std::string mode = scene.optimization_overrides.lightweight_mode;
    if (mode == "ultra" || mode == "aggressive") {
        return 8U;
    }
    if (mode == "performance") {
        return 12U;
    }
    return 24U;
}

void SanitizeComponent(RealTimeCombatComponent& rtc) {
    rtc.max_health = std::max(1.0F, rtc.max_health);
    rtc.health = std::clamp(rtc.health, 0.0F, rtc.max_health);
    rtc.max_stamina = std::max(1.0F, rtc.max_stamina);
    rtc.stamina = std::clamp(rtc.stamina, 0.0F, rtc.max_stamina);
    rtc.move_speed = std::clamp(rtc.move_speed, 0.5F, 12.0F);
    rtc.attack_damage = std::clamp(rtc.attack_damage, 1.0F, 120.0F);
    rtc.melee_range = std::clamp(rtc.melee_range, 0.5F, 4.0F);
    rtc.ranged_range = std::clamp(rtc.ranged_range, rtc.melee_range, 20.0F);
    rtc.attack_cooldown_seconds = std::clamp(rtc.attack_cooldown_seconds, 0.1F, 4.0F);
    rtc.dodge_cooldown_seconds = std::clamp(rtc.dodge_cooldown_seconds, 0.2F, 4.0F);
    rtc.dodge_duration_seconds = std::clamp(rtc.dodge_duration_seconds, 0.05F, 0.5F);
    rtc.hit_reaction_seconds = std::clamp(rtc.hit_reaction_seconds, 0.05F, 0.6F);
    rtc.stamina_regen_per_second = std::clamp(rtc.stamina_regen_per_second, 2.0F, 60.0F);
    rtc.stamina_attack_cost = std::clamp(rtc.stamina_attack_cost, 5.0F, 80.0F);
    rtc.stamina_dodge_cost = std::clamp(rtc.stamina_dodge_cost, 5.0F, 80.0F);
    rtc.attack_cooldown_remaining = std::max(0.0F, rtc.attack_cooldown_remaining);
    rtc.dodge_cooldown_remaining = std::max(0.0F, rtc.dodge_cooldown_remaining);
    rtc.dodge_remaining = std::max(0.0F, rtc.dodge_remaining);
    rtc.hit_reaction_remaining = std::max(0.0F, rtc.hit_reaction_remaining);
    rtc.alive = rtc.health > 0.0F;
}

}  // namespace

namespace RealTimeCombatSystem {

void EnsureDefaults(Scene& scene) {
    std::size_t enabled_count = 0U;
    for (Entity& entity : scene.entities) {
        if (!entity.realtime_combat.enabled) {
            continue;
        }
        SanitizeComponent(entity.realtime_combat);
        ++enabled_count;
    }

    if (enabled_count < 2U) {
        scene.realtime_combat.active = false;
    }

    if (scene.realtime_combat.controlled_entity_id != 0) {
        Entity* controlled = FindEntity(scene, scene.realtime_combat.controlled_entity_id);
        if (controlled == nullptr || !controlled->realtime_combat.enabled) {
            scene.realtime_combat.controlled_entity_id = 0;
        }
    }
}

void SetInput(Scene& scene, const InputFrame& input) {
    (void)scene;
    MutableRuntimeState().input = input;
}

bool Start(Scene& scene, const std::string& source) {
    EnsureDefaults(scene);

    std::uint64_t first_enabled = 0;
    std::size_t enabled_count = 0;
    for (Entity& entity : scene.entities) {
        if (!entity.realtime_combat.enabled) {
            continue;
        }
        if (first_enabled == 0) {
            first_enabled = entity.id;
        }
        ++enabled_count;
    }

    if (enabled_count < 2U) {
        return false;
    }

    scene.realtime_combat.active = true;
    scene.realtime_combat.trigger_source = source;
    scene.realtime_combat.last_action = "start";
    scene.realtime_combat.last_resolution.clear();
    if (scene.realtime_combat.controlled_entity_id == 0) {
        scene.realtime_combat.controlled_entity_id = first_enabled;
    }

    bool assigned_player = false;
    for (Entity& entity : scene.entities) {
        if (!entity.realtime_combat.enabled) {
            continue;
        }
        if (!assigned_player && entity.id == scene.realtime_combat.controlled_entity_id) {
            entity.realtime_combat.team_id = 0;
            assigned_player = true;
        } else {
            entity.realtime_combat.team_id = std::max<std::uint32_t>(1U, entity.realtime_combat.team_id);
        }
        entity.realtime_combat.alive = entity.realtime_combat.health > 0.0F;
        entity.realtime_combat.action_state = "idle";
    }

    scene.recent_actions.push_back("realtime_combat_start:" + source);
    return true;
}

bool QueueAction(Scene& scene, const std::string& action, std::string& out_message) {
    EnsureDefaults(scene);
    if (!scene.realtime_combat.active) {
        out_message = "Real-time combat is not active.";
        return false;
    }

    Entity* actor = FindEntity(scene, scene.realtime_combat.controlled_entity_id);
    if (actor == nullptr || !actor->realtime_combat.enabled || !actor->realtime_combat.alive) {
        out_message = "Real-time actor unavailable.";
        return false;
    }

    RealTimeCombatComponent& rtc = actor->realtime_combat;
    if (action == "stop") {
        MutableRuntimeState().input = InputFrame{};
        MutableRuntimeState().input.stop_pressed = true;
        out_message = "Queued stop action.";
        return true;
    }
    if (action == "move") {
        MutableRuntimeState().input = InputFrame{};
        MutableRuntimeState().input.move_axis = glm::vec2(0.0F, 1.0F);
        out_message = "Queued move action.";
        return true;
    }
    if (action == "attack") {
        if (rtc.stamina < rtc.stamina_attack_cost) {
            out_message = "Not enough stamina to attack.";
            return false;
        }
        MutableRuntimeState().input.attack_pressed = true;
        out_message = "Queued attack action.";
        return true;
    }
    if (action == "dodge") {
        if (rtc.stamina < rtc.stamina_dodge_cost) {
            out_message = "Not enough stamina to dodge.";
            return false;
        }
        MutableRuntimeState().input.dodge_pressed = true;
        out_message = "Queued dodge action.";
        return true;
    }

    out_message = "Usage: /realtime_combat_action <attack|dodge|move|stop>";
    return false;
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    if (!scene.realtime_combat.active) {
        return;
    }

    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.05F);
    InputFrame input = MutableRuntimeState().input;
    MutableRuntimeState().input = InputFrame{};

    Entity* actor = FindEntity(scene, scene.realtime_combat.controlled_entity_id);
    if (actor == nullptr || !actor->realtime_combat.enabled || !actor->realtime_combat.alive) {
        scene.realtime_combat.active = false;
        scene.realtime_combat.last_resolution = "controlled_entity_unavailable";
        return;
    }

    std::size_t alive_team0 = 0U;
    std::size_t alive_hostiles = 0U;
    std::size_t simulated_count = 0U;
    const std::size_t actor_cap = MaxRealtimeActors(scene);

    for (Entity& entity : scene.entities) {
        if (!entity.realtime_combat.enabled || simulated_count >= actor_cap) {
            continue;
        }
        ++simulated_count;
        RealTimeCombatComponent& rtc = entity.realtime_combat;
        SanitizeComponent(rtc);

        rtc.attack_cooldown_remaining = std::max(0.0F, rtc.attack_cooldown_remaining - safe_dt);
        rtc.dodge_cooldown_remaining = std::max(0.0F, rtc.dodge_cooldown_remaining - safe_dt);
        rtc.dodge_remaining = std::max(0.0F, rtc.dodge_remaining - safe_dt);
        rtc.hit_reaction_remaining = std::max(0.0F, rtc.hit_reaction_remaining - safe_dt);
        rtc.stamina = std::clamp(rtc.stamina + rtc.stamina_regen_per_second * safe_dt, 0.0F, rtc.max_stamina);

        if (rtc.team_id == 0 && rtc.alive) {
            ++alive_team0;
        } else if (rtc.team_id != 0 && rtc.alive) {
            ++alive_hostiles;
        }

        if (entity.id == scene.realtime_combat.controlled_entity_id && rtc.alive) {
            if (input.stop_pressed) {
                rtc.action_state = "idle";
            } else if (rtc.hit_reaction_remaining <= 0.0F) {
                glm::vec2 axis = input.move_axis;
                if (glm::dot(axis, axis) > 1.0F) {
                    axis = glm::normalize(axis);
                }
                const float movement_multiplier = rtc.dodge_remaining > 0.0F ? 1.8F : 1.0F;
                entity.transform.pos.x += axis.x * rtc.move_speed * movement_multiplier * safe_dt;
                entity.transform.pos.z += axis.y * rtc.move_speed * movement_multiplier * safe_dt;
                if (glm::dot(axis, axis) > 0.0001F) {
                    rtc.action_state = rtc.dodge_remaining > 0.0F ? "dodging" : "moving";
                } else if (rtc.action_state == "moving") {
                    rtc.action_state = "idle";
                }
            }

            if (input.dodge_pressed && rtc.dodge_cooldown_remaining <= 0.0F && rtc.stamina >= rtc.stamina_dodge_cost) {
                rtc.stamina -= rtc.stamina_dodge_cost;
                rtc.dodge_cooldown_remaining = rtc.dodge_cooldown_seconds;
                rtc.dodge_remaining = rtc.dodge_duration_seconds;
                rtc.action_state = "dodging";
                scene.realtime_combat.last_action = "dodge";
            }

            if (input.attack_pressed && rtc.attack_cooldown_remaining <= 0.0F && rtc.stamina >= rtc.stamina_attack_cost) {
                rtc.stamina -= rtc.stamina_attack_cost;
                rtc.attack_cooldown_remaining = rtc.attack_cooldown_seconds;
                rtc.action_state = "attacking";

                const float range = rtc.ranged_enabled ? rtc.ranged_range : rtc.melee_range;
                Entity* target = FindClosestHostile(scene, entity, range);
                if (target != nullptr && target->realtime_combat.dodge_remaining <= 0.0F) {
                    const float base_damage = rtc.attack_damage;
                    const float damage = base_damage * WeatherDamageMultiplier(scene) *
                        RelationshipDamageMultiplier(scene, entity) * MoraleDamageMultiplier(scene);
                    target->realtime_combat.health = std::max(0.0F, target->realtime_combat.health - damage);
                    target->realtime_combat.alive = target->realtime_combat.health > 0.0F;
                    target->realtime_combat.hit_reaction_remaining = target->realtime_combat.hit_reaction_seconds;
                    target->realtime_combat.action_state = target->realtime_combat.alive ? "hit_reaction" : "down";
                    scene.realtime_combat.last_action = "attack_hit";
                } else {
                    scene.realtime_combat.last_action = "attack_miss";
                }
            }
        }

        if (!rtc.alive) {
            rtc.action_state = "down";
        } else if (rtc.hit_reaction_remaining > 0.0F) {
            rtc.action_state = "hit_reaction";
        } else if (rtc.action_state == "attacking") {
            rtc.action_state = "idle";
        }
    }

    if (alive_team0 == 0U || alive_hostiles == 0U) {
        scene.realtime_combat.active = false;
        scene.realtime_combat.last_resolution = alive_team0 > 0U ? "player_team_victory" : "player_team_defeat";
        scene.recent_actions.push_back("realtime_combat_outcome:" + scene.realtime_combat.last_resolution);
        GF_LOG_INFO("Realtime combat resolved: " + scene.realtime_combat.last_resolution);
    }
}

}  // namespace RealTimeCombatSystem
