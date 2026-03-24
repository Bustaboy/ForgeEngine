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
    return node;
}

DialogChoice DialogChoiceFromJson(const json& node, const DialogChoice& fallback) {
    DialogChoice choice = fallback;
    choice.text = node.value("text", choice.text);
    choice.next_node_id = node.value("next_node_id", choice.next_node_id);
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

    return node;
}

DialogComponent DialogComponentFromJson(const json& node, const DialogComponent& fallback) {
    DialogComponent dialog = fallback;
    dialog.nodes.clear();
    dialog.start_node_id = node.value("start_node_id", dialog.start_node_id);
    dialog.active_node_id = node.value("active_node_id", dialog.active_node_id);
    dialog.in_progress = node.value("in_progress", false);

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
    document["build_mode_enabled"] = scene.build_mode_enabled;
    document["active_dialog_npc_id"] = scene.active_dialog_npc_id;
    document["player_inventory"] = InventoryToJson(scene.player_inventory);
    json relationships = json::object();
    for (const auto& [npc_id, relationship] : scene.npc_relationships) {
        relationships[std::to_string(npc_id)] = relationship;
    }
    document["npc_relationships"] = relationships;
    document["directional_light"] = DirectionalLightToJson(scene.directional_light);

    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }

    file << document.dump(4) << '\n';
    return true;
}
