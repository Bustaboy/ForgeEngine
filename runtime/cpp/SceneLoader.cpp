#include "SceneLoader.h"

#include "Logger.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <set>
#include <sstream>
#include <vector>
#include <nlohmann/json.hpp>

namespace {
using json = nlohmann::json;

std::string TrimCopy(const std::string& value) {
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) {
        return "";
    }
    const auto last = value.find_last_not_of(" \t\r\n");
    return value.substr(first, last - first + 1);
}

bool ReadJsonIfObject(const std::filesystem::path& path, json& out) {
    if (path.empty() || !std::filesystem::exists(path) || !std::filesystem::is_regular_file(path)) {
        return false;
    }
    std::ifstream file(path);
    if (!file.is_open()) {
        return false;
    }
    try {
        file >> out;
        return out.is_object();
    } catch (const std::exception&) {
        return false;
    }
}

std::string ResolveFreeWillPathFromModelsJson(const std::filesystem::path& models_json_path) {
    json models_json;
    if (!ReadJsonIfObject(models_json_path, models_json)) {
        return "";
    }
    if (!models_json.contains("models") || !models_json["models"].is_object()) {
        return "";
    }
    const json& freewill = models_json["models"].value("freewill", json::object());
    if (!freewill.is_object()) {
        return "";
    }
    return TrimCopy(freewill.value("path", std::string{}));
}

std::string ResolveFreeWillPathFromArtBible(const std::filesystem::path& art_bible_path) {
    json art_bible;
    if (!ReadJsonIfObject(art_bible_path, art_bible)) {
        return "";
    }
    if (art_bible.contains("free_will") && art_bible["free_will"].is_object()) {
        const std::string direct = TrimCopy(art_bible["free_will"].value("model_path", std::string{}));
        if (!direct.empty()) {
            return direct;
        }
    }
    if (art_bible.contains("models") && art_bible["models"].is_object()) {
        const json& freewill = art_bible["models"].value("freewill", json::object());
        if (freewill.is_object()) {
            const std::string path = TrimCopy(freewill.value("path", std::string{}));
            if (!path.empty()) {
                return path;
            }
        }
    }
    return "";
}

std::string ResolveFreeWillModelPath(const std::string& scene_path) {
    std::vector<std::filesystem::path> bases;
    if (!scene_path.empty()) {
        const auto absolute_scene = std::filesystem::absolute(std::filesystem::path(scene_path));
        bases.push_back(absolute_scene.parent_path());
        bases.push_back(absolute_scene.parent_path().parent_path());
    }
    bases.push_back(std::filesystem::current_path());

    for (const auto& base : bases) {
        if (base.empty()) {
            continue;
        }
        const std::string models_path = ResolveFreeWillPathFromModelsJson(base / "models.json");
        if (!models_path.empty()) {
            return models_path;
        }
        const std::string art_bible_path = ResolveFreeWillPathFromArtBible(base / "art_bible.json");
        if (!art_bible_path.empty()) {
            return art_bible_path;
        }
    }
    return "";
}

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

json Vec2ToJson(const glm::vec2& value) {
    return json{{"x", value.x}, {"y", value.y}};
}

glm::vec2 Vec2FromJson(const json& node, const glm::vec2& fallback) {
    glm::vec2 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    return value;
}

json Vec4ToJson(const glm::vec4& value) {
    return json{{"x", value.x}, {"y", value.y}, {"z", value.z}, {"w", value.w}};
}

json SceneSprite2DToJson(const SceneSprite2D& sprite) {
    json node = json{
        {"asset_id", sprite.asset_id},
        {"position", Vec2ToJson(sprite.position)},
        {"size", Vec2ToJson(sprite.size)},
        {"tint", Vec4ToJson(sprite.tint)},
        {"rotation_radians", sprite.rotation_radians},
        {"layer", sprite.layer},
    };
    if (!sprite.entity_type.empty()) {
        node["entity_type"] = sprite.entity_type;
    }
    return node;
}

SceneSprite2D SceneSprite2DFromJson(const json& node, const SceneSprite2D& fallback) {
    SceneSprite2D sprite = fallback;
    sprite.asset_id = node.value("asset_id", sprite.asset_id);
    sprite.entity_type = node.value("entity_type", sprite.entity_type);
    if (node.contains("position") && node["position"].is_object()) {
        sprite.position = Vec2FromJson(node["position"], sprite.position);
    }
    if (node.contains("size") && node["size"].is_object()) {
        sprite.size = Vec2FromJson(node["size"], sprite.size);
    }
    if (node.contains("tint") && node["tint"].is_object()) {
        sprite.tint = Vec4FromJson(node["tint"], sprite.tint);
    }
    sprite.rotation_radians = node.value("rotation_radians", sprite.rotation_radians);
    sprite.layer = node.value("layer", sprite.layer);
    return sprite;
}

json SceneTilemap2DToJson(const SceneTilemap2D& tilemap) {
    json node = json{
        {"id", tilemap.id},
        {"tileset_asset_id", tilemap.tileset_asset_id},
        {"origin", Vec2ToJson(tilemap.origin)},
        {"tile_size", Vec2ToJson(tilemap.tile_size)},
        {"columns", tilemap.columns},
        {"rows", tilemap.rows},
        {"layer", tilemap.layer},
        {"tiles", json::array()},
    };
    for (const int value : tilemap.tiles) {
        node["tiles"].push_back(value);
    }
    return node;
}

SceneTilemap2D SceneTilemap2DFromJson(const json& node, const SceneTilemap2D& fallback) {
    SceneTilemap2D tilemap = fallback;
    tilemap.id = node.value("id", tilemap.id);
    tilemap.tileset_asset_id = node.value("tileset_asset_id", tilemap.tileset_asset_id);
    if (node.contains("origin") && node["origin"].is_object()) {
        tilemap.origin = Vec2FromJson(node["origin"], tilemap.origin);
    }
    if (node.contains("tile_size") && node["tile_size"].is_object()) {
        tilemap.tile_size = Vec2FromJson(node["tile_size"], tilemap.tile_size);
    }
    tilemap.columns = node.value("columns", tilemap.columns);
    tilemap.rows = node.value("rows", tilemap.rows);
    tilemap.layer = node.value("layer", tilemap.layer);
    tilemap.tiles.clear();
    if (node.contains("tiles") && node["tiles"].is_array()) {
        for (const json& tile : node["tiles"]) {
            if (tile.is_number_integer()) {
                tilemap.tiles.push_back(tile.get<int>());
            }
        }
    }
    return tilemap;
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

json ScheduleEntryToJson(const ScheduleEntry& entry) {
    return json{
        {"start_minute", entry.start_minute},
        {"end_minute", entry.end_minute},
        {"activity", entry.activity},
        {"location", entry.location},
    };
}

ScheduleEntry ScheduleEntryFromJson(const json& node, const ScheduleEntry& fallback) {
    ScheduleEntry entry = fallback;
    entry.start_minute = node.value("start_minute", entry.start_minute);
    entry.end_minute = node.value("end_minute", entry.end_minute);
    entry.activity = node.value("activity", entry.activity);
    entry.location = node.value("location", entry.location);
    return entry;
}

json ScheduleComponentToJson(const ScheduleComponent& schedule) {
    json node = json{
        {"home_entity_id", schedule.home_entity_id},
        {"workplace_entity_id", schedule.workplace_entity_id},
        {"home_position", Vec3ToJson(schedule.home_position)},
        {"workplace_position", Vec3ToJson(schedule.workplace_position)},
        {"job_id", schedule.job_id},
        {"current_activity", schedule.current_activity},
        {"current_location", schedule.current_location},
        {"daily_schedule", json::array()},
    };
    for (const ScheduleEntry& entry : schedule.daily_schedule) {
        node["daily_schedule"].push_back(ScheduleEntryToJson(entry));
    }
    return node;
}

ScheduleComponent ScheduleComponentFromJson(const json& node, const ScheduleComponent& fallback) {
    ScheduleComponent schedule = fallback;
    schedule.home_entity_id = node.value("home_entity_id", schedule.home_entity_id);
    schedule.workplace_entity_id = node.value("workplace_entity_id", schedule.workplace_entity_id);
    if (node.contains("home_position") && node["home_position"].is_object()) {
        schedule.home_position = Vec3FromJson(node["home_position"], schedule.home_position);
    }
    if (node.contains("workplace_position") && node["workplace_position"].is_object()) {
        schedule.workplace_position = Vec3FromJson(node["workplace_position"], schedule.workplace_position);
    }
    schedule.job_id = node.value("job_id", schedule.job_id);
    schedule.current_activity = node.value("current_activity", schedule.current_activity);
    schedule.current_location = node.value("current_location", schedule.current_location);
    schedule.daily_schedule.clear();
    if (node.contains("daily_schedule") && node["daily_schedule"].is_array()) {
        for (const json& entry_node : node["daily_schedule"]) {
            if (entry_node.is_object()) {
                schedule.daily_schedule.push_back(ScheduleEntryFromJson(entry_node, ScheduleEntry{}));
            }
        }
    }
    return schedule;
}

json NeedsToJson(const NeedsComponent& needs) {
    return json{
        {"hunger", needs.hunger},
        {"energy", needs.energy},
        {"social", needs.social},
        {"fun", needs.fun},
    };
}

NeedsComponent NeedsFromJson(const json& node, const NeedsComponent& fallback) {
    NeedsComponent needs = fallback;
    needs.hunger = Clamp01(node.value("hunger", needs.hunger) / 100.0F) * 100.0F;
    needs.energy = Clamp01(node.value("energy", needs.energy) / 100.0F) * 100.0F;
    needs.social = Clamp01(node.value("social", needs.social) / 100.0F) * 100.0F;
    needs.fun = Clamp01(node.value("fun", needs.fun) / 100.0F) * 100.0F;
    return needs;
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
    if (!choice.required_relationship_dimension.empty()) {
        node["required_relationship_dimension"] = choice.required_relationship_dimension;
        node["min_required_relationship"] = choice.min_required_relationship;
    }
    return node;
}

DialogChoice DialogChoiceFromJson(const json& node, const DialogChoice& fallback) {
    DialogChoice choice = fallback;
    choice.text = node.value("text", choice.text);
    choice.next_node_id = node.value("next_node_id", choice.next_node_id);
    choice.required_faction_id = node.value("required_faction_id", choice.required_faction_id);
    choice.min_required_reputation = node.value("min_required_reputation", choice.min_required_reputation);
    choice.required_relationship_dimension = node.value("required_relationship_dimension", choice.required_relationship_dimension);
    choice.min_required_relationship = node.value("min_required_relationship", choice.min_required_relationship);
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

json VoiceProfileToJson(const VoiceProfileComponent& voice_profile) {
    return json{
        {"profile_id", voice_profile.profile_id},
        {"gender", voice_profile.gender},
        {"build", voice_profile.build},
        {"personality", voice_profile.personality},
        {"style", voice_profile.style},
        {"base_voice_id", voice_profile.base_voice_id},
        {"pitch", voice_profile.pitch},
        {"rate", voice_profile.rate},
        {"volume", voice_profile.volume},
    };
}

VoiceProfileComponent VoiceProfileFromJson(const json& node, const VoiceProfileComponent& fallback) {
    VoiceProfileComponent profile = fallback;
    profile.profile_id = node.value("profile_id", profile.profile_id);
    profile.gender = node.value("gender", profile.gender);
    profile.build = node.value("build", profile.build);
    profile.personality = node.value("personality", profile.personality);
    profile.style = node.value("style", profile.style);
    profile.base_voice_id = node.value("base_voice_id", profile.base_voice_id);
    profile.pitch = node.value("pitch", profile.pitch);
    profile.rate = node.value("rate", profile.rate);
    profile.volume = node.value("volume", profile.volume);
    return profile;
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
        {"animation", {{"motion_phase", entity.animation.motion_phase}, {"left_foot_offset", entity.animation.left_foot_offset}, {"right_foot_offset", entity.animation.right_foot_offset}}},
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
    if (std::abs(entity.relationship.trust_bias) > 0.0001F ||
        std::abs(entity.relationship.respect_bias) > 0.0001F ||
        std::abs(entity.relationship.loyalty_bias) > 0.0001F ||
        std::abs(entity.relationship.grudge_sensitivity - 1.0F) > 0.0001F ||
        std::abs(entity.relationship.debt_sensitivity - 1.0F) > 0.0001F) {
        node["relationship"] = json{
            {"trust_bias", entity.relationship.trust_bias},
            {"respect_bias", entity.relationship.respect_bias},
            {"loyalty_bias", entity.relationship.loyalty_bias},
            {"grudge_sensitivity", entity.relationship.grudge_sensitivity},
            {"debt_sensitivity", entity.relationship.debt_sensitivity},
        };
    }
    if (entity.dialog.IsValid()) {
        node["dialog"] = DialogComponentToJson(entity.dialog);
    }
    if (entity.mesh.IsValid()) {
        node["mesh"] = json{
            {"source", entity.mesh.source},
            {"primitive_index", entity.mesh.primitive_index},
            {"bounds_min", Vec3ToJson(entity.mesh.bounds_min)},
            {"bounds_max", Vec3ToJson(entity.mesh.bounds_max)},
        };
    }
    node["voice_profile"] = VoiceProfileToJson(entity.voice_profile);
    node["schedule"] = ScheduleComponentToJson(entity.schedule);
    node["needs"] = NeedsToJson(entity.needs);

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
    if (node.contains("animation") && node["animation"].is_object()) {
        const json& animation = node["animation"];
        entity.animation.motion_phase = animation.value("motion_phase", entity.animation.motion_phase);
        entity.animation.left_foot_offset = animation.value("left_foot_offset", entity.animation.left_foot_offset);
        entity.animation.right_foot_offset = animation.value("right_foot_offset", entity.animation.right_foot_offset);
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
    if (node.contains("relationship") && node["relationship"].is_object()) {
        const json& relationship = node["relationship"];
        entity.relationship.trust_bias = relationship.value("trust_bias", entity.relationship.trust_bias);
        entity.relationship.respect_bias = relationship.value("respect_bias", entity.relationship.respect_bias);
        entity.relationship.loyalty_bias = relationship.value("loyalty_bias", entity.relationship.loyalty_bias);
        entity.relationship.grudge_sensitivity = relationship.value("grudge_sensitivity", entity.relationship.grudge_sensitivity);
        entity.relationship.debt_sensitivity = relationship.value("debt_sensitivity", entity.relationship.debt_sensitivity);
    }
    if (node.contains("dialog") && node["dialog"].is_object()) {
        entity.dialog = DialogComponentFromJson(node["dialog"], entity.dialog);
    }
    if (node.contains("mesh") && node["mesh"].is_object()) {
        const json& mesh = node["mesh"];
        entity.mesh.source = mesh.value("source", entity.mesh.source);
        entity.mesh.primitive_index = mesh.value("primitive_index", entity.mesh.primitive_index);
        if (mesh.contains("bounds_min") && mesh["bounds_min"].is_object()) {
            entity.mesh.bounds_min = Vec3FromJson(mesh["bounds_min"], entity.mesh.bounds_min);
        }
        if (mesh.contains("bounds_max") && mesh["bounds_max"].is_object()) {
            entity.mesh.bounds_max = Vec3FromJson(mesh["bounds_max"], entity.mesh.bounds_max);
        }
    }
    if (node.contains("voice_profile") && node["voice_profile"].is_object()) {
        entity.voice_profile = VoiceProfileFromJson(node["voice_profile"], entity.voice_profile);
    }
    if (node.contains("schedule") && node["schedule"].is_object()) {
        entity.schedule = ScheduleComponentFromJson(node["schedule"], entity.schedule);
    }
    if (node.contains("needs") && node["needs"].is_object()) {
        entity.needs = NeedsFromJson(node["needs"], entity.needs);
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

json WeatherStateToJson(const WeatherState& weather) {
    return json{
        {"current_weather", weather.current_weather},
        {"target_weather", weather.target_weather},
        {"transition_progress", weather.transition_progress},
        {"intensity", weather.intensity},
        {"seconds_until_next_transition", weather.seconds_until_next_transition},
        {"transition_duration_seconds", weather.transition_duration_seconds},
        {"movement_speed_multiplier", weather.movement_speed_multiplier},
        {"economy_supply_multiplier", weather.economy_supply_multiplier},
        {"economy_demand_multiplier", weather.economy_demand_multiplier},
        {"relationship_delta_per_day", weather.relationship_delta_per_day},
        {"light_multiplier", weather.light_multiplier},
        {"dialog_tone", weather.dialog_tone},
        {"last_relationship_day_applied", weather.last_relationship_day_applied},
    };
}

json WorldTimeToJson(const WorldTime& world_time) {
    return json{
        {"elapsed_seconds", world_time.elapsed_seconds},
        {"day_progress", world_time.day_progress},
        {"day_cycle_speed", world_time.day_cycle_speed},
        {"day_count", world_time.day_count},
        {"minutes_per_day", world_time.minutes_per_day},
    };
}

WorldTime WorldTimeFromJson(const json& node, const WorldTime& fallback) {
    WorldTime world_time = fallback;
    world_time.elapsed_seconds = std::max(0.0F, node.value("elapsed_seconds", world_time.elapsed_seconds));
    world_time.day_progress = Clamp01(node.value("day_progress", world_time.day_progress));
    world_time.day_cycle_speed = std::max(0.0F, node.value("day_cycle_speed", world_time.day_cycle_speed));
    world_time.day_count = std::max(1U, node.value("day_count", world_time.day_count));
    world_time.minutes_per_day = std::max(60U, node.value("minutes_per_day", world_time.minutes_per_day));
    return world_time;
}

WeatherState WeatherStateFromJson(const json& node, const WeatherState& fallback) {
    WeatherState weather = fallback;
    weather.current_weather = node.value("current_weather", weather.current_weather);
    weather.target_weather = node.value("target_weather", weather.target_weather);
    weather.transition_progress = Clamp01(node.value("transition_progress", weather.transition_progress));
    weather.intensity = Clamp01(node.value("intensity", weather.intensity));
    weather.seconds_until_next_transition = std::max(5.0F, node.value("seconds_until_next_transition", weather.seconds_until_next_transition));
    weather.transition_duration_seconds = std::max(2.0F, node.value("transition_duration_seconds", weather.transition_duration_seconds));
    weather.movement_speed_multiplier = node.value("movement_speed_multiplier", weather.movement_speed_multiplier);
    weather.economy_supply_multiplier = node.value("economy_supply_multiplier", weather.economy_supply_multiplier);
    weather.economy_demand_multiplier = node.value("economy_demand_multiplier", weather.economy_demand_multiplier);
    weather.relationship_delta_per_day = node.value("relationship_delta_per_day", weather.relationship_delta_per_day);
    weather.light_multiplier = node.value("light_multiplier", weather.light_multiplier);
    weather.dialog_tone = node.value("dialog_tone", weather.dialog_tone);
    weather.last_relationship_day_applied = std::max(1U, node.value("last_relationship_day_applied", weather.last_relationship_day_applied));
    return weather;
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

json RelationshipMemoryToJson(const RelationshipMemory& memory) {
    return json{
        {"event_id", memory.event_id},
        {"summary", memory.summary},
        {"trust_delta", memory.trust_delta},
        {"respect_delta", memory.respect_delta},
        {"grudge_delta", memory.grudge_delta},
        {"debt_delta", memory.debt_delta},
        {"loyalty_delta", memory.loyalty_delta},
        {"day_recorded", memory.day_recorded},
        {"decay_per_day", memory.decay_per_day},
    };
}

RelationshipMemory RelationshipMemoryFromJson(const json& node) {
    RelationshipMemory memory{};
    memory.event_id = node.value("event_id", memory.event_id);
    memory.summary = node.value("summary", memory.summary);
    memory.trust_delta = node.value("trust_delta", memory.trust_delta);
    memory.respect_delta = node.value("respect_delta", memory.respect_delta);
    memory.grudge_delta = node.value("grudge_delta", memory.grudge_delta);
    memory.debt_delta = node.value("debt_delta", memory.debt_delta);
    memory.loyalty_delta = node.value("loyalty_delta", memory.loyalty_delta);
    memory.day_recorded = node.value("day_recorded", memory.day_recorded);
    memory.decay_per_day = node.value("decay_per_day", memory.decay_per_day);
    return memory;
}

json RelationshipProfileToJson(const RelationshipProfile& profile) {
    json node = json{
        {"trust", profile.trust},
        {"respect", profile.respect},
        {"grudge", profile.grudge},
        {"debt", profile.debt},
        {"loyalty", profile.loyalty},
        {"last_interaction_day", profile.last_interaction_day},
        {"memories", json::array()},
    };
    for (const RelationshipMemory& memory : profile.memories) {
        node["memories"].push_back(RelationshipMemoryToJson(memory));
    }
    return node;
}

RelationshipProfile RelationshipProfileFromJson(const json& node) {
    RelationshipProfile profile{};
    profile.trust = node.value("trust", profile.trust);
    profile.respect = node.value("respect", profile.respect);
    profile.grudge = node.value("grudge", profile.grudge);
    profile.debt = node.value("debt", profile.debt);
    profile.loyalty = node.value("loyalty", profile.loyalty);
    profile.last_interaction_day = node.value("last_interaction_day", profile.last_interaction_day);
    if (node.contains("memories") && node["memories"].is_array()) {
        for (const json& memory_node : node["memories"]) {
            if (memory_node.is_object()) {
                profile.memories.push_back(RelationshipMemoryFromJson(memory_node));
            }
        }
    }
    return profile;
}


json NavPathNodeToJson(const NavPathNode& node) {
    return json{{"x", node.x}, {"z", node.z}};
}

NavPathNode NavPathNodeFromJson(const json& node) {
    NavPathNode out{};
    out.x = node.value("x", 0);
    out.z = node.value("z", 0);
    return out;
}

json NpcNavigationStateToJson(const NpcNavigationState& navigation) {
    json node = json{
        {"mode", navigation.mode},
        {"route_id", navigation.route_id},
        {"heading_to_destination", navigation.heading_to_destination},
        {"path_index", navigation.path_index},
        {"target_world", Vec3ToJson(navigation.target_world)},
        {"desired_speed", navigation.desired_speed},
        {"path", json::array()},
    };
    for (const NavPathNode& path_node : navigation.path) {
        node["path"].push_back(NavPathNodeToJson(path_node));
    }
    return node;
}

NpcNavigationState NpcNavigationStateFromJson(const json& node) {
    NpcNavigationState navigation{};
    navigation.mode = node.value("mode", navigation.mode);
    navigation.route_id = node.value("route_id", navigation.route_id);
    navigation.heading_to_destination = node.value("heading_to_destination", navigation.heading_to_destination);
    navigation.path_index = node.value("path_index", navigation.path_index);
    navigation.desired_speed = node.value("desired_speed", navigation.desired_speed);
    if (node.contains("target_world") && node["target_world"].is_object()) {
        navigation.target_world = Vec3FromJson(node["target_world"], navigation.target_world);
    }
    if (node.contains("path") && node["path"].is_array()) {
        for (const json& path_node : node["path"]) {
            if (path_node.is_object()) {
                navigation.path.push_back(NavPathNodeFromJson(path_node));
            }
        }
    }
    return navigation;
}

json StoryBibleEntryToJson(const StoryBibleEntry& entry) {
    json node = json{
        {"id", entry.id},
        {"title", entry.title},
        {"summary", entry.summary},
        {"tags", json::array()},
    };
    for (const std::string& tag : entry.tags) {
        node["tags"].push_back(tag);
    }
    return node;
}

StoryBibleEntry StoryBibleEntryFromJson(const json& node) {
    StoryBibleEntry entry{};
    entry.id = node.value("id", entry.id);
    entry.title = node.value("title", entry.title);
    entry.summary = node.value("summary", entry.summary);
    if (node.contains("tags") && node["tags"].is_array()) {
        for (const json& tag_node : node["tags"]) {
            if (tag_node.is_string()) {
                entry.tags.push_back(tag_node.get<std::string>());
            }
        }
    }
    return entry;
}

json StoryBeatToJson(const StoryBeat& beat) {
    json node = json{
        {"id", beat.id},
        {"title", beat.title},
        {"summary", beat.summary},
        {"completed", beat.completed},
        {"cutscene_trigger", beat.cutscene_trigger},
        {"next_ids", json::array()},
    };
    for (const std::string& next_id : beat.next_ids) {
        node["next_ids"].push_back(next_id);
    }
    return node;
}

StoryBeat StoryBeatFromJson(const json& node) {
    StoryBeat beat{};
    beat.id = node.value("id", beat.id);
    beat.title = node.value("title", beat.title);
    beat.summary = node.value("summary", beat.summary);
    beat.completed = node.value("completed", beat.completed);
    beat.cutscene_trigger = node.value("cutscene_trigger", beat.cutscene_trigger);
    if (node.contains("next_ids") && node["next_ids"].is_array()) {
        for (const json& next_id_node : node["next_ids"]) {
            if (next_id_node.is_string()) {
                beat.next_ids.push_back(next_id_node.get<std::string>());
            }
        }
    }
    return beat;
}

json StoryRippleToJson(const StoryRipple& ripple) {
    return json{
        {"type", ripple.type},
        {"target_id", ripple.target_id},
        {"dimension", ripple.dimension},
        {"value", ripple.value},
        {"reason", ripple.reason},
    };
}

StoryRipple StoryRippleFromJson(const json& node) {
    StoryRipple ripple{};
    ripple.type = node.value("type", ripple.type);
    ripple.target_id = node.value("target_id", ripple.target_id);
    ripple.dimension = node.value("dimension", ripple.dimension);
    ripple.value = node.value("value", ripple.value);
    ripple.reason = node.value("reason", ripple.reason);
    return ripple;
}

json StoryEventToJson(const StoryEvent& event) {
    json node = json{
        {"event_id", event.event_id},
        {"beat_id", event.beat_id},
        {"title", event.title},
        {"summary", event.summary},
        {"narrator_line", event.narrator_line},
        {"applied", event.applied},
        {"ripples", json::array()},
    };
    for (const StoryRipple& ripple : event.ripples) {
        node["ripples"].push_back(StoryRippleToJson(ripple));
    }
    return node;
}

StoryEvent StoryEventFromJson(const json& node) {
    StoryEvent event{};
    event.event_id = node.value("event_id", event.event_id);
    event.beat_id = node.value("beat_id", event.beat_id);
    event.title = node.value("title", event.title);
    event.summary = node.value("summary", event.summary);
    event.narrator_line = node.value("narrator_line", event.narrator_line);
    event.applied = node.value("applied", event.applied);
    if (node.contains("ripples") && node["ripples"].is_array()) {
        for (const json& ripple_node : node["ripples"]) {
            if (ripple_node.is_object()) {
                event.ripples.push_back(StoryRippleFromJson(ripple_node));
            }
        }
    }
    return event;
}

json NarratorStateToJson(const NarratorState& narrator) {
    json node = json{
        {"enabled", narrator.enabled},
        {"voice_id", narrator.voice_id},
        {"voice_profile", VoiceProfileToJson(narrator.voice_profile)},
        {"pending_lines", json::array()},
        {"spoken_history", json::array()},
    };
    for (const std::string& line : narrator.pending_lines) {
        node["pending_lines"].push_back(line);
    }
    for (const std::string& line : narrator.spoken_history) {
        node["spoken_history"].push_back(line);
    }
    return node;
}

NarratorState NarratorStateFromJson(const json& node) {
    NarratorState narrator{};
    narrator.enabled = node.value("enabled", narrator.enabled);
    narrator.voice_id = node.value("voice_id", narrator.voice_id);
    if (node.contains("voice_profile") && node["voice_profile"].is_object()) {
        narrator.voice_profile = VoiceProfileFromJson(node["voice_profile"], narrator.voice_profile);
    } else {
        narrator.voice_profile.base_voice_id = narrator.voice_id;
    }
    if (node.contains("pending_lines") && node["pending_lines"].is_array()) {
        for (const json& line_node : node["pending_lines"]) {
            if (line_node.is_string()) {
                narrator.pending_lines.push_back(line_node.get<std::string>());
            }
        }
    }
    if (node.contains("spoken_history") && node["spoken_history"].is_array()) {
        for (const json& line_node : node["spoken_history"]) {
            if (line_node.is_string()) {
                narrator.spoken_history.push_back(line_node.get<std::string>());
            }
        }
    }
    return narrator;
}

json CutsceneStateToJson(const CutsceneState& cutscene) {
    json node = json{
        {"enabled", cutscene.enabled},
        {"active", cutscene.active},
        {"source_beat_id", cutscene.source_beat_id},
        {"source_event_id", cutscene.source_event_id},
        {"target_npc_id", cutscene.target_npc_id},
        {"target_npc_animation", cutscene.target_npc_animation},
        {"camera_start", Vec3ToJson(cutscene.camera_start)},
        {"camera_mid", Vec3ToJson(cutscene.camera_mid)},
        {"camera_end", Vec3ToJson(cutscene.camera_end)},
        {"duration_seconds", cutscene.duration_seconds},
        {"elapsed_seconds", cutscene.elapsed_seconds},
        {"narrator_line", cutscene.narrator_line},
        {"played_cutscene_history", json::array()},
    };
    for (const std::string& beat_id : cutscene.played_cutscene_history) {
        node["played_cutscene_history"].push_back(beat_id);
    }
    return node;
}

CutsceneState CutsceneStateFromJson(const json& node) {
    CutsceneState cutscene{};
    cutscene.enabled = node.value("enabled", cutscene.enabled);
    cutscene.active = node.value("active", cutscene.active);
    cutscene.source_beat_id = node.value("source_beat_id", cutscene.source_beat_id);
    cutscene.source_event_id = node.value("source_event_id", cutscene.source_event_id);
    cutscene.target_npc_id = node.value("target_npc_id", cutscene.target_npc_id);
    cutscene.target_npc_animation = node.value("target_npc_animation", cutscene.target_npc_animation);
    cutscene.duration_seconds = node.value("duration_seconds", cutscene.duration_seconds);
    cutscene.elapsed_seconds = node.value("elapsed_seconds", cutscene.elapsed_seconds);
    cutscene.narrator_line = node.value("narrator_line", cutscene.narrator_line);
    if (node.contains("camera_start") && node["camera_start"].is_object()) {
        cutscene.camera_start = Vec3FromJson(node["camera_start"], cutscene.camera_start);
    }
    if (node.contains("camera_mid") && node["camera_mid"].is_object()) {
        cutscene.camera_mid = Vec3FromJson(node["camera_mid"], cutscene.camera_mid);
    }
    if (node.contains("camera_end") && node["camera_end"].is_object()) {
        cutscene.camera_end = Vec3FromJson(node["camera_end"], cutscene.camera_end);
    }
    if (node.contains("played_cutscene_history") && node["played_cutscene_history"].is_array()) {
        for (const json& beat_node : node["played_cutscene_history"]) {
            if (beat_node.is_string()) {
                cutscene.played_cutscene_history.push_back(beat_node.get<std::string>());
            }
        }
    }
    return cutscene;
}


json FreeWillStateToJson(const FreeWillState& free_will) {
    json node = json{
        {"enabled", free_will.enabled},
        {"llm_enabled", free_will.llm_enabled},
        {"model_path", free_will.model_path},
        {"max_sparks_per_npc_per_day", free_will.max_sparks_per_npc_per_day},
        {"min_seconds_between_global_sparks", free_will.min_seconds_between_global_sparks},
        {"spark_chance_per_second", free_will.spark_chance_per_second},
        {"last_processed_day", free_will.last_processed_day},
        {"global_cooldown_remaining", free_will.global_cooldown_remaining},
        {"rng_seed", free_will.rng_seed},
    };

    json daily_counts = json::object();
    for (const auto& [npc_id, count] : free_will.daily_spark_count) {
        daily_counts[std::to_string(npc_id)] = count;
    }
    node["daily_spark_count"] = daily_counts;

    json last_lines = json::object();
    for (const auto& [npc_id, line] : free_will.last_spark_line_by_npc) {
        last_lines[std::to_string(npc_id)] = line;
    }
    node["last_spark_line_by_npc"] = last_lines;

    return node;
}

FreeWillState FreeWillStateFromJson(const json& node, const FreeWillState& fallback) {
    FreeWillState free_will = fallback;
    free_will.enabled = node.value("enabled", free_will.enabled);
    free_will.llm_enabled = node.value("llm_enabled", free_will.llm_enabled);
    free_will.model_path = node.value("model_path", free_will.model_path);
    free_will.max_sparks_per_npc_per_day = std::max(1U, node.value("max_sparks_per_npc_per_day", free_will.max_sparks_per_npc_per_day));
    free_will.min_seconds_between_global_sparks = std::max(0.25F, node.value("min_seconds_between_global_sparks", free_will.min_seconds_between_global_sparks));
    free_will.spark_chance_per_second = std::max(0.0F, node.value("spark_chance_per_second", free_will.spark_chance_per_second));
    free_will.last_processed_day = std::max(1U, node.value("last_processed_day", free_will.last_processed_day));
    free_will.global_cooldown_remaining = std::max(0.0F, node.value("global_cooldown_remaining", free_will.global_cooldown_remaining));
    free_will.rng_seed = node.value("rng_seed", free_will.rng_seed);

    free_will.daily_spark_count.clear();
    if (node.contains("daily_spark_count") && node["daily_spark_count"].is_object()) {
        for (const auto& [npc_id_key, count_node] : node["daily_spark_count"].items()) {
            if (!count_node.is_number_unsigned()) {
                continue;
            }
            try {
                const std::uint64_t npc_id = std::stoull(npc_id_key);
                free_will.daily_spark_count[npc_id] = count_node.get<std::uint32_t>();
            } catch (const std::exception&) {
                continue;
            }
        }
    }

    free_will.last_spark_line_by_npc.clear();
    if (node.contains("last_spark_line_by_npc") && node["last_spark_line_by_npc"].is_object()) {
        for (const auto& [npc_id_key, line_node] : node["last_spark_line_by_npc"].items()) {
            if (!line_node.is_string()) {
                continue;
            }
            try {
                const std::uint64_t npc_id = std::stoull(npc_id_key);
                free_will.last_spark_line_by_npc[npc_id] = line_node.get<std::string>();
            } catch (const std::exception&) {
                continue;
            }
        }
    }

    free_will.pending_sparks.clear();
    return free_will;
}

json NavmeshToJson(const NavmeshData& navmesh) {
    return json{
        {"cell_size", navmesh.cell_size},
        {"min_x", navmesh.min_x},
        {"min_z", navmesh.min_z},
        {"width", navmesh.width},
        {"height", navmesh.height},
        {"walkable", navmesh.walkable},
        {"revision", navmesh.revision},
        {"last_buildable_count", navmesh.last_buildable_count},
        {"dirty", navmesh.dirty},
    };
}

NavmeshData NavmeshFromJson(const json& node) {
    NavmeshData navmesh{};
    navmesh.cell_size = node.value("cell_size", navmesh.cell_size);
    navmesh.min_x = node.value("min_x", navmesh.min_x);
    navmesh.min_z = node.value("min_z", navmesh.min_z);
    navmesh.width = std::max(1, node.value("width", navmesh.width));
    navmesh.height = std::max(1, node.value("height", navmesh.height));
    navmesh.revision = node.value("revision", navmesh.revision);
    navmesh.last_buildable_count = node.value("last_buildable_count", navmesh.last_buildable_count);
    navmesh.dirty = node.value("dirty", navmesh.dirty);
    if (node.contains("walkable") && node["walkable"].is_array()) {
        navmesh.walkable.clear();
        for (const json& walkable_node : node["walkable"]) {
            if (walkable_node.is_number_integer()) {
                navmesh.walkable.push_back(static_cast<std::uint8_t>(walkable_node.get<int>() > 0 ? 1 : 0));
            }
        }
    }
    return navmesh;
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


json CombatUnitStateToJson(const CombatUnitState& unit) {
    return json{
        {"entity_id", unit.entity_id},
        {"team_id", unit.team_id},
        {"grid_x", unit.grid_x},
        {"grid_y", unit.grid_y},
        {"health", unit.health},
        {"max_health", unit.max_health},
        {"ap", unit.ap},
        {"max_ap", unit.max_ap},
        {"initiative", unit.initiative},
        {"alive", unit.alive},
    };
}

CombatUnitState CombatUnitStateFromJson(const json& node, const CombatUnitState& fallback) {
    CombatUnitState unit = fallback;
    unit.entity_id = node.value("entity_id", unit.entity_id);
    unit.team_id = node.value("team_id", unit.team_id);
    unit.grid_x = node.value("grid_x", unit.grid_x);
    unit.grid_y = node.value("grid_y", unit.grid_y);
    unit.health = std::max(0.0F, node.value("health", unit.health));
    unit.max_health = std::max(unit.health, node.value("max_health", unit.max_health));
    unit.ap = node.value("ap", unit.ap);
    unit.max_ap = std::max(1U, node.value("max_ap", unit.max_ap));
    unit.initiative = node.value("initiative", unit.initiative);
    unit.alive = node.value("alive", unit.alive) && unit.health > 0.0F;
    return unit;
}

json CombatStateToJson(const CombatState& combat) {
    json node = json{
        {"active", combat.active},
        {"grid_width", combat.grid_width},
        {"grid_height", combat.grid_height},
        {"active_turn_index", combat.active_turn_index},
        {"round_index", combat.round_index},
        {"trigger_source", combat.trigger_source},
        {"last_resolution", combat.last_resolution},
        {"units", json::array()},
        {"turn_order", json::array()},
    };
    for (const CombatUnitState& unit : combat.units) {
        node["units"].push_back(CombatUnitStateToJson(unit));
    }
    for (const std::uint64_t entity_id : combat.turn_order) {
        node["turn_order"].push_back(entity_id);
    }
    return node;
}

CombatState CombatStateFromJson(const json& node, const CombatState& fallback) {
    CombatState combat = fallback;
    combat.active = node.value("active", combat.active);
    combat.grid_width = std::max(4U, node.value("grid_width", combat.grid_width));
    combat.grid_height = std::max(4U, node.value("grid_height", combat.grid_height));
    combat.active_turn_index = node.value("active_turn_index", combat.active_turn_index);
    combat.round_index = std::max(0U, node.value("round_index", combat.round_index));
    combat.trigger_source = node.value("trigger_source", combat.trigger_source);
    combat.last_resolution = node.value("last_resolution", combat.last_resolution);
    combat.units.clear();
    if (node.contains("units") && node["units"].is_array()) {
        for (const json& unit_node : node["units"]) {
            if (unit_node.is_object()) {
                combat.units.push_back(CombatUnitStateFromJson(unit_node, CombatUnitState{}));
            }
        }
    }
    combat.turn_order.clear();
    if (node.contains("turn_order") && node["turn_order"].is_array()) {
        for (const json& id_node : node["turn_order"]) {
            if (id_node.is_number_unsigned()) {
                combat.turn_order.push_back(id_node.get<std::uint64_t>());
            }
        }
    }
    if (combat.turn_order.empty()) {
        for (const CombatUnitState& unit : combat.units) {
            combat.turn_order.push_back(unit.entity_id);
        }
    }
    if (combat.turn_order.empty()) {
        combat.active_turn_index = 0;
    } else {
        combat.active_turn_index = std::min(combat.active_turn_index, combat.turn_order.size() - 1U);
    }
    return combat;
}

json SettlementStateToJson(const SettlementState& settlement) {
    return json{
        {"village_name", settlement.village_name},
        {"total_population", settlement.total_population},
        {"shared_resources", FloatMapToJson(settlement.shared_resources)},
        {"morale", settlement.morale},
        {"tick_interval_seconds", settlement.tick_interval_seconds},
        {"accumulated_tick_seconds", settlement.accumulated_tick_seconds},
    };
}

SettlementState SettlementStateFromJson(const json& node, const SettlementState& fallback) {
    SettlementState settlement = fallback;
    settlement.village_name = node.value("village_name", settlement.village_name);
    settlement.total_population = node.value("total_population", settlement.total_population);
    if (node.contains("shared_resources") && node["shared_resources"].is_object()) {
        FloatMapFromJson(node["shared_resources"], settlement.shared_resources);
    }
    settlement.morale = std::clamp(node.value("morale", settlement.morale), 0.0F, 100.0F);
    settlement.tick_interval_seconds = std::clamp(node.value("tick_interval_seconds", settlement.tick_interval_seconds), 1.0F, 60.0F);
    settlement.accumulated_tick_seconds = std::max(0.0F, node.value("accumulated_tick_seconds", settlement.accumulated_tick_seconds));
    settlement.shared_resources["food"] = std::max(0.0F, settlement.shared_resources.count("food") > 0 ? settlement.shared_resources["food"] : 80.0F);
    settlement.shared_resources["stockpile"] =
        std::max(0.0F, settlement.shared_resources.count("stockpile") > 0 ? settlement.shared_resources["stockpile"] : 45.0F);
    return settlement;
}

constexpr int kCurrentSceneSchemaVersion = 2;
constexpr std::size_t kMaxEntityCount = 10000U;
constexpr std::size_t kMaxRecentActionsCount = 5000U;
constexpr std::size_t kMaxActiveNpcCount = 5000U;

std::set<std::string> RootFieldAllowList() {
    return {
        "schema_version",
        "entities",
        "elapsed_seconds",
        "day_progress",
        "day_cycle_speed",
        "day_count",
        "world_time",
        "biome",
        "world_style_guide",
        "weather",
        "settlement",
        "combat",
        "build_mode_enabled",
        "active_dialog_npc_id",
        "player_inventory",
        "npc_relationships",
        "relationships",
        "factions",
        "player_reputation",
        "economy",
        "navmesh",
        "active_npc_ids",
        "npc_navigation",
        "player_proxy_position",
        "directional_light",
        "recent_actions",
        "co_creator_queue",
        "story",
        "narrator",
        "cutscene",
        "free_will",
        "render_2d",
        "post_processing",
        "quality_metadata",
    };
}

void AppendDiagnostic(std::vector<std::string>& diagnostics, const std::string& message) {
    diagnostics.push_back(message);
}

bool ReadSceneDocument(const std::string& path, json& out_document, std::string& out_error) {
    std::ifstream file(path);
    if (!file.is_open()) {
        out_error = "unable to open file";
        return false;
    }

    try {
        file >> out_document;
    } catch (const std::exception& exception) {
        out_error = std::string("JSON parse error: ") + exception.what();
        return false;
    }

    if (!out_document.is_object()) {
        out_error = "root node must be a JSON object";
        return false;
    }

    return true;
}

bool ValidateSceneDocument(
    const json& document,
    std::vector<std::string>& warnings,
    std::vector<std::string>& errors) {
    const std::set<std::string> allow_list = RootFieldAllowList();

    if (!document.contains("entities")) {
        AppendDiagnostic(errors, "Missing required field: entities");
    } else if (!document["entities"].is_array()) {
        AppendDiagnostic(errors, "Field 'entities' must be an array");
    } else if (document["entities"].size() > kMaxEntityCount) {
        AppendDiagnostic(
            errors,
            "Field 'entities' exceeds limit (" + std::to_string(document["entities"].size()) + " > " +
                std::to_string(kMaxEntityCount) + ")");
    }

    if (document.contains("recent_actions") && document["recent_actions"].is_array() &&
        document["recent_actions"].size() > kMaxRecentActionsCount) {
        AppendDiagnostic(
            warnings,
            "Field 'recent_actions' exceeds recommended limit (" + std::to_string(document["recent_actions"].size()) + ")");
    }

    if (document.contains("active_npc_ids") && document["active_npc_ids"].is_array() &&
        document["active_npc_ids"].size() > kMaxActiveNpcCount) {
        AppendDiagnostic(
            warnings,
            "Field 'active_npc_ids' exceeds recommended limit (" + std::to_string(document["active_npc_ids"].size()) + ")");
    }

    if (document.contains("schema_version") && !document["schema_version"].is_number_integer()) {
        AppendDiagnostic(warnings, "Field 'schema_version' should be an integer; treating as legacy schema.");
    }

    for (const auto& [field, _] : document.items()) {
        if (allow_list.find(field) == allow_list.end()) {
            AppendDiagnostic(warnings, "Unknown root field: " + field);
        }
    }

    return errors.empty();
}

std::string BuildBackupPath(const std::string& path) {
    namespace fs = std::filesystem;
    const fs::path source(path);
    const auto now = std::chrono::system_clock::now();
    const std::time_t now_time = std::chrono::system_clock::to_time_t(now);
    std::tm utc{};
#if defined(_WIN32)
    gmtime_s(&utc, &now_time);
#else
    gmtime_r(&now_time, &utc);
#endif
    std::ostringstream stamp;
    stamp << std::put_time(&utc, "%Y%m%dT%H%M%SZ");
    return source.string() + ".v" + stamp.str() + ".bak";
}

bool CreateBackupFile(const std::string& source_path, std::string& out_backup_path) {
    namespace fs = std::filesystem;
    out_backup_path = BuildBackupPath(source_path);
    std::error_code copy_error{};
    fs::copy_file(source_path, out_backup_path, fs::copy_options::overwrite_existing, copy_error);
    return !copy_error;
}

bool SaveMigratedDocument(const std::string& path, const json& document) {
    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }
    file << document.dump(4) << '\n';
    return static_cast<bool>(file);
}

int ReadSchemaVersion(const json& document) {
    if (!document.contains("schema_version") || !document["schema_version"].is_number_integer()) {
        return 1;
    }
    return std::max(1, document["schema_version"].get<int>());
}

bool MigrateV1ToV2(json& document, std::vector<std::string>& diagnostics) {
    bool changed = false;
    if (!document.contains("world_time") || !document["world_time"].is_object()) {
        const float elapsed = document.value("elapsed_seconds", 0.0F);
        const float progress = Clamp01(document.value("day_progress", 0.25F));
        const float cycle_speed = std::max(0.0F, document.value("day_cycle_speed", 0.01F));
        const std::uint32_t day_count = std::max(1U, document.value("day_count", 1U));
        document["world_time"] = WorldTimeToJson(WorldTime{elapsed, progress, cycle_speed, day_count, 1440U});
        changed = true;
    }

    if (!document.contains("relationships") && document.contains("npc_relationships") && document["npc_relationships"].is_object()) {
        json relationships = json::object();
        for (const auto& [npc_id_key, trust_node] : document["npc_relationships"].items()) {
            if (!trust_node.is_number()) {
                continue;
            }
            const float trust = trust_node.get<float>();
            relationships[npc_id_key] = RelationshipProfileToJson(RelationshipProfile{
                trust,
                trust * 0.75F,
                std::max(0.0F, -trust),
                0.0F,
                trust * 0.60F,
                0U,
                {}});
        }
        document["relationships"] = relationships;
        changed = true;
    }

    if (changed) {
        AppendDiagnostic(diagnostics, "Applied migration v1 -> v2");
    }
    document["schema_version"] = kCurrentSceneSchemaVersion;
    return changed;
}

bool MigrateToCurrentSchema(
    const std::string& path,
    json& document,
    std::vector<std::string>& diagnostics,
    std::vector<std::string>& errors,
    std::vector<std::string>& warnings) {
    int schema_version = ReadSchemaVersion(document);
    if (schema_version > kCurrentSceneSchemaVersion) {
        AppendDiagnostic(
            warnings,
            "Save schema " + std::to_string(schema_version) +
                " is newer than runtime schema " + std::to_string(kCurrentSceneSchemaVersion) + "; attempting best-effort load.");
        return false;
    }

    if (schema_version == kCurrentSceneSchemaVersion) {
        return false;
    }

    std::string backup_path;
    if (!CreateBackupFile(path, backup_path)) {
        AppendDiagnostic(errors, "Migration aborted: failed to create backup for '" + path + "'");
        return false;
    }
    AppendDiagnostic(diagnostics, "Backup created: " + backup_path);

    bool changed = false;
    if (schema_version < 2) {
        changed = MigrateV1ToV2(document, diagnostics) || changed;
    }

    if (changed && !SaveMigratedDocument(path, document)) {
        AppendDiagnostic(errors, "Migration failed: unable to write migrated scene to '" + path + "'");
        return false;
    }
    if (changed) {
        AppendDiagnostic(diagnostics, "Migrated scene saved to: " + path);
    }
    return changed;
}
}  // namespace

bool SceneLoader::Load(const std::string& path, Scene& scene) {
    json document;
    std::string read_error;
    if (!ReadSceneDocument(path, document, read_error)) {
        GF_LOG_WARN("Scene load failed for '" + path + "': " + read_error);
        return false;
    }

    std::vector<std::string> validation_warnings;
    std::vector<std::string> validation_errors;
    if (!ValidateSceneDocument(document, validation_warnings, validation_errors)) {
        for (const std::string& error : validation_errors) {
            GF_LOG_WARN("Scene validation error: " + error);
        }
        return false;
    }
    std::vector<std::string> migration_diagnostics;
    if (MigrateToCurrentSchema(path, document, migration_diagnostics, validation_errors, validation_warnings)) {
        for (const std::string& diagnostic : migration_diagnostics) {
            GF_LOG_INFO(diagnostic);
        }
    }
    if (!validation_errors.empty()) {
        for (const std::string& error : validation_errors) {
            GF_LOG_WARN("Scene migration error: " + error);
        }
        return false;
    }
    for (const std::string& warning : validation_warnings) {
        GF_LOG_WARN("Scene migration warning: " + warning);
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
    scene.world_time.elapsed_seconds = scene.elapsed_seconds;
    scene.world_time.day_progress = scene.day_progress;
    scene.world_time.day_cycle_speed = scene.day_cycle_speed;
    scene.world_time.day_count = scene.day_count;
    if (document.contains("world_time") && document["world_time"].is_object()) {
        scene.world_time = WorldTimeFromJson(document["world_time"], scene.world_time);
        scene.elapsed_seconds = scene.world_time.elapsed_seconds;
        scene.day_progress = scene.world_time.day_progress;
        scene.day_cycle_speed = scene.world_time.day_cycle_speed;
        scene.day_count = scene.world_time.day_count;
    }
    scene.biome = document.value("biome", scene.biome);
    scene.world_style_guide = document.value("world_style_guide", scene.world_style_guide);
    if (document.contains("weather") && document["weather"].is_object()) {
        scene.weather = WeatherStateFromJson(document["weather"], scene.weather);
    }
    scene.settlement = SettlementState{};
    if (document.contains("settlement") && document["settlement"].is_object()) {
        scene.settlement = SettlementStateFromJson(document["settlement"], scene.settlement);
    }
    scene.combat = CombatState{};
    if (document.contains("combat") && document["combat"].is_object()) {
        scene.combat = CombatStateFromJson(document["combat"], scene.combat);
    }
    scene.weather.last_relationship_day_applied = std::max(scene.day_count, scene.weather.last_relationship_day_applied);
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
    scene.relationships.clear();
    if (document.contains("relationships") && document["relationships"].is_object()) {
        for (const auto& [npc_id_key, profile_node] : document["relationships"].items()) {
            if (!profile_node.is_object()) {
                continue;
            }
            try {
                const std::uint64_t npc_id = std::stoull(npc_id_key);
                scene.relationships[npc_id] = RelationshipProfileFromJson(profile_node);
            } catch (const std::exception&) {
                continue;
            }
        }
    }
    if (scene.relationships.empty()) {
        for (const auto& [npc_id, legacy] : scene.npc_relationships) {
            RelationshipProfile profile{};
            profile.trust = legacy;
            profile.respect = legacy * 0.75F;
            profile.loyalty = legacy * 0.60F;
            profile.grudge = std::max(0.0F, -legacy);
            scene.relationships[npc_id] = profile;
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

    if (document.contains("navmesh") && document["navmesh"].is_object()) {
        scene.navmesh = NavmeshFromJson(document["navmesh"]);
    } else {
        scene.navmesh = NavmeshData{};
    }

    scene.active_npc_ids.clear();
    if (document.contains("active_npc_ids") && document["active_npc_ids"].is_array()) {
        for (const json& npc_id_node : document["active_npc_ids"]) {
            if (!npc_id_node.is_number_unsigned()) {
                continue;
            }
            scene.active_npc_ids.push_back(npc_id_node.get<std::uint64_t>());
        }
    }

    scene.npc_navigation.clear();
    if (document.contains("npc_navigation") && document["npc_navigation"].is_object()) {
        for (const auto& [npc_id_key, nav_node] : document["npc_navigation"].items()) {
            if (!nav_node.is_object()) {
                continue;
            }
            try {
                const std::uint64_t npc_id = std::stoull(npc_id_key);
                scene.npc_navigation[npc_id] = NpcNavigationStateFromJson(nav_node);
            } catch (const std::exception&) {
                continue;
            }
        }
    }

    if (document.contains("player_proxy_position") && document["player_proxy_position"].is_object()) {
        scene.player_proxy_position = Vec3FromJson(document["player_proxy_position"], scene.player_proxy_position);
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
    scene.story = StoryState{};
    if (document.contains("story") && document["story"].is_object()) {
        const json& story = document["story"];
        if (story.contains("lore_entries") && story["lore_entries"].is_array()) {
            for (const json& node : story["lore_entries"]) {
                if (node.is_object()) {
                    scene.story.lore_entries.push_back(StoryBibleEntryFromJson(node));
                }
            }
        }
        if (story.contains("major_npcs") && story["major_npcs"].is_array()) {
            for (const json& node : story["major_npcs"]) {
                if (node.is_object()) {
                    scene.story.major_npcs.push_back(StoryBibleEntryFromJson(node));
                }
            }
        }
        if (story.contains("key_events") && story["key_events"].is_array()) {
            for (const json& node : story["key_events"]) {
                if (node.is_object()) {
                    scene.story.key_events.push_back(StoryBibleEntryFromJson(node));
                }
            }
        }
        if (story.contains("faction_notes") && story["faction_notes"].is_array()) {
            for (const json& node : story["faction_notes"]) {
                if (node.is_object()) {
                    scene.story.faction_notes.push_back(StoryBibleEntryFromJson(node));
                }
            }
        }
        if (story.contains("campaign_beats") && story["campaign_beats"].is_array()) {
            for (const json& node : story["campaign_beats"]) {
                if (node.is_object()) {
                    scene.story.campaign_beats.push_back(StoryBeatFromJson(node));
                }
            }
        }
        if (story.contains("pending_events") && story["pending_events"].is_array()) {
            for (const json& node : story["pending_events"]) {
                if (node.is_object()) {
                    scene.story.pending_events.push_back(StoryEventFromJson(node));
                }
            }
        }
        if (story.contains("event_history") && story["event_history"].is_array()) {
            for (const json& node : story["event_history"]) {
                if (node.is_string()) {
                    scene.story.event_history.push_back(node.get<std::string>());
                }
            }
        }
    }
    scene.narrator = NarratorState{};
    if (document.contains("narrator") && document["narrator"].is_object()) {
        scene.narrator = NarratorStateFromJson(document["narrator"]);
    }
    scene.cutscene = CutsceneState{};
    if (document.contains("cutscene") && document["cutscene"].is_object()) {
        scene.cutscene = CutsceneStateFromJson(document["cutscene"]);
    }
    scene.free_will = FreeWillState{};
    if (document.contains("free_will") && document["free_will"].is_object()) {
        scene.free_will = FreeWillStateFromJson(document["free_will"], scene.free_will);
    }
    if (scene.free_will.llm_enabled && scene.free_will.model_path.empty()) {
        scene.free_will.model_path = ResolveFreeWillModelPath(path);
    }
    if (!scene.free_will.model_path.empty()) {
        const std::filesystem::path configured_model_path(scene.free_will.model_path);
        if (!std::filesystem::exists(configured_model_path)) {
            scene.free_will.model_path.clear();
        }
    }
    scene.render_2d = SceneRender2D{};
    if (document.contains("render_2d") && document["render_2d"].is_object()) {
        const json& render_2d = document["render_2d"];
        scene.render_2d.render_mode = render_2d.value("render_mode", scene.render_2d.render_mode);
        if (scene.render_2d.render_mode != "3D") {
            scene.render_2d.render_mode = "2D";
        }
        scene.render_2d.enabled = render_2d.value("enabled", scene.render_2d.enabled);
        if (render_2d.contains("camera") && render_2d["camera"].is_object()) {
            const json& camera_2d = render_2d["camera"];
            if (camera_2d.contains("center") && camera_2d["center"].is_object()) {
                scene.render_2d.camera.center = Vec2FromJson(camera_2d["center"], scene.render_2d.camera.center);
            }
            if (camera_2d.contains("viewport_world_size") && camera_2d["viewport_world_size"].is_object()) {
                scene.render_2d.camera.viewport_world_size =
                    Vec2FromJson(camera_2d["viewport_world_size"], scene.render_2d.camera.viewport_world_size);
            }
            scene.render_2d.camera.pixels_per_unit =
                std::max(1.0F, camera_2d.value("pixels_per_unit", scene.render_2d.camera.pixels_per_unit));
            scene.render_2d.camera.pixel_snap =
                camera_2d.value("pixel_snap", scene.render_2d.camera.pixel_snap);
        }
        if (render_2d.contains("sprites") && render_2d["sprites"].is_array()) {
            for (const json& sprite_node : render_2d["sprites"]) {
                if (sprite_node.is_object()) {
                    scene.render_2d.sprites.push_back(SceneSprite2DFromJson(sprite_node, SceneSprite2D{}));
                }
            }
        }
        if (render_2d.contains("entity_sprite_map") && render_2d["entity_sprite_map"].is_object()) {
            for (const auto& [entity_type, asset_id_node] : render_2d["entity_sprite_map"].items()) {
                if (!asset_id_node.is_string()) {
                    continue;
                }
                const std::string asset_id = asset_id_node.get<std::string>();
                if (entity_type.empty() || asset_id.empty()) {
                    continue;
                }
                scene.render_2d.entity_sprite_map[entity_type] = asset_id;
            }
        }
        if (render_2d.contains("tilemaps") && render_2d["tilemaps"].is_array()) {
            for (const json& tilemap_node : render_2d["tilemaps"]) {
                if (tilemap_node.is_object()) {
                    scene.render_2d.tilemaps.push_back(SceneTilemap2DFromJson(tilemap_node, SceneTilemap2D{}));
                }
            }
        }
    }


    scene.post_processing = ScenePostProcessingSettings{};
    if (document.contains("post_processing") && document["post_processing"].is_object()) {
        const json& post = document["post_processing"];
        scene.post_processing.enabled = post.value("enabled", scene.post_processing.enabled);
        scene.post_processing.bloom_enabled = post.value("bloom_enabled", scene.post_processing.bloom_enabled);
        scene.post_processing.vignette_enabled = post.value("vignette_enabled", scene.post_processing.vignette_enabled);
        scene.post_processing.color_grading_enabled = post.value("color_grading_enabled", scene.post_processing.color_grading_enabled);
        scene.post_processing.outline_enabled = post.value("outline_enabled", scene.post_processing.outline_enabled);
        scene.post_processing.bloom_strength = std::clamp(post.value("bloom_strength", scene.post_processing.bloom_strength), 0.0F, 2.0F);
        scene.post_processing.vignette_strength = std::clamp(post.value("vignette_strength", scene.post_processing.vignette_strength), 0.0F, 1.0F);
        scene.post_processing.color_grade_saturation = std::clamp(post.value("color_grade_saturation", scene.post_processing.color_grade_saturation), 0.2F, 2.0F);
        scene.post_processing.color_grade_contrast = std::clamp(post.value("color_grade_contrast", scene.post_processing.color_grade_contrast), 0.5F, 1.8F);
        scene.post_processing.outline_strength = std::clamp(post.value("outline_strength", scene.post_processing.outline_strength), 0.0F, 1.0F);
    }

    scene.quality_metadata = SceneQualityMetadata{};
    if (document.contains("quality_metadata") && document["quality_metadata"].is_object()) {
        const json& quality = document["quality_metadata"];
        scene.quality_metadata.score = std::clamp(quality.value("score", scene.quality_metadata.score), 0, 100);
        scene.quality_metadata.estimated_vram_mb = std::max(0.0F, quality.value("estimated_vram_mb", scene.quality_metadata.estimated_vram_mb));
        scene.quality_metadata.sprite_count = std::max(0, quality.value("sprite_count", scene.quality_metadata.sprite_count));
        scene.quality_metadata.vram_warning_threshold_mb = std::max(128, quality.value("vram_warning_threshold_mb", scene.quality_metadata.vram_warning_threshold_mb));
        scene.quality_metadata.sprite_warning_threshold = std::max(64, quality.value("sprite_warning_threshold", scene.quality_metadata.sprite_warning_threshold));
        scene.quality_metadata.warnings.clear();
        if (quality.contains("warnings") && quality["warnings"].is_array()) {
            for (const json& warning : quality["warnings"]) {
                if (warning.is_string()) {
                    scene.quality_metadata.warnings.push_back(warning.get<std::string>());
                }
            }
        }
    }

    scene.Update(0.0F);
    return true;
}

bool SceneLoader::Save(const std::string& path, const Scene& scene) {
    json document;
    document["schema_version"] = kCurrentSceneSchemaVersion;
    document["entities"] = json::array();
    document["entities"].reserve(scene.entities.size());

    for (const Entity& entity : scene.entities) {
        document["entities"].push_back(EntityToJson(entity));
    }
    document["elapsed_seconds"] = scene.elapsed_seconds;
    document["day_progress"] = scene.day_progress;
    document["day_cycle_speed"] = scene.day_cycle_speed;
    document["day_count"] = scene.day_count;
    document["world_time"] = WorldTimeToJson(scene.world_time);
    document["biome"] = scene.biome;
    document["world_style_guide"] = scene.world_style_guide;
    document["weather"] = WeatherStateToJson(scene.weather);
    document["settlement"] = SettlementStateToJson(scene.settlement);
    document["combat"] = CombatStateToJson(scene.combat);
    document["build_mode_enabled"] = scene.build_mode_enabled;
    document["active_dialog_npc_id"] = scene.active_dialog_npc_id;
    document["player_inventory"] = InventoryToJson(scene.player_inventory);
    json relationships = json::object();
    for (const auto& [npc_id, relationship] : scene.npc_relationships) {
        relationships[std::to_string(npc_id)] = relationship;
    }
    document["npc_relationships"] = relationships;
    json multidim_relationships = json::object();
    for (const auto& [npc_id, profile] : scene.relationships) {
        multidim_relationships[std::to_string(npc_id)] = RelationshipProfileToJson(profile);
    }
    document["relationships"] = multidim_relationships;
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
    document["navmesh"] = NavmeshToJson(scene.navmesh);
    document["active_npc_ids"] = json::array();
    for (const std::uint64_t npc_id : scene.active_npc_ids) {
        document["active_npc_ids"].push_back(npc_id);
    }
    json npc_navigation = json::object();
    for (const auto& [npc_id, navigation] : scene.npc_navigation) {
        npc_navigation[std::to_string(npc_id)] = NpcNavigationStateToJson(navigation);
    }
    document["npc_navigation"] = npc_navigation;
    document["player_proxy_position"] = Vec3ToJson(scene.player_proxy_position);
    document["directional_light"] = DirectionalLightToJson(scene.directional_light);
    document["recent_actions"] = json::array();
    for (const std::string& action : scene.recent_actions) {
        document["recent_actions"].push_back(action);
    }
    document["co_creator_queue"] = json::array();
    for (const CoCreatorQueuedMutation& mutation : scene.co_creator_queue) {
        document["co_creator_queue"].push_back(CoCreatorMutationToJson(mutation));
    }
    json story = json::object();
    story["lore_entries"] = json::array();
    for (const StoryBibleEntry& entry : scene.story.lore_entries) {
        story["lore_entries"].push_back(StoryBibleEntryToJson(entry));
    }
    story["major_npcs"] = json::array();
    for (const StoryBibleEntry& entry : scene.story.major_npcs) {
        story["major_npcs"].push_back(StoryBibleEntryToJson(entry));
    }
    story["key_events"] = json::array();
    for (const StoryBibleEntry& entry : scene.story.key_events) {
        story["key_events"].push_back(StoryBibleEntryToJson(entry));
    }
    story["faction_notes"] = json::array();
    for (const StoryBibleEntry& entry : scene.story.faction_notes) {
        story["faction_notes"].push_back(StoryBibleEntryToJson(entry));
    }
    story["campaign_beats"] = json::array();
    for (const StoryBeat& beat : scene.story.campaign_beats) {
        story["campaign_beats"].push_back(StoryBeatToJson(beat));
    }
    story["pending_events"] = json::array();
    for (const StoryEvent& event : scene.story.pending_events) {
        story["pending_events"].push_back(StoryEventToJson(event));
    }
    story["event_history"] = json::array();
    for (const std::string& event_id : scene.story.event_history) {
        story["event_history"].push_back(event_id);
    }
    document["story"] = story;
    document["narrator"] = NarratorStateToJson(scene.narrator);
    document["cutscene"] = CutsceneStateToJson(scene.cutscene);
    document["free_will"] = FreeWillStateToJson(scene.free_will);
    json render_2d = json::object();
    render_2d["render_mode"] = scene.render_2d.render_mode == "3D" ? "3D" : "2D";
    render_2d["enabled"] = scene.render_2d.enabled;
    render_2d["camera"] = json{
        {"center", Vec2ToJson(scene.render_2d.camera.center)},
        {"viewport_world_size", Vec2ToJson(scene.render_2d.camera.viewport_world_size)},
        {"pixels_per_unit", scene.render_2d.camera.pixels_per_unit},
        {"pixel_snap", scene.render_2d.camera.pixel_snap},
    };
    render_2d["sprites"] = json::array();
    for (const SceneSprite2D& sprite : scene.render_2d.sprites) {
        render_2d["sprites"].push_back(SceneSprite2DToJson(sprite));
    }
    render_2d["entity_sprite_map"] = json::object();
    for (const auto& [entity_type, asset_id] : scene.render_2d.entity_sprite_map) {
        if (entity_type.empty() || asset_id.empty()) {
            continue;
        }
        render_2d["entity_sprite_map"][entity_type] = asset_id;
    }
    render_2d["tilemaps"] = json::array();
    for (const SceneTilemap2D& tilemap : scene.render_2d.tilemaps) {
        render_2d["tilemaps"].push_back(SceneTilemap2DToJson(tilemap));
    }
    document["render_2d"] = render_2d;
    document["post_processing"] = json{
        {"enabled", scene.post_processing.enabled},
        {"bloom_enabled", scene.post_processing.bloom_enabled},
        {"vignette_enabled", scene.post_processing.vignette_enabled},
        {"color_grading_enabled", scene.post_processing.color_grading_enabled},
        {"outline_enabled", scene.post_processing.outline_enabled},
        {"bloom_strength", scene.post_processing.bloom_strength},
        {"vignette_strength", scene.post_processing.vignette_strength},
        {"color_grade_saturation", scene.post_processing.color_grade_saturation},
        {"color_grade_contrast", scene.post_processing.color_grade_contrast},
        {"outline_strength", scene.post_processing.outline_strength},
    };
    document["quality_metadata"] = json{
        {"schema", "gameforge.scene_quality_metadata.v1"},
        {"score", scene.quality_metadata.score},
        {"estimated_vram_mb", scene.quality_metadata.estimated_vram_mb},
        {"sprite_count", scene.quality_metadata.sprite_count},
        {"vram_warning_threshold_mb", scene.quality_metadata.vram_warning_threshold_mb},
        {"sprite_warning_threshold", scene.quality_metadata.sprite_warning_threshold},
        {"warnings", scene.quality_metadata.warnings},
    };

    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }

    file << document.dump(4) << '\n';
    return true;
}

bool SceneLoader::Validate(const std::string& path, std::string& report) {
    json document;
    std::string read_error;
    if (!ReadSceneDocument(path, document, read_error)) {
        report = "Validation failed: " + read_error;
        return false;
    }

    std::vector<std::string> warnings;
    std::vector<std::string> errors;
    ValidateSceneDocument(document, warnings, errors);
    std::ostringstream summary;
    summary << "scene='" << path << "' schema_version=" << ReadSchemaVersion(document)
            << " warnings=" << warnings.size() << " errors=" << errors.size();
    for (const std::string& warning : warnings) {
        summary << "\n  warning: " << warning;
    }
    for (const std::string& error : errors) {
        summary << "\n  error: " << error;
    }
    report = summary.str();
    return errors.empty();
}
