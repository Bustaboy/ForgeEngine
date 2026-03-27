#include "ScriptedBehaviorSystem.h"

#include "Logger.h"
#include "NPCController.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <sstream>
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

bool IsPerformanceLikeMode(const Scene& scene) {
    const std::string& mode = scene.optimization_overrides.lightweight_mode;
    return mode == "performance" || mode == "aggressive" || mode == "ultra";
}

void LogPerformanceDecision(Scene& scene, const std::string& summary) {
    const std::string entry = "[change-log][behavior_perf] " + summary;
    scene.recent_actions.push_back(entry);
    GF_LOG_INFO(entry);
}

void UpdatePerformanceMonitoring(Scene& scene, float dt_seconds) {
    ScriptedBehaviorState& scripted = scene.scripted_behavior;
    if (!scripted.performance_monitoring_enabled) {
        scripted.performance_mode_active = false;
        scripted.force_scripted_fallback = false;
        scripted.effective_spark_chance_multiplier = 1.0F;
        scripted.performance_reason = "monitoring_off";
        scripted.scripted_decisions_window = 0;
        scripted.spark_decisions_window = 0;
        return;
    }

    const float instant_fps = (dt_seconds > 0.0001F) ? (1.0F / dt_seconds) : scripted.monitored_fps;
    scripted.monitored_fps = std::clamp((scripted.monitored_fps * 0.85F) + (instant_fps * 0.15F), 1.0F, 240.0F);
    scripted.monitored_entity_count = static_cast<int>(scene.entities.size());
    scripted.performance_check_accumulator_seconds += std::max(0.0F, dt_seconds);
    if (scripted.performance_check_accumulator_seconds < scripted.performance_check_interval_seconds) {
        return;
    }
    scripted.performance_check_accumulator_seconds = 0.0F;

    const std::uint32_t total_decisions = scripted.scripted_decisions_window + scripted.spark_decisions_window;
    if (total_decisions > 0U) {
        scripted.monitored_scripted_ratio = static_cast<float>(scripted.scripted_decisions_window) / static_cast<float>(total_decisions);
        scripted.monitored_spark_ratio = static_cast<float>(scripted.spark_decisions_window) / static_cast<float>(total_decisions);
    } else {
        scripted.monitored_scripted_ratio = 1.0F;
        scripted.monitored_spark_ratio = 0.0F;
    }
    scripted.scripted_decisions_window = 0;
    scripted.spark_decisions_window = 0;

    const bool hard_fps_drop = scripted.monitored_fps <= scripted.monitoring_hard_fps;
    const bool soft_fps_drop = scripted.monitored_fps <= scripted.monitoring_min_fps;
    const bool hard_entity_over = scripted.monitored_entity_count >= scripted.monitoring_hard_entity_count;
    const bool soft_entity_over = scripted.monitored_entity_count >= scripted.monitoring_soft_entity_count;
    const bool hard_drop = hard_fps_drop || hard_entity_over;
    const bool soft_drop = !hard_drop && (soft_fps_drop || soft_entity_over);
    const bool performance_like_mode = IsPerformanceLikeMode(scene);

    float next_multiplier = 1.0F;
    bool force_fallback = false;
    std::string reason = "stable";
    if (hard_drop) {
        next_multiplier = 0.0F;
        force_fallback = true;
        reason = hard_fps_drop ? "hard_fps_drop" : "hard_entity_overload";
    } else if (soft_drop) {
        next_multiplier = performance_like_mode ? 0.15F : 0.35F;
        reason = soft_fps_drop ? "soft_fps_drop" : "soft_entity_load";
    } else if (performance_like_mode) {
        next_multiplier = 0.55F;
        reason = "lightweight_mode_performance";
    }

    if (scripted.monitored_scripted_ratio < 0.45F) {
        next_multiplier *= 0.7F;
        if (reason == "stable") {
            reason = "scripted_ratio_low";
        }
    }

    next_multiplier = std::clamp(next_multiplier, 0.0F, 1.0F);
    const bool next_active = force_fallback || next_multiplier < 0.999F;
    const bool state_changed = scripted.performance_mode_active != next_active ||
                               scripted.force_scripted_fallback != force_fallback ||
                               std::fabs(scripted.effective_spark_chance_multiplier - next_multiplier) > 0.05F ||
                               scripted.performance_reason != reason;
    scripted.performance_mode_active = next_active;
    scripted.force_scripted_fallback = force_fallback;
    scripted.effective_spark_chance_multiplier = next_multiplier;
    scripted.performance_reason = reason;

    if (state_changed) {
        LogPerformanceDecision(scene, ScriptedBehaviorSystem::BuildPerformanceStatus(scene));
    }
}

}  // namespace

bool ScriptedBehaviorSystem::IsBehaviorSuitable(const Scene& scene, const Entity& entity) {
    const ScriptedBehaviorComponent& scripted = entity.scripted_behavior;
    if (!scripted.enabled || scripted.current_state.empty()) {
        return false;
    }

    const auto def_it = scene.scripted_behavior.definitions.find(scripted.current_state);
    if (def_it == scene.scripted_behavior.definitions.end()) {
        return false;
    }

    const ScriptedBehaviorDefinition& def = def_it->second;
    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    if (performance_mode && def.complex) {
        return false;
    }

    if (scripted.schedule_override) {
        return true;
    }

    const std::string& scheduled_activity = entity.schedule.current_activity;
    if (scheduled_activity.empty() || scheduled_activity == "idle" || scheduled_activity == "free_time") {
        return true;
    }
    return scheduled_activity == def.activity;
}

void ScriptedBehaviorSystem::EnsureDefaults(Scene& scene) {
    if (!scene.scripted_behavior.enabled) {
        return;
    }

    SeedDefaultDefinitions(scene);
    if (!scene.scripted_behavior.definitions_loaded) {
        TryLoadDefinitionsFromJson(scene);
    }

    scene.scripted_behavior.update_accumulator_seconds = std::max(0.0F, scene.scripted_behavior.update_accumulator_seconds);
    scene.scripted_behavior.performance_check_interval_seconds =
        std::clamp(scene.scripted_behavior.performance_check_interval_seconds, 0.25F, 10.0F);
    scene.scripted_behavior.monitoring_min_fps = std::clamp(scene.scripted_behavior.monitoring_min_fps, 10.0F, 240.0F);
    scene.scripted_behavior.monitoring_hard_fps = std::clamp(
        scene.scripted_behavior.monitoring_hard_fps,
        5.0F,
        scene.scripted_behavior.monitoring_min_fps);
    scene.scripted_behavior.monitoring_soft_entity_count = std::max(32, scene.scripted_behavior.monitoring_soft_entity_count);
    scene.scripted_behavior.monitoring_hard_entity_count = std::max(
        scene.scripted_behavior.monitoring_soft_entity_count,
        scene.scripted_behavior.monitoring_hard_entity_count);
}

void ScriptedBehaviorSystem::RefreshDefinitions(Scene& scene) {
    scene.scripted_behavior.definitions.clear();
    scene.scripted_behavior.definitions_loaded = false;
    EnsureDefaults(scene);
}

void ScriptedBehaviorSystem::Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    if (!scene.scripted_behavior.enabled) {
        return;
    }
    UpdatePerformanceMonitoring(scene, std::max(0.0F, dt_seconds));

    const float safe_dt = std::max(0.0F, dt_seconds);
    scene.scripted_behavior.update_accumulator_seconds += safe_dt;
    const float interval = UpdateIntervalSeconds(scene);
    if (scene.scripted_behavior.update_accumulator_seconds < interval) {
        return;
    }
    scene.scripted_behavior.update_accumulator_seconds = 0.0F;
    ++scene.scripted_behavior.update_tick;

    for (Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        ScriptedBehaviorComponent& scripted = entity.scripted_behavior;
        if (!IsBehaviorSuitable(scene, entity)) {
            continue;
        }

        const auto def_it = scene.scripted_behavior.definitions.find(scripted.current_state);
        const ScriptedBehaviorDefinition& def = def_it->second;

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
        ++scene.scripted_behavior.scripted_decisions_window;

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
    entity->scripted_behavior.spark_override_chance = std::clamp(
        entity->scripted_behavior.spark_override_chance,
        0.0F,
        1.0F);
    entity->scripted_behavior.parameters = parameters;

    auto def_it = scene.scripted_behavior.definitions.find(state);
    if (def_it != scene.scripted_behavior.definitions.end()) {
        const ScriptedBehaviorDefinition& def = def_it->second;
        entity->schedule.current_activity = def.activity;
        entity->schedule.current_location = def.location;
    }

    return true;
}

std::vector<std::string> ScriptedBehaviorSystem::ListBehaviors(Scene& scene) {
    EnsureDefaults(scene);

    std::vector<std::string> rows{};
    rows.reserve(scene.entities.size() + scene.scripted_behavior.definitions.size() + 4U);

    rows.push_back("Available scripted states:");
    if (scene.scripted_behavior.definitions.empty()) {
        rows.push_back("  (none)");
    } else {
        for (const auto& [name, def] : scene.scripted_behavior.definitions) {
            rows.push_back(
                "  " + name + " activity=" + def.activity + " location=" + def.location +
                " duration_hours=" + std::to_string(def.duration_hours) +
                " complex=" + (def.complex ? "true" : "false"));
        }
    }

    rows.push_back("NPC scripted behavior preview:");
    rows.push_back("Performance monitor: " + BuildPerformanceStatus(scene));

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

void ScriptedBehaviorSystem::RecordSparkDecision(Scene& scene) {
    ++scene.scripted_behavior.spark_decisions_window;
}

std::string ScriptedBehaviorSystem::BuildPerformanceStatus(const Scene& scene) {
    const ScriptedBehaviorState& state = scene.scripted_behavior;
    std::ostringstream out;
    out << "monitoring=" << (state.performance_monitoring_enabled ? "on" : "off")
        << " perf_mode_active=" << (state.performance_mode_active ? "yes" : "no")
        << " force_scripted=" << (state.force_scripted_fallback ? "yes" : "no")
        << " spark_multiplier=" << state.effective_spark_chance_multiplier
        << " fps=" << state.monitored_fps
        << " entities=" << state.monitored_entity_count
        << " ratio(scripted/spark)=" << state.monitored_scripted_ratio << "/" << state.monitored_spark_ratio
        << " reason=" << state.performance_reason;
    return out.str();
}
