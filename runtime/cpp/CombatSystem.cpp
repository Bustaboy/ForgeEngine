#include "CombatSystem.h"

#include "InventorySystem.h"
#include "Logger.h"
#include "RelationshipSystem.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <sstream>

namespace {
constexpr std::size_t kCombatStateCap = 48U;

Entity* FindEntity(Scene& scene, std::uint64_t entity_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id) {
            return &entity;
        }
    }
    return nullptr;
}

const Entity* FindEntity(const Scene& scene, std::uint64_t entity_id) {
    for (const Entity& entity : scene.entities) {
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

const CombatUnitState* FindUnit(const Scene& scene, std::uint64_t entity_id) {
    for (const CombatUnitState& unit : scene.combat.units) {
        if (unit.entity_id == entity_id) {
            return &unit;
        }
    }
    return nullptr;
}

int ManhattanDistance(std::int32_t ax, std::int32_t ay, std::int32_t bx, std::int32_t by) {
    return std::abs(ax - bx) + std::abs(ay - by);
}

bool TileOccupied(const Scene& scene, std::int32_t x, std::int32_t y, std::uint64_t ignore_entity_id) {
    for (const CombatUnitState& unit : scene.combat.units) {
        if (!unit.alive || unit.entity_id == ignore_entity_id) {
            continue;
        }
        if (unit.grid_x == x && unit.grid_y == y) {
            return true;
        }
    }
    return false;
}

float WeatherMoveCostMultiplier(const Scene& scene) {
    return std::clamp(1.0F / std::max(0.55F, scene.weather.movement_speed_multiplier), 0.8F, 1.8F);
}

float WeatherAccuracyMultiplier(const Scene& scene) {
    const std::string weather = scene.weather.current_weather;
    if (weather == "storm" || weather == "sandstorm") {
        return 0.80F;
    }
    if (weather == "fog" || weather == "rain" || weather == "snow") {
        return 0.88F;
    }
    return 1.0F;
}

float BuildMaxHealth(const Scene& scene, const Entity& entity) {
    const float relation = RelationshipSystem::CompositeScore(scene, entity.id);
    const float baseline = 42.0F + (entity.needs.energy * 0.50F) - (entity.needs.hunger * 0.25F) + (entity.needs.social * 0.12F);
    return std::clamp(baseline + relation * 0.05F, 18.0F, 120.0F);
}

std::uint32_t BuildMaxAp(const Entity& entity) {
    const float baseline = 2.0F + (entity.needs.energy / 33.0F);
    const float rounded = static_cast<float>(std::round(baseline));
    return static_cast<std::uint32_t>(std::clamp(rounded, 2.0F, 5.0F));
}

float BuildInitiative(const Scene& scene, const Entity& entity) {
    const float relation = RelationshipSystem::CompositeScore(scene, entity.id);
    return entity.needs.energy * 0.8F + entity.needs.fun * 0.2F + relation * 0.15F;
}

float BuildAttackPower(const Scene& scene, const Entity& attacker) {
    const float trust = RelationshipSystem::GetDimension(scene, attacker.id, "trust");
    const float respect = RelationshipSystem::GetDimension(scene, attacker.id, "respect");
    return 9.0F + attacker.needs.energy * 0.10F + (trust + respect) * 0.04F;
}

float BuildDefensePower(const Scene& scene, const Entity& defender) {
    const float loyalty = RelationshipSystem::GetDimension(scene, defender.id, "loyalty");
    const float grudge = RelationshipSystem::GetDimension(scene, defender.id, "grudge");
    return 4.0F + defender.needs.social * 0.08F + loyalty * 0.03F - grudge * 0.015F;
}

bool IsTeamAlive(const Scene& scene, std::uint32_t team_id) {
    for (const CombatUnitState& unit : scene.combat.units) {
        if (unit.alive && unit.team_id == team_id) {
            return true;
        }
    }
    return false;
}

bool AdvanceTurn(Scene& scene, std::string& out_message) {
    if (scene.combat.turn_order.empty()) {
        scene.combat.active = false;
        out_message = "Combat ended: no turn order.";
        return false;
    }

    const std::size_t order_size = scene.combat.turn_order.size();
    for (std::size_t step = 0; step < order_size; ++step) {
        scene.combat.active_turn_index = (scene.combat.active_turn_index + 1U) % order_size;
        if (scene.combat.active_turn_index == 0U) {
            ++scene.combat.round_index;
            for (CombatUnitState& unit : scene.combat.units) {
                if (unit.alive) {
                    unit.ap = unit.max_ap;
                }
            }
        }

        const std::uint64_t candidate_id = scene.combat.turn_order[scene.combat.active_turn_index];
        CombatUnitState* candidate = FindUnit(scene, candidate_id);
        if (candidate != nullptr && candidate->alive && candidate->ap > 0) {
            out_message = "Next turn: entity #" + std::to_string(candidate_id) + ".";
            return true;
        }
    }

    out_message = "No combatants can act.";
    return false;
}

void ApplyOutcome(Scene& scene, bool player_team_won) {
    if (player_team_won) {
        scene.settlement.morale = std::clamp(scene.settlement.morale + 6.0F, 0.0F, 100.0F);
        scene.settlement.shared_resources["stockpile"] = std::max(0.0F, scene.settlement.shared_resources["stockpile"] + 4.0F);
        scene.economy.resource_supply["stockpile"] += 1.0F;
    } else {
        scene.settlement.morale = std::clamp(scene.settlement.morale - 8.0F, 0.0F, 100.0F);
        scene.settlement.shared_resources["food"] = std::max(0.0F, scene.settlement.shared_resources["food"] - 3.0F);
        scene.economy.resource_demand["medicine"] += 2.0F;
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
    const bool player_alive = IsTeamAlive(scene, 0);
    const bool hostile_alive = IsTeamAlive(scene, 1);
    if (!player_alive || !hostile_alive) {
        const bool player_won = player_alive && !hostile_alive;
        ApplyOutcome(scene, player_won);
        scene.combat.last_resolution = player_won ? "player_victory" : "player_defeat";
        scene.combat.active = false;
    }
}

std::vector<std::uint64_t> DefaultParticipants(const Scene& scene) {
    std::vector<std::uint64_t> ids{};
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
    return ids;
}

bool ParseGridTarget(const std::string& target, std::int32_t& out_x, std::int32_t& out_y) {
    const std::size_t comma = target.find(',');
    if (comma == std::string::npos) {
        return false;
    }
    try {
        out_x = std::stoi(target.substr(0, comma));
        out_y = std::stoi(target.substr(comma + 1));
        return true;
    } catch (const std::exception&) {
        return false;
    }
}

}  // namespace

namespace CombatSystem {

void EnsureDefaults(Scene& scene) {
    scene.combat.grid_width = std::max(4U, scene.combat.grid_width);
    scene.combat.grid_height = std::max(4U, scene.combat.grid_height);
    if (scene.combat.units.size() > kCombatStateCap) {
        scene.combat.units.resize(kCombatStateCap);
    }
    if (scene.combat.turn_order.size() > kCombatStateCap) {
        scene.combat.turn_order.resize(kCombatStateCap);
    }
    scene.combat.turn_order.erase(
        std::remove_if(scene.combat.turn_order.begin(), scene.combat.turn_order.end(), [&](std::uint64_t entity_id) {
            return FindUnit(scene, entity_id) == nullptr;
        }),
        scene.combat.turn_order.end());
    scene.combat.active_turn_index = std::min(scene.combat.active_turn_index, scene.combat.turn_order.empty() ? 0U : scene.combat.turn_order.size() - 1U);
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
        ids = DefaultParticipants(scene);
    }
    if (ids.size() < 2U) {
        GF_LOG_INFO("Combat start failed: requires at least two units.");
        return false;
    }

    scene.combat = CombatState{};
    scene.combat.active = true;
    scene.combat.grid_width = std::max(4U, grid_width);
    scene.combat.grid_height = std::max(4U, grid_height);
    scene.combat.trigger_source = source;
    scene.combat.round_index = 1U;
    scene.combat.last_action = "start";
    scene.combat.units.reserve(ids.size());

    std::uint32_t slot = 0;
    for (const std::uint64_t id : ids) {
        Entity* entity = FindEntity(scene, id);
        if (entity == nullptr) {
            continue;
        }

        CombatUnitState unit{};
        unit.entity_id = entity->id;
        unit.team_id = (slot == 0U) ? 0U : 1U;
        unit.grid_x = (slot == 0U) ? 1 : static_cast<std::int32_t>(scene.combat.grid_width) - 2;
        unit.grid_y = static_cast<std::int32_t>(1U + (slot % std::max(1U, scene.combat.grid_height - 2U)));
        unit.max_health = BuildMaxHealth(scene, *entity);
        unit.health = unit.max_health;
        unit.max_ap = BuildMaxAp(*entity);
        unit.ap = unit.max_ap;
        unit.initiative = BuildInitiative(scene, *entity);
        unit.alive = true;
        scene.combat.units.push_back(unit);
        ++slot;
    }

    if (scene.combat.units.size() < 2U) {
        scene.combat = CombatState{};
        GF_LOG_INFO("Combat start failed: not enough valid participants.");
        return false;
    }

    std::sort(scene.combat.units.begin(), scene.combat.units.end(), [](const CombatUnitState& a, const CombatUnitState& b) {
        if (std::abs(a.initiative - b.initiative) <= 0.001F) {
            return a.entity_id < b.entity_id;
        }
        return a.initiative > b.initiative;
    });

    scene.combat.turn_order.clear();
    for (const CombatUnitState& unit : scene.combat.units) {
        scene.combat.turn_order.push_back(unit.entity_id);
    }
    scene.combat.active_turn_index = 0U;
    scene.recent_actions.push_back("combat_start:" + source);
    GF_LOG_INFO("Combat started on " + std::to_string(scene.combat.grid_width) + "x" + std::to_string(scene.combat.grid_height) + " grid.");
    return true;
}

bool TryAction(Scene& scene, const std::string& action, const std::string& target, std::string& out_message) {
    EnsureDefaults(scene);
    if (!scene.combat.active || scene.combat.turn_order.empty()) {
        out_message = "No active combat.";
        return false;
    }

    const std::uint64_t actor_id = scene.combat.turn_order[scene.combat.active_turn_index];
    CombatUnitState* actor_unit = FindUnit(scene, actor_id);
    Entity* actor_entity = FindEntity(scene, actor_id);
    if (actor_unit == nullptr || actor_entity == nullptr || !actor_unit->alive) {
        out_message = "Active combatant unavailable.";
        return false;
    }

    if (actor_unit->ap == 0U) {
        AdvanceTurn(scene, out_message);
        return false;
    }

    if (action == "wait") {
        actor_unit->ap = 0U;
        scene.combat.last_action = "wait";
        std::string turn_message;
        AdvanceTurn(scene, turn_message);
        ResolveIfComplete(scene);
        out_message = "Entity #" + std::to_string(actor_id) + " waits. " + turn_message;
        return true;
    }

    if (action == "move") {
        std::int32_t target_x = actor_unit->grid_x;
        std::int32_t target_y = actor_unit->grid_y;
        if (!ParseGridTarget(target, target_x, target_y)) {
            out_message = "Move target must be x,y.";
            return false;
        }

        if (target_x < 0 || target_y < 0 || target_x >= static_cast<std::int32_t>(scene.combat.grid_width) ||
            target_y >= static_cast<std::int32_t>(scene.combat.grid_height)) {
            out_message = "Move target is outside combat grid.";
            return false;
        }

        if (TileOccupied(scene, target_x, target_y, actor_id)) {
            out_message = "Move blocked: tile occupied.";
            return false;
        }

        const int distance = ManhattanDistance(actor_unit->grid_x, actor_unit->grid_y, target_x, target_y);
        const std::uint32_t ap_cost = static_cast<std::uint32_t>(
            std::max(1.0, std::ceil(static_cast<double>(distance) * WeatherMoveCostMultiplier(scene))));
        if (actor_unit->ap < ap_cost) {
            out_message = "Not enough AP to move.";
            return false;
        }

        actor_unit->grid_x = target_x;
        actor_unit->grid_y = target_y;
        actor_unit->ap -= ap_cost;
        scene.combat.last_action = "move";
        out_message = "Entity #" + std::to_string(actor_id) + " moved to (" + std::to_string(target_x) + "," + std::to_string(target_y) + ").";
        if (actor_unit->ap == 0U) {
            std::string turn_message;
            AdvanceTurn(scene, turn_message);
            out_message += " " + turn_message;
        }
        return true;
    }

    if (action == "attack") {
        std::uint64_t target_id = 0;
        try {
            target_id = std::stoull(target);
        } catch (const std::exception&) {
            out_message = "Attack target must be an entity id.";
            return false;
        }

        CombatUnitState* target_unit = FindUnit(scene, target_id);
        Entity* target_entity = FindEntity(scene, target_id);
        if (target_unit == nullptr || target_entity == nullptr || !target_unit->alive) {
            out_message = "Attack target unavailable.";
            return false;
        }
        if (target_unit->team_id == actor_unit->team_id) {
            out_message = "Cannot attack a friendly unit.";
            return false;
        }

        const bool has_ranged = actor_entity->inventory.items.count("ranged_kit") > 0;
        const int max_range = has_ranged ? 3 : 1;
        const int range = ManhattanDistance(actor_unit->grid_x, actor_unit->grid_y, target_unit->grid_x, target_unit->grid_y);
        if (range > max_range) {
            out_message = "Target out of range.";
            return false;
        }

        constexpr std::uint32_t kAttackApCost = 2U;
        if (actor_unit->ap < kAttackApCost) {
            out_message = "Not enough AP to attack.";
            return false;
        }

        const float attack_value = BuildAttackPower(scene, *actor_entity);
        const float defense_value = BuildDefensePower(scene, *target_entity);
        const float accuracy = std::clamp((0.70F + (actor_entity->needs.fun * 0.0015F)) * WeatherAccuracyMultiplier(scene), 0.45F, 0.98F);
        const float deterministic_roll = std::fmod(static_cast<float>((scene.day_count + actor_id + target_id) % 100U) / 100.0F, 1.0F);

        actor_unit->ap -= kAttackApCost;
        if (deterministic_roll > accuracy) {
            scene.combat.last_action = "attack_miss";
            out_message = "Entity #" + std::to_string(actor_id) + " missed #" + std::to_string(target_id) + ".";
        } else {
            const float damage = std::max(1.0F, attack_value - (defense_value * 0.65F));
            target_unit->health = std::max(0.0F, target_unit->health - damage);
            target_unit->alive = target_unit->health > 0.0F;
            scene.combat.last_action = "attack_hit";
            out_message = "Entity #" + std::to_string(actor_id) + " hit #" + std::to_string(target_id) + " for " +
                          std::to_string(static_cast<int>(std::round(damage))) + " dmg.";
            if (!target_unit->alive) {
                out_message += " Target defeated.";
            }
        }

        ResolveIfComplete(scene);
        if (scene.combat.active && actor_unit->ap == 0U) {
            std::string turn_message;
            AdvanceTurn(scene, turn_message);
            out_message += " " + turn_message;
        }
        return true;
    }

    if (action == "use" || action == "item" || action == "use_item") {
        constexpr std::uint32_t kItemApCost = 1U;
        if (actor_unit->ap < kItemApCost) {
            out_message = "Not enough AP to use item.";
            return false;
        }

        const std::string item = target.empty() ? "medkit" : target;
        if (!InventorySystem::RemoveItem(scene.player_inventory, item, 1)) {
            out_message = "Item not available in player inventory: " + item;
            return false;
        }

        actor_unit->health = std::min(actor_unit->max_health, actor_unit->health + 16.0F);
        actor_unit->ap -= kItemApCost;
        scene.combat.last_action = "use_item";
        out_message = "Entity #" + std::to_string(actor_id) + " used " + item + " and recovered health.";
        if (actor_unit->ap == 0U) {
            std::string turn_message;
            AdvanceTurn(scene, turn_message);
            out_message += " " + turn_message;
        }
        return true;
    }

    out_message = "Unknown combat action. Use move|attack|wait|use_item.";
    return false;
}

void Update(Scene& scene, float /*dt_seconds*/) {
    EnsureDefaults(scene);
    if (!scene.combat.active) {
        return;
    }
    ResolveIfComplete(scene);
}

}  // namespace CombatSystem
