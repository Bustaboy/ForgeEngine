#include "CombatSystem.h"

#include "Logger.h"
#include "RelationshipSystem.h"
#include "Scene.h"

#include <glm/geometric.hpp>

#include <algorithm>
#include <cmath>

namespace {
constexpr std::size_t kCombatStateCap = 48U;
constexpr float kAttackRangeMelee = 1.8F;
constexpr float kAttackRangeRanged = 8.0F;
constexpr float kAttackStaminaCost = 20.0F;
constexpr float kDodgeStaminaCost = 25.0F;
constexpr float kMoveStaminaDrainPerSecond = 7.5F;
constexpr float kStaminaRegenPerSecond = 14.0F;

Entity* FindEntity(Scene& scene, std::uint64_t entity_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id) {
            return &entity;
        }
    }
    return nullptr;
}

CombatUnitState* FindUnit(Scene& scene, std::uint64_t entity_id) {
    for (CombatUnitState& unit : scene.combat.units) {
        if (unit.entity_id == entity_id) {
            return &unit;
        }
    }
    return nullptr;
}

float WeatherCombatMultiplier(const Scene& scene, bool ranged) {
    const std::string weather = scene.weather.current_weather;
    if (weather == "storm" || weather == "sandstorm") {
        return ranged ? 0.75F : 0.90F;
    }
    if (weather == "fog") {
        return ranged ? 0.82F : 0.95F;
    }
    if (weather == "rain" || weather == "snow") {
        return 0.90F;
    }
    return 1.0F;
}

float LightweightDamageScale(const Scene& scene) {
    if (scene.optimization_overrides.lightweight_mode == "performance") {
        return 0.88F;
    }
    if (scene.optimization_overrides.lightweight_mode == "quality") {
        return 1.06F;
    }
    return 1.0F;
}

float Distance(const CombatUnitState& a, const CombatUnitState& b) {
    return glm::length(a.world_position - b.world_position);
}

void ApplyOutcome(Scene& scene, bool player_team_won) {
    if (player_team_won) {
        scene.settlement.morale = std::clamp(scene.settlement.morale + 6.0F, 0.0F, 100.0F);
    } else {
        scene.settlement.morale = std::clamp(scene.settlement.morale - 8.0F, 0.0F, 100.0F);
    }

    for (const CombatUnitState& unit : scene.combat.units) {
        if (unit.entity_id == 0) {
            continue;
        }
        if (unit.team_id == 0) {
            RelationshipSystem::SetDimension(scene, unit.entity_id, "respect", player_team_won ? 3.0F : -2.0F, false);
            RelationshipSystem::SetDimension(scene, unit.entity_id, "trust", player_team_won ? 2.0F : -3.0F, false);
        } else {
            RelationshipSystem::SetDimension(scene, unit.entity_id, "grudge", player_team_won ? 6.0F : 2.0F, false);
            RelationshipSystem::SetDimension(scene, unit.entity_id, "trust", player_team_won ? -4.0F : -1.5F, false);
        }
    }

    scene.recent_actions.push_back(player_team_won ? "combat_outcome:player_win" : "combat_outcome:player_loss");
}

void ResolveIfComplete(Scene& scene) {
    bool player_alive = false;
    bool hostile_alive = false;
    for (const CombatUnitState& unit : scene.combat.units) {
        if (!unit.alive) {
            continue;
        }
        if (unit.team_id == 0U) {
            player_alive = true;
        } else {
            hostile_alive = true;
        }
    }

    if (!player_alive || !hostile_alive) {
        const bool player_won = player_alive && !hostile_alive;
        ApplyOutcome(scene, player_won);
        scene.combat.last_resolution = player_won ? "player_victory" : "player_defeat";
        scene.combat.active = false;
        scene.combat.queued_action.clear();
        scene.combat.queued_target.clear();
    }
}

void SyncEntityCombatFromUnit(Entity& entity, const CombatUnitState& unit) {
    entity.transform.pos = unit.world_position;
    entity.combat.health = unit.health;
    entity.combat.max_health = unit.max_health;
    entity.combat.stamina = unit.stamina;
    entity.combat.max_stamina = unit.max_stamina;
    entity.combat.attack_cooldown_seconds = unit.attack_cooldown_seconds;
    entity.combat.dodge_cooldown_seconds = unit.dodge_cooldown_seconds;
    entity.combat.action_state = unit.action_state;
}

CombatUnitState* FindNearestEnemy(Scene& scene, const CombatUnitState& actor) {
    CombatUnitState* best = nullptr;
    float best_distance = 99999.0F;
    for (CombatUnitState& candidate : scene.combat.units) {
        if (!candidate.alive || candidate.team_id == actor.team_id || candidate.entity_id == actor.entity_id) {
            continue;
        }
        const float distance = Distance(actor, candidate);
        if (distance < best_distance) {
            best_distance = distance;
            best = &candidate;
        }
    }
    return best;
}

float ComputeDamage(const Scene& scene, const Entity& attacker, const Entity& defender, bool ranged) {
    const float trust = RelationshipSystem::GetDimension(scene, attacker.id, "trust");
    const float respect = RelationshipSystem::GetDimension(scene, attacker.id, "respect");
    const float loyalty = RelationshipSystem::GetDimension(scene, defender.id, "loyalty");
    const float grudge = RelationshipSystem::GetDimension(scene, defender.id, "grudge");
    const float relationship_scale = 1.0F + std::clamp((trust + respect - loyalty + grudge) * 0.0025F, -0.20F, 0.25F);
    const float weather_scale = WeatherCombatMultiplier(scene, ranged);
    const float stamina_scale = 0.65F + std::clamp(attacker.combat.stamina / std::max(1.0F, attacker.combat.max_stamina), 0.0F, 1.0F) * 0.35F;
    const float base = ranged ? 10.0F : 14.0F;
    return std::max(1.0F, base * relationship_scale * weather_scale * stamina_scale * LightweightDamageScale(scene));
}

bool TryAttack(Scene& scene, CombatUnitState& attacker_unit, Entity& attacker_entity, CombatUnitState& target_unit, Entity& target_entity) {
    if (!attacker_unit.alive || !target_unit.alive || attacker_unit.team_id == target_unit.team_id) {
        return false;
    }

    const bool ranged = attacker_entity.combat.ranged_enabled || attacker_entity.inventory.items.count("ranged_kit") > 0;
    const float range = ranged ? kAttackRangeRanged : kAttackRangeMelee;
    if (Distance(attacker_unit, target_unit) > range) {
        return false;
    }
    if (attacker_unit.attack_cooldown_seconds > 0.0F || attacker_unit.stamina < kAttackStaminaCost) {
        return false;
    }

    const float damage = ComputeDamage(scene, attacker_entity, target_entity, ranged);
    attacker_unit.stamina = std::max(0.0F, attacker_unit.stamina - kAttackStaminaCost);
    attacker_unit.attack_cooldown_seconds = (scene.optimization_overrides.lightweight_mode == "performance") ? 0.55F : 0.45F;
    attacker_unit.action_state = "attacking";

    target_unit.health = std::max(0.0F, target_unit.health - damage);
    target_unit.alive = target_unit.health > 0.0F;
    target_unit.action_state = target_unit.alive ? "hit_reaction" : "idle";

    scene.recent_actions.push_back(
        "combat_hit:" + std::to_string(attacker_entity.id) + "->" + std::to_string(target_entity.id));
    return true;
}

}  // namespace

namespace CombatSystem {

void EnsureDefaults(Scene& scene) {
    scene.combat.grid_width = std::max(4U, scene.combat.grid_width);
    scene.combat.grid_height = std::max(4U, scene.combat.grid_height);
    scene.combat.input_move_x = std::clamp(scene.combat.input_move_x, -1.0F, 1.0F);
    scene.combat.input_move_z = std::clamp(scene.combat.input_move_z, -1.0F, 1.0F);
    if (scene.combat.units.size() > kCombatStateCap) {
        scene.combat.units.resize(kCombatStateCap);
    }
    scene.combat.units.erase(
        std::remove_if(scene.combat.units.begin(), scene.combat.units.end(), [](const CombatUnitState& unit) {
            return unit.entity_id == 0;
        }),
        scene.combat.units.end());
}

void SetMoveInput(Scene& scene, float input_x, float input_z) {
    scene.combat.input_move_x = std::clamp(input_x, -1.0F, 1.0F);
    scene.combat.input_move_z = std::clamp(input_z, -1.0F, 1.0F);
}

bool StartEncounter(
    Scene& scene,
    const std::vector<std::uint64_t>& participant_ids,
    std::uint32_t grid_width,
    std::uint32_t grid_height,
    const std::string& source) {
    EnsureDefaults(scene);

    std::vector<std::uint64_t> ids = participant_ids;
    if (ids.empty()) {
        ids.reserve(4U);
        for (const Entity& entity : scene.entities) {
            if (entity.buildable.IsValid()) {
                continue;
            }
            ids.push_back(entity.id);
            if (ids.size() >= 4U) {
                break;
            }
        }
    }

    if (ids.size() < 2U) {
        GF_LOG_INFO("Combat start failed: requires at least two units.");
        return false;
    }

    scene.combat = CombatState{};
    scene.combat.active = true;
    scene.combat.combat_mode_enabled = true;
    scene.combat.grid_width = std::max(4U, grid_width);
    scene.combat.grid_height = std::max(4U, grid_height);
    scene.combat.trigger_source = source;

    std::uint32_t slot = 0;
    for (const std::uint64_t id : ids) {
        Entity* entity = FindEntity(scene, id);
        if (entity == nullptr) {
            continue;
        }

        entity->combat.enabled = true;
        entity->combat.max_health = std::max(20.0F, entity->combat.max_health);
        if (entity->combat.health <= 0.0F) {
            entity->combat.health = entity->combat.max_health;
        }

        CombatUnitState unit{};
        unit.entity_id = entity->id;
        unit.team_id = (slot == 0U) ? 0U : 1U;
        unit.world_position = entity->transform.pos;
        unit.max_health = entity->combat.max_health;
        unit.health = std::clamp(entity->combat.health, 0.0F, unit.max_health);
        unit.max_stamina = std::max(50.0F, entity->combat.max_stamina);
        unit.stamina = std::clamp(entity->combat.stamina <= 0.0F ? unit.max_stamina : entity->combat.stamina, 0.0F, unit.max_stamina);
        unit.attack_cooldown_seconds = 0.0F;
        unit.dodge_cooldown_seconds = 0.0F;
        unit.action_state = "idle";
        unit.alive = unit.health > 0.0F;

        scene.combat.units.push_back(unit);
        ++slot;
    }

    if (scene.combat.units.size() < 2U) {
        scene.combat = CombatState{};
        GF_LOG_INFO("Combat start failed: not enough valid participants.");
        return false;
    }

    scene.recent_actions.push_back("combat_start:" + source);
    GF_LOG_INFO("Combat started (real-time). Participants=" + std::to_string(scene.combat.units.size()));
    return true;
}

bool TryAction(Scene& scene, const std::string& action, const std::string& target, std::string& out_message) {
    EnsureDefaults(scene);
    if (action == "start") {
        const bool ok = StartEncounter(scene, {}, scene.combat.grid_width, scene.combat.grid_height, "console_action");
        out_message = ok ? "Combat started." : "Combat start failed.";
        return ok;
    }

    if (!scene.combat.active) {
        out_message = "No active combat.";
        return false;
    }

    if (action == "attack" || action == "dodge" || action == "move" || action == "ranged") {
        scene.combat.queued_action = action;
        scene.combat.queued_target = target;
        out_message = "Queued combat action: " + action;
        return true;
    }

    if (action == "stop") {
        scene.combat.active = false;
        scene.combat.queued_action.clear();
        scene.combat.queued_target.clear();
        scene.combat.last_resolution = "stopped";
        out_message = "Combat stopped.";
        return true;
    }

    out_message = "Unknown combat action. Use attack|ranged|dodge|move|stop.";
    return false;
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    if (!scene.combat.active) {
        return;
    }

    for (CombatUnitState& unit : scene.combat.units) {
        Entity* entity = FindEntity(scene, unit.entity_id);
        if (entity == nullptr || !unit.alive) {
            continue;
        }

        unit.attack_cooldown_seconds = std::max(0.0F, unit.attack_cooldown_seconds - dt_seconds);
        unit.dodge_cooldown_seconds = std::max(0.0F, unit.dodge_cooldown_seconds - dt_seconds);
        unit.stamina = std::clamp(unit.stamina + (kStaminaRegenPerSecond * dt_seconds), 0.0F, unit.max_stamina);

        if (unit.team_id == 0U) {
            glm::vec3 move_dir{scene.combat.input_move_x, 0.0F, scene.combat.input_move_z};
            if (glm::length(move_dir) > 0.001F && unit.stamina > 0.0F) {
                move_dir = glm::normalize(move_dir);
                const float move_speed = std::clamp(entity->combat.move_speed, 1.5F, 8.0F) * scene.weather.movement_speed_multiplier;
                unit.world_position += move_dir * (move_speed * dt_seconds);
                unit.stamina = std::max(0.0F, unit.stamina - (kMoveStaminaDrainPerSecond * dt_seconds));
                unit.action_state = "moving";
            } else if (unit.action_state == "moving") {
                unit.action_state = "idle";
            }

            if (scene.combat.queued_action == "dodge" && unit.dodge_cooldown_seconds <= 0.0F && unit.stamina >= kDodgeStaminaCost) {
                unit.stamina -= kDodgeStaminaCost;
                unit.dodge_cooldown_seconds = 1.0F;
                unit.world_position += glm::vec3(0.0F, 0.0F, 1.6F);
                unit.action_state = "dodging";
                scene.combat.queued_action.clear();
            }

            if (scene.combat.queued_action == "attack" || scene.combat.queued_action == "ranged") {
                CombatUnitState* target_unit = nullptr;
                if (!scene.combat.queued_target.empty()) {
                    try {
                        target_unit = FindUnit(scene, std::stoull(scene.combat.queued_target));
                    } catch (...) {
                        target_unit = nullptr;
                    }
                }
                if (target_unit == nullptr) {
                    target_unit = FindNearestEnemy(scene, unit);
                }
                if (target_unit != nullptr) {
                    Entity* target_entity = FindEntity(scene, target_unit->entity_id);
                    if (target_entity != nullptr) {
                        entity->combat.ranged_enabled = (scene.combat.queued_action == "ranged");
                        (void)TryAttack(scene, unit, *entity, *target_unit, *target_entity);
                    }
                }
                scene.combat.queued_action.clear();
                scene.combat.queued_target.clear();
            }
        } else {
            CombatUnitState* target_unit = FindNearestEnemy(scene, unit);
            if (target_unit != nullptr) {
                Entity* target_entity = FindEntity(scene, target_unit->entity_id);
                if (target_entity != nullptr) {
                    const bool attacked = TryAttack(scene, unit, *entity, *target_unit, *target_entity);
                    if (!attacked) {
                        const glm::vec3 to_target = target_unit->world_position - unit.world_position;
                        if (glm::length(to_target) > 0.001F) {
                            unit.world_position += glm::normalize(to_target) * ((entity->combat.move_speed * 0.8F) * dt_seconds);
                            unit.action_state = "moving";
                        }
                    }
                }
            }
        }

        SyncEntityCombatFromUnit(*entity, unit);
    }

    ResolveIfComplete(scene);
}

}  // namespace CombatSystem
