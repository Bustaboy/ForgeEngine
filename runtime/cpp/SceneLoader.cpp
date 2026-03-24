#include "SceneLoader.h"

#include "Logger.h"
#include <fstream>
#include <algorithm>
#include <nlohmann/json.hpp>

namespace {
using json = nlohmann::json;

json Vec3ToJson(const glm::vec3& value) {
    return json{{"x", value.x}, {"y", value.y}, {"z", value.z}};
}

glm::vec3 Vec3FromJson(const json& node, const glm::vec3& fallback) {
    glm::vec3 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    value.z = node.value("z", fallback.z);
    return value;
}

json Vec4ToJson(const glm::vec4& value) {
    return json{{"x", value.x}, {"y", value.y}, {"z", value.z}, {"w", value.w}};
}

glm::vec4 Vec4FromJson(const json& node, const glm::vec4& fallback) {
    glm::vec4 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    value.z = node.value("z", fallback.z);
    value.w = node.value("w", fallback.w);
    return value;
}

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

json IVec2ToJson(const glm::ivec2& value) {
    return json{{"x", value.x}, {"y", value.y}};
}

glm::ivec2 IVec2FromJson(const json& node, const glm::ivec2& fallback) {
    glm::ivec2 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    return value;
}

json InventoryToJson(const Inventory& inventory) {
    json node = json::object();
    for (const auto& [item, amount] : inventory.items) {
        node[item] = amount;
    }
    return node;
}

Inventory InventoryFromJson(const json& node, const Inventory& fallback) {
    Inventory inventory = fallback;
    inventory.items.clear();

    if (!node.is_object()) {
        return inventory;
    }

    for (const auto& [item, amount_node] : node.items()) {
        if (!amount_node.is_number_integer()) {
            continue;
        }
        const int amount = amount_node.get<int>();
        if (amount > 0) {
            inventory.items[item] = amount;
        }
    }
    return inventory;
}

json DialogEffectToJson(const DialogEffect& effect) {
    json node = json{
        {"inventory_delta", effect.inventory_delta},
        {"relationship_delta", effect.relationship_delta},
    };
    if (!effect.inventory_item.empty()) {
        node["inventory_item"] = effect.inventory_item;
    }
    return node;
}

DialogEffect DialogEffectFromJson(const json& node, const DialogEffect& fallback) {
    DialogEffect effect = fallback;
    effect.inventory_item = node.value("inventory_item", effect.inventory_item);
    effect.inventory_delta = node.value("inventory_delta", effect.inventory_delta);
    effect.relationship_delta = node.value("relationship_delta", effect.relationship_delta);
    return effect;
}

json DialogChoiceToJson(const DialogChoice& choice) {
    json node = json{
        {"text", choice.text},
        {"effect", DialogEffectToJson(choice.effect)},
    };
    if (!choice.next_node_id.empty()) {
        node["next_node_id"] = choice.next_node_id;
    }
    if (!choice.required_faction_id.empty()) {
        node["required_faction_id"] = choice.required_faction_id;
        node["min_required_reputation"] = choice.min_required_reputation;
    }
    return node;
}

DialogChoice DialogChoiceFromJson(const json& node, const DialogChoice& fallback) {
    DialogChoice choice = fallback;
    choice.text = node.value("text", choice.text);
    choice.next_node_id = node.value("next_node_id", choice.next_node_id);
    choice.required_faction_id = node.value("required_faction_id", choice.required_faction_id);
    choice.min_required_reputation = node.value("min_required_reputation", choice.min_required_reputation);
    if (node.contains("effect") && node["effect"].is_object()) {
        choice.effect = DialogEffectFromJson(node["effect"], choice.effect);
    }
    return choice;
}

json DialogNodeToJson(const DialogNode& dialog_node) {
    json node = json{
        {"id", dialog_node.id},
        {"text", dialog_node.text},
        {"choices", json::array()},
    };
    node["choices"].reserve(dialog_node.choices.size());
    for (const DialogChoice& choice : dialog_node.choices) {
        node["choices"].push_back(DialogChoiceToJson(choice));
    }
    return node;
}

DialogNode DialogNodeFromJson(const json& node, const DialogNode& fallback) {
    DialogNode dialog_node = fallback;
    dialog_node.id = node.value("id", dialog_node.id);
    dialog_node.text = node.value("text", dialog_node.text);
    dialog_node.choices.clear();
    if (node.contains("choices") && node["choices"].is_array()) {
        dialog_node.choices.reserve(node["choices"].size());
        for (const json& choice_node : node["choices"]) {
            if (choice_node.is_object()) {
                dialog_node.choices.push_back(DialogChoiceFromJson(choice_node, DialogChoice{}));
            }
        }
    }
    return dialog_node;
}

json DialogComponentToJson(const DialogComponent& dialog) {
    json node = json{
        {"nodes", json::array()},
        {"in_progress", dialog.in_progress},
    };

    node["nodes"].reserve(dialog.nodes.size());
    for (const DialogNode& dialog_node : dialog.nodes) {
        node["nodes"].push_back(DialogNodeToJson(dialog_node));
    }

    if (!dialog.start_node_id.empty()) {
        node["start_node_id"] = dialog.start_node_id;
    }
    if (!dialog.active_node_id.empty()) {
        node["active_node_id"] = dialog.active_node_id;
    }
    if (!dialog.past_choices.empty()) {
        node["past_choices"] = json::array();
        for (const std::string& choice_text : dialog.past_choices) {
            node["past_choices"].push_back(choice_text);
        }
    }
    if (!dialog.world_events.empty()) {
        node["world_events"] = json::array();
        for (const std::string& world_event : dialog.world_events) {
            node["world_events"].push_back(world_event);
        }
    }
    if (!dialog.reputation_influence.empty()) {
        json influence = json::object();
        for (const auto& [faction_id, value] : dialog.reputation_influence) {
            influence[faction_id] = value;
        }
        node["reputation_influence"] = influence;
    }

    return node;
}

DialogComponent DialogComponentFromJson(const json& node, const DialogComponent& fallback) {
    DialogComponent dialog = fallback;
    dialog.nodes.clear();
    dialog.start_node_id = node.value("start_node_id", dialog.start_node_id);
    dialog.active_node_id = node.value("active_node_id", dialog.active_node_id);
    dialog.in_progress = node.value("in_progress", false);
    dialog.past_choices.clear();
    if (node.contains("past_choices") && node["past_choices"].is_array()) {
        for (const json& choice_node : node["past_choices"]) {
            if (choice_node.is_string()) {
                dialog.past_choices.push_back(choice_node.get<std::string>());
            }
        }
    }
    dialog.world_events.clear();
    if (node.contains("world_events") && node["world_events"].is_array()) {
        for (const json& event_node : node["world_events"]) {
            if (event_node.is_string()) {
                dialog.world_events.push_back(event_node.get<std::string>());
            }
        }
    }
    dialog.reputation_influence.clear();
    if (node.contains("reputation_influence") && node["reputation_influence"].is_object()) {
        for (const auto& [faction_id, value_node] : node["reputation_influence"].items()) {
            if (!value_node.is_number()) {
                continue;
            }
            dialog.reputation_influence[faction_id] = value_node.get<float>();
        }
    }

    if (node.contains("nodes") && node["nodes"].is_array()) {
        dialog.nodes.reserve(node["nodes"].size());
        for (const json& dialog_node : node["nodes"]) {
            if (dialog_node.is_object()) {
                DialogNode parsed = DialogNodeFromJson(dialog_node, DialogNode{});
                if (parsed.IsValid()) {
                    dialog.nodes.push_back(parsed);
                }
            }
        }
    }

    if (!dialog.IsValid()) {
        return DialogComponent{};
    }
    return dialog;
}

json EntityToJson(const Entity& entity) {
    json node = json{
        {"id", entity.id},
        {"transform",
         {{"pos", Vec3ToJson(entity.transform.pos)},
          {"rot", Vec3ToJson(entity.transform.rot)},
          {"scale", Vec3ToJson(entity.transform.scale)}}},
        {"renderable", {{"color", Vec4ToJson(entity.renderable.color)}}},
        {"velocity", Vec3ToJson(entity.velocity)},
    };

    if (entity.buildable.IsValid()) {
        node["buildable"] = json{
            {"type", entity.buildable.type},
            {"grid_size", IVec2ToJson(entity.buildable.grid_size)},
        };
    }
    if (!entity.inventory.items.empty()) {
        node["inventory"] = InventoryToJson(entity.inventory);
    }
    if (!entity.faction.faction_id.empty() || !entity.faction.role.empty()) {
        node["faction"] = json{
            {"faction_id", entity.faction.faction_id},
            {"role", entity.faction.role},
        };
    }
    if (!entity.reputation.values.empty()) {
        json reputation = json::object();
        for (const auto& [faction_id, value] : entity.reputation.values) {
            reputation[faction_id] = value;
        }
        node["reputation"] = reputation;
    }
    if (entity.dialog.IsValid()) {
        node["dialog"] = DialogComponentToJson(entity.dialog);
    }

    return node;
}

Entity EntityFromJson(const json& node) {
    Entity entity{};
    entity.id = node.value("id", entity.id);

    if (node.contains("transform") && node["transform"].is_object()) {
        const json& transform = node["transform"];
        if (transform.contains("pos") && transform["pos"].is_object()) {
            entity.transform.pos = Vec3FromJson(transform["pos"], entity.transform.pos);
        }
        if (transform.contains("rot") && transform["rot"].is_object()) {
            entity.transform.rot = Vec3FromJson(transform["rot"], entity.transform.rot);
        }
        if (transform.contains("scale") && transform["scale"].is_object()) {
            entity.transform.scale = Vec3FromJson(transform["scale"], entity.transform.scale);
        }
    }

    if (node.contains("renderable") && node["renderable"].is_object()) {
        const json& renderable = node["renderable"];
        if (renderable.contains("color") && renderable["color"].is_object()) {
            entity.renderable.color = Vec4FromJson(renderable["color"], entity.renderable.color);
        }
    }

    if (node.contains("velocity") && node["velocity"].is_object()) {
        entity.velocity = Vec3FromJson(node["velocity"], entity.velocity);
    }

    if (node.contains("buildable") && node["buildable"].is_object()) {
        const json& buildable = node["buildable"];
        entity.buildable.type = buildable.value("type", entity.buildable.type);
        if (buildable.contains("grid_size") && buildable["grid_size"].is_object()) {
            entity.buildable.grid_size = IVec2FromJson(buildable["grid_size"], entity.buildable.grid_size);
        }
    }
    if (node.contains("inventory")) {
        entity.inventory = InventoryFromJson(node["inventory"], entity.inventory);
    }
    if (node.contains("faction") && node["faction"].is_object()) {
        const json& faction = node["faction"];
        entity.faction.faction_id = faction.value("faction_id", entity.faction.faction_id);
        entity.faction.role = faction.value("role", entity.faction.role);
    }
    if (node.contains("reputation") && node["reputation"].is_object()) {
        entity.reputation.values.clear();
        for (const auto& [faction_id, value_node] : node["reputation"].items()) {
            if (!value_node.is_number()) {
                continue;
            }
            entity.reputation.values[faction_id] = value_node.get<float>();
        }
    }
    if (node.contains("dialog") && node["dialog"].is_object()) {
        entity.dialog = DialogComponentFromJson(node["dialog"], entity.dialog);
    }

    return entity;
}

json DirectionalLightToJson(const DirectionalLight& light) {
    return json{
        {"direction", Vec3ToJson(light.direction)},
        {"color", Vec3ToJson(light.color)},
        {"intensity", light.intensity},
    };
}

json CoCreatorMutationToJson(const CoCreatorQueuedMutation& mutation) {
    return json{
        {"suggestion_id", mutation.suggestion_id},
        {"title", mutation.title},
        {"why_this_fits", mutation.why_this_fits},
        {"mutation_json", mutation.mutation_json},
    };
}

CoCreatorQueuedMutation CoCreatorMutationFromJson(const json& node) {
    CoCreatorQueuedMutation mutation{};
    mutation.suggestion_id = node.value("suggestion_id", mutation.suggestion_id);
    mutation.title = node.value("title", mutation.title);
    mutation.why_this_fits = node.value("why_this_fits", mutation.why_this_fits);
    mutation.mutation_json = node.value("mutation_json", mutation.mutation_json);
    return mutation;
}

json FactionDefinitionToJson(const FactionDefinition& faction) {
    json relationships = json::object();
    for (const auto& [other_faction, value] : faction.relationships) {
        relationships[other_faction] = value;
    }
    return json{
        {"id", faction.id},
        {"display_name", faction.display_name},
        {"category", faction.category},
        {"biome_hint", faction.biome_hint},
        {"style_hint", faction.style_hint},
        {"min_reputation_to_build", faction.min_reputation_to_build},
        {"dialog_gate_reputation", faction.dialog_gate_reputation},
        {"trade_bonus_threshold", faction.trade_bonus_threshold},
        {"relationships", relationships},
    };
}

FactionDefinition FactionDefinitionFromJson(const json& node, const std::string& fallback_id) {
    FactionDefinition faction{};
    faction.id = node.value("id", fallback_id);
    faction.display_name = node.value("display_name", faction.id);
    faction.category = node.value("category", faction.category);
    faction.biome_hint = node.value("biome_hint", faction.biome_hint);
    faction.style_hint = node.value("style_hint", faction.style_hint);
    faction.min_reputation_to_build = node.value("min_reputation_to_build", faction.min_reputation_to_build);
    faction.dialog_gate_reputation = node.value("dialog_gate_reputation", faction.dialog_gate_reputation);
    faction.trade_bonus_threshold = node.value("trade_bonus_threshold", faction.trade_bonus_threshold);
    if (node.contains("relationships") && node["relationships"].is_object()) {
        for (const auto& [other_faction, value_node] : node["relationships"].items()) {
            if (!value_node.is_number()) {
                continue;
            }
            faction.relationships[other_faction] = value_node.get<float>();
        }
    }
    return faction;
}

json EconomyRouteToJson(const EconomyTradeRoute& route) {
    return json{
        {"route_id", route.route_id},
        {"from_settlement", route.from_settlement},
        {"to_settlement", route.to_settlement},
        {"resource", route.resource},
        {"units_per_tick", route.units_per_tick},
        {"risk", route.risk},
        {"disruption", route.disruption},
        {"last_trader_id", route.last_trader_id},
        {"trader_deaths", route.trader_deaths},
    };
}

EconomyTradeRoute EconomyRouteFromJson(const json& node) {
    EconomyTradeRoute route{};
    route.route_id = node.value("route_id", route.route_id);
    route.from_settlement = node.value("from_settlement", route.from_settlement);
    route.to_settlement = node.value("to_settlement", route.to_settlement);
    route.resource = node.value("resource", route.resource);
    route.units_per_tick = std::max(1, node.value("units_per_tick", route.units_per_tick));
    route.risk = node.value("risk", route.risk);
    route.disruption = node.value("disruption", route.disruption);
    route.last_trader_id = node.value("last_trader_id", route.last_trader_id);
    route.trader_deaths = node.value("trader_deaths", route.trader_deaths);
    return route;
}

json FloatMapToJson(const std::map<std::string, float>& values) {
    json node = json::object();
    for (const auto& [key, value] : values) {
        node[key] = value;
    }
    return node;
}

void FloatMapFromJson(const json& node, std::map<std::string, float>& values) {
    values.clear();
    if (!node.is_object()) {
        return;
    }

    for (const auto& [key, value_node] : node.items()) {
        if (!value_node.is_number()) {
            continue;
        }
        values[key] = value_node.get<float>();
    }
}
}  // namespace

bool SceneLoader::Load(const std::string& path, Scene& scene) {
    std::ifstream file(path);
    if (!file.is_open()) {
        return false;
    }

    json document;
    try {
        file >> document;
    } catch (const std::exception& exception) {
        GF_LOG_INFO("Scene JSON parse failed: " + std::string(exception.what()));
        return false;
    }

    if (!document.contains("entities") || !document["entities"].is_array()) {
        return false;
    }

    scene.entities.clear();
    scene.entities.reserve(document["entities"].size());

    for (const json& entity_node : document["entities"]) {
        if (entity_node.is_object()) {
            scene.entities.push_back(EntityFromJson(entity_node));
        }
    }

    scene.elapsed_seconds = document.value("elapsed_seconds", 0.0F);
    scene.day_progress = Clamp01(document.value("day_progress", scene.day_progress));
    scene.day_cycle_speed = std::max(0.0F, document.value("day_cycle_speed", scene.day_cycle_speed));
    scene.day_count = std::max(1U, document.value("day_count", scene.day_count));
    scene.biome = document.value("biome", scene.biome);
    scene.world_style_guide = document.value("world_style_guide", scene.world_style_guide);
    scene.build_mode_enabled = document.value("build_mode_enabled", scene.build_mode_enabled);
    scene.active_dialog_npc_id = document.value("active_dialog_npc_id", 0ULL);
    if (document.contains("player_inventory")) {
        scene.player_inventory = InventoryFromJson(document["player_inventory"], scene.player_inventory);
    }
    scene.npc_relationships.clear();
    if (document.contains("npc_relationships") && document["npc_relationships"].is_object()) {
        for (const auto& [npc_id_key, relationship_node] : document["npc_relationships"].items()) {
            if (!relationship_node.is_number()) {
                continue;
            }
            try {
                const std::uint64_t npc_id = std::stoull(npc_id_key);
                scene.npc_relationships[npc_id] = relationship_node.get<float>();
            } catch (const std::exception&) {
                continue;
            }
        }
    }
    scene.factions.clear();
    if (document.contains("factions") && document["factions"].is_object()) {
        for (const auto& [faction_id, faction_node] : document["factions"].items()) {
            if (!faction_node.is_object()) {
                continue;
            }
            scene.factions[faction_id] = FactionDefinitionFromJson(faction_node, faction_id);
        }
    }
    scene.player_reputation.clear();
    if (document.contains("player_reputation") && document["player_reputation"].is_object()) {
        for (const auto& [faction_id, reputation_node] : document["player_reputation"].items()) {
            if (!reputation_node.is_number()) {
                continue;
            }
            scene.player_reputation[faction_id] = reputation_node.get<float>();
        }
    }

    scene.economy = EconomyState{};
    if (document.contains("economy") && document["economy"].is_object()) {
        const json& economy = document["economy"];
        FloatMapFromJson(economy.value("resource_supply", json::object()), scene.economy.resource_supply);
        FloatMapFromJson(economy.value("resource_demand", json::object()), scene.economy.resource_demand);
        FloatMapFromJson(economy.value("base_prices", json::object()), scene.economy.base_prices);
        FloatMapFromJson(economy.value("price_table", json::object()), scene.economy.price_table);
        scene.economy.tick_interval_seconds = std::max(0.1F, economy.value("tick_interval_seconds", scene.economy.tick_interval_seconds));
        scene.economy.accumulated_tick_seconds = std::max(0.0F, economy.value("accumulated_tick_seconds", scene.economy.accumulated_tick_seconds));
        if (economy.contains("trade_routes") && economy["trade_routes"].is_array()) {
            for (const json& route_node : economy["trade_routes"]) {
                if (!route_node.is_object()) {
                    continue;
                }
                EconomyTradeRoute route = EconomyRouteFromJson(route_node);
                if (route.IsValid()) {
                    scene.economy.trade_routes.push_back(route);
                }
            }
        }
    }


    if (document.contains("directional_light") && document["directional_light"].is_object()) {
        const json& light = document["directional_light"];
        if (light.contains("direction") && light["direction"].is_object()) {
            scene.directional_light.direction = Vec3FromJson(light["direction"], scene.directional_light.direction);
        }
        if (light.contains("color") && light["color"].is_object()) {
            scene.directional_light.color = Vec3FromJson(light["color"], scene.directional_light.color);
        }
        scene.directional_light.intensity = light.value("intensity", scene.directional_light.intensity);
    }

    scene.recent_actions.clear();
    if (document.contains("recent_actions") && document["recent_actions"].is_array()) {
        for (const json& action_node : document["recent_actions"]) {
            if (action_node.is_string()) {
                scene.recent_actions.push_back(action_node.get<std::string>());
            }
        }
    }

    scene.co_creator_queue.clear();
    if (document.contains("co_creator_queue") && document["co_creator_queue"].is_array()) {
        for (const json& mutation_node : document["co_creator_queue"]) {
            if (!mutation_node.is_object()) {
                continue;
            }
            scene.co_creator_queue.push_back(CoCreatorMutationFromJson(mutation_node));
        }
    }

    scene.Update(0.0F);
    return true;
}

bool SceneLoader::Save(const std::string& path, const Scene& scene) {
    json document;
    document["entities"] = json::array();
    document["entities"].reserve(scene.entities.size());

    for (const Entity& entity : scene.entities) {
        document["entities"].push_back(EntityToJson(entity));
    }
    document["elapsed_seconds"] = scene.elapsed_seconds;
    document["day_progress"] = scene.day_progress;
    document["day_cycle_speed"] = scene.day_cycle_speed;
    document["day_count"] = scene.day_count;
    document["biome"] = scene.biome;
    document["world_style_guide"] = scene.world_style_guide;
    document["build_mode_enabled"] = scene.build_mode_enabled;
    document["active_dialog_npc_id"] = scene.active_dialog_npc_id;
    document["player_inventory"] = InventoryToJson(scene.player_inventory);
    json relationships = json::object();
    for (const auto& [npc_id, relationship] : scene.npc_relationships) {
        relationships[std::to_string(npc_id)] = relationship;
    }
    document["npc_relationships"] = relationships;
    json factions = json::object();
    for (const auto& [faction_id, faction] : scene.factions) {
        factions[faction_id] = FactionDefinitionToJson(faction);
    }
    document["factions"] = factions;
    json player_reputation = json::object();
    for (const auto& [faction_id, value] : scene.player_reputation) {
        player_reputation[faction_id] = value;
    }
    document["player_reputation"] = player_reputation;
    json economy = json::object();
    economy["resource_supply"] = FloatMapToJson(scene.economy.resource_supply);
    economy["resource_demand"] = FloatMapToJson(scene.economy.resource_demand);
    economy["base_prices"] = FloatMapToJson(scene.economy.base_prices);
    economy["price_table"] = FloatMapToJson(scene.economy.price_table);
    economy["tick_interval_seconds"] = scene.economy.tick_interval_seconds;
    economy["accumulated_tick_seconds"] = scene.economy.accumulated_tick_seconds;
    economy["trade_routes"] = json::array();
    for (const EconomyTradeRoute& route : scene.economy.trade_routes) {
        economy["trade_routes"].push_back(EconomyRouteToJson(route));
    }
    document["economy"] = economy;
    document["directional_light"] = DirectionalLightToJson(scene.directional_light);
    document["recent_actions"] = json::array();
    for (const std::string& action : scene.recent_actions) {
        document["recent_actions"].push_back(action);
    }
    document["co_creator_queue"] = json::array();
    for (const CoCreatorQueuedMutation& mutation : scene.co_creator_queue) {
        document["co_creator_queue"].push_back(CoCreatorMutationToJson(mutation));
    }

    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }

    file << document.dump(4) << '\n';
    return true;
}
