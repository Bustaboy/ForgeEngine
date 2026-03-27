#include "ScriptedBehaviorSystem.h"

#include "Logger.h"
#include "NPCController.h"
#include "Scene.h"

#include <algorithm>
#include <fstream>
#include <nlohmann/json.hpp>

namespace {
using json = nlohmann::json;

Entity* FindNpc(Scene& scene, std::uint64_t entity_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id && !entity.buildable.IsValid()) {
            return &entity;
        }
    }
    return nullptr;
}

void SeedDefaultDefinitions(Scene& scene) {
    if (!scene.scripted_behavior.definitions.empty()) {
        return;
    }

    auto add_def = [&](const std::string& name, const std::string& activity, const std::string& location, float duration_hours, bool complex) {
        scene.scripted_behavior.definitions[name] = ScriptedBehaviorDefinition{name, activity, location, duration_hours, complex};
    };

    add_def("patrol", "patrol", "town", 0.20F, false);
    add_def("harvest", "work", "work", 0.50F, true);
    add_def("guard", "guard", "work", 0.35F, false);
    add_def("rest", "rest", "home", 0.25F, false);
    add_def("flee", "flee", "home", 0.10F, false);
    add_def("socialize", "socialize", "town", 0.20F, true);
}

void TryLoadDefinitionsFromJson(Scene& scene) {
    const std::string& path = scene.scripted_behavior.definitions_path;
    if (path.empty()) {
        scene.scripted_behavior.definitions_loaded = true;
        return;
    }

    std::ifstream input(path);
    if (!input.good()) {
        scene.scripted_behavior.definitions_loaded = true;
        return;
    }

    json root;
    try {
        input >> root;
    } catch (const std::exception&) {
        scene.scripted_behavior.definitions_loaded = true;
        return;
    }

    if (!root.contains("states") || !root["states"].is_array()) {
        scene.scripted_behavior.definitions_loaded = true;
        return;
    }

    for (const json& node : root["states"]) {
        if (!node.is_object()) {
            continue;
        }

        const std::string name = node.value("name", std::string{});
        if (name.empty()) {
            continue;
        }

        ScriptedBehaviorDefinition def{};
        def.name = name;
        def.activity = node.value("activity", std::string{"idle"});
        def.location = node.value("location", std::string{"anywhere"});
        def.duration_hours = std::clamp(node.value("duration_hours", 0.25F), 0.05F, 2.0F);
        def.complex = node.value("complex", false);
        scene.scripted_behavior.definitions[name] = def;
    }

    scene.scripted_behavior.definitions_loaded = true;
}

float UpdateIntervalSeconds(const Scene& scene) {
    const std::string& mode = scene.optimization_overrides.lightweight_mode;
    if (mode == "performance") {
        return 1.2F;
    }
    if (mode == "quality") {
        return 0.25F;
    }
    return 0.5F;
}

}  // namespace

void ScriptedBehaviorSystem::EnsureDefaults(Scene& scene) {
    if (!scene.scripted_behavior.enabled) {
        return;
    }

    SeedDefaultDefinitions(scene);
    if (!scene.scripted_behavior.definitions_loaded) {
        TryLoadDefinitionsFromJson(scene);
    }

    scene.scripted_behavior.update_accumulator_seconds = std::max(0.0F, scene.scripted_behavior.update_accumulator_seconds);
}

void ScriptedBehaviorSystem::Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    if (!scene.scripted_behavior.enabled) {
        return;
    }

    const float safe_dt = std::max(0.0F, dt_seconds);
    scene.scripted_behavior.update_accumulator_seconds += safe_dt;
    const float interval = UpdateIntervalSeconds(scene);
    if (scene.scripted_behavior.update_accumulator_seconds < interval) {
        return;
    }
    scene.scripted_behavior.update_accumulator_seconds = 0.0F;
    ++scene.scripted_behavior.update_tick;

    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";

    for (Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        ScriptedBehaviorComponent& scripted = entity.scripted_behavior;
        if (!scripted.enabled || scripted.current_state.empty()) {
            continue;
        }

        auto def_it = scene.scripted_behavior.definitions.find(scripted.current_state);
        if (def_it == scene.scripted_behavior.definitions.end()) {
            continue;
        }

        const ScriptedBehaviorDefinition& def = def_it->second;
        if (performance_mode && def.complex) {
            continue;
        }

        float duration_value = def.duration_hours;
        const auto duration_it = scripted.parameters.find("duration_hours");
        if (duration_it != scripted.parameters.end()) {
            duration_value = duration_it->second;
        }
        const float duration = std::clamp(duration_value, 0.05F, 2.0F);

        const bool applied = NPCController::ForceActivity(scene, entity.id, def.activity, def.location, duration);
        if (!applied) {
            continue;
        }

        if (scripted.schedule_override) {
            entity.schedule.current_activity = def.activity;
            entity.schedule.current_location = def.location;
        }
    }
}

bool ScriptedBehaviorSystem::SetBehavior(
    Scene& scene,
    std::uint64_t entity_id,
    const std::string& state,
    const std::map<std::string, float>& parameters,
    bool schedule_override,
    std::uint64_t target_entity_id) {
    Entity* entity = FindNpc(scene, entity_id);
    if (entity == nullptr || state.empty()) {
        return false;
    }

    EnsureDefaults(scene);

    entity->scripted_behavior.enabled = true;
    entity->scripted_behavior.current_state = state;
    entity->scripted_behavior.target_entity_id = target_entity_id;
    entity->scripted_behavior.schedule_override = schedule_override;
    entity->scripted_behavior.parameters = parameters;

    auto def_it = scene.scripted_behavior.definitions.find(state);
    if (def_it != scene.scripted_behavior.definitions.end()) {
        const ScriptedBehaviorDefinition& def = def_it->second;
        entity->schedule.current_activity = def.activity;
        entity->schedule.current_location = def.location;
    }

    return true;
}

std::vector<std::string> ScriptedBehaviorSystem::ListBehaviors(const Scene& scene) {
    std::vector<std::string> rows{};
    rows.reserve(scene.entities.size());

    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        const ScriptedBehaviorComponent& scripted = entity.scripted_behavior;
        if (!scripted.enabled || scripted.current_state.empty()) {
            rows.push_back("NPC " + std::to_string(entity.id) + " scripted=off");
            continue;
        }

        rows.push_back(
            "NPC " + std::to_string(entity.id) + " scripted=" + scripted.current_state +
            " override=" + (scripted.schedule_override ? "on" : "off") +
            " target=" + std::to_string(scripted.target_entity_id));
    }

    return rows;
}
