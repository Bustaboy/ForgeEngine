#include "NPCController.h"

#include "NavmeshSystem.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>

#include <glm/geometric.hpp>
#include <glm/vec2.hpp>

namespace {
float ClampNeed(float value) {
    return std::clamp(value, 0.0F, 100.0F);
}

Entity* FindNpc(Scene& scene, std::uint64_t npc_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == npc_id && !entity.buildable.IsValid()) {
            return &entity;
        }
    }
    return nullptr;
}

std::uint32_t WrappedMinute(std::uint32_t base_minute, std::uint32_t add_minute, std::uint32_t minutes_per_day) {
    const std::uint32_t safe_minutes_per_day = std::max(1U, minutes_per_day);
    return (base_minute + add_minute) % safe_minutes_per_day;
}

const ScheduleEntry* ActiveScheduleEntry(const ScheduleComponent& schedule, std::uint32_t minute_of_day) {
    for (const ScheduleEntry& entry : schedule.daily_schedule) {
        const std::uint32_t start = std::min(entry.start_minute, entry.end_minute);
        const std::uint32_t end = std::max(entry.start_minute, entry.end_minute);
        if (minute_of_day >= start && minute_of_day < end) {
            return &entry;
        }
    }
    return nullptr;
}

float DistanceSq(const glm::vec3& a, const glm::vec3& b) {
    const glm::vec3 delta = a - b;
    return glm::dot(delta, delta);
}

std::size_t CountNpcEntities(const Scene& scene) {
    return static_cast<std::size_t>(std::count_if(scene.entities.begin(), scene.entities.end(), [](const Entity& entity) {
        return !entity.buildable.IsValid();
    }));
}

float OffscreenFastForwardSeconds(const Scene& scene, const Entity& entity, float safe_dt) {
    if (entity.id == scene.active_dialog_npc_id) {
        return safe_dt;
    }

    const std::size_t npc_count = CountNpcEntities(scene);
    const float load_scale = npc_count >= 250U ? 0.45F : (npc_count >= 120U ? 0.65F : 1.0F);
    const float distance_sq = DistanceSq(entity.transform.pos, scene.player_proxy_position);
    if (distance_sq > (100.0F * 100.0F)) {
        return std::clamp(std::max(safe_dt, 3600.0F * load_scale), safe_dt, 2.0F * 3600.0F);
    }
    if (distance_sq > (60.0F * 60.0F)) {
        return std::clamp(std::max(safe_dt, 1800.0F * load_scale), safe_dt, 3600.0F);
    }
    if (distance_sq > (35.0F * 35.0F)) {
        return std::clamp(std::max(safe_dt, 900.0F * load_scale), safe_dt, 1800.0F);
    }
    return safe_dt;
}

glm::vec3 ActivityTarget(const Entity& entity) {
    const std::string& location = entity.schedule.current_location;
    if (location == "home") {
        return entity.schedule.home_position;
    }
    if (location == "work") {
        return entity.schedule.workplace_position;
    }
    if (location == "town") {
        return (entity.schedule.home_position + entity.schedule.workplace_position) * 0.5F;
    }
    return entity.transform.pos;
}

void ApplyActivityOverrides(Entity& entity) {
    if (entity.needs.hunger > 80.0F) {
        entity.schedule.current_activity = "eat";
        entity.schedule.current_location = "home";
        return;
    }
    if (entity.needs.energy < 20.0F) {
        entity.schedule.current_activity = "rest";
        entity.schedule.current_location = "home";
    }
}

void TickNeeds(Entity& entity, float simulated_hours) {
    const std::string& activity = entity.schedule.current_activity;

    const float hunger_rate = (activity == "sleep" || activity == "rest") ? 2.5F : 5.0F;
    entity.needs.hunger = ClampNeed(entity.needs.hunger + hunger_rate * simulated_hours);

    if (activity == "sleep" || activity == "rest") {
        entity.needs.energy = ClampNeed(entity.needs.energy + 18.0F * simulated_hours);
    } else if (activity == "work") {
        entity.needs.energy = ClampNeed(entity.needs.energy - 12.0F * simulated_hours);
    } else {
        entity.needs.energy = ClampNeed(entity.needs.energy - 6.0F * simulated_hours);
    }

    if (activity == "free_time" || activity == "socialize") {
        entity.needs.social = ClampNeed(entity.needs.social + 8.0F * simulated_hours);
        entity.needs.fun = ClampNeed(entity.needs.fun + 10.0F * simulated_hours);
    } else {
        entity.needs.social = ClampNeed(entity.needs.social - 3.5F * simulated_hours);
        entity.needs.fun = ClampNeed(entity.needs.fun - 4.0F * simulated_hours);
    }

    if (activity == "eat") {
        entity.needs.hunger = ClampNeed(entity.needs.hunger - 45.0F * simulated_hours);
    }
}

void ApplyNavigation(Scene& scene, Entity& entity, float simulated_seconds) {
    const glm::vec3 target = ActivityTarget(entity);
    const float distance_sq = DistanceSq(entity.transform.pos, target);
    if (distance_sq <= 0.25F) {
        entity.velocity = glm::vec3(0.0F, entity.velocity.y, 0.0F);
        return;
    }

    const bool offscreen = simulated_seconds >= 3600.0F;
    if (offscreen) {
        entity.transform.pos.x = target.x;
        entity.transform.pos.z = target.z;
        entity.velocity = glm::vec3(0.0F, entity.velocity.y, 0.0F);
        return;
    }

    const bool has_path = NavmeshSystem::BuildPath(scene, entity.id, target, "schedule", entity.schedule.current_activity);
    if (!has_path) {
        const glm::vec3 delta = target - entity.transform.pos;
        const glm::vec2 planar_delta{delta.x, delta.z};
        const float length = glm::length(planar_delta);
        if (length > 0.001F) {
            const glm::vec2 direction = planar_delta / length;
            entity.velocity.x = direction.x * 1.2F;
            entity.velocity.z = direction.y * 1.2F;
        }
        return;
    }

    auto nav_it = scene.npc_navigation.find(entity.id);
    if (nav_it != scene.npc_navigation.end()) {
        nav_it->second.desired_speed = 1.2F;
        entity.velocity = glm::vec3(0.0F, entity.velocity.y, 0.0F);
    }
}

}  // namespace

void NPCController::EnsureDefaults(Scene& scene) {
    for (Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        if (entity.schedule.home_entity_id == 0) {
            entity.schedule.home_position = entity.transform.pos;
        }

        if (entity.schedule.workplace_entity_id == 0 && entity.schedule.job_id == "unassigned") {
            entity.schedule.workplace_position = entity.transform.pos + glm::vec3(0.75F, 0.0F, 0.75F);
        }

        if (entity.schedule.daily_schedule.empty()) {
            entity.schedule.daily_schedule = {
                ScheduleEntry{0U, 360U, "sleep", "home"},
                ScheduleEntry{360U, 480U, "commute", "work"},
                ScheduleEntry{480U, 1020U, "work", "work"},
                ScheduleEntry{1020U, 1140U, "eat", "home"},
                ScheduleEntry{1140U, 1320U, "free_time", "town"},
                ScheduleEntry{1320U, 1440U, "sleep", "home"},
            };
        }
    }
}

void NPCController::Update(Scene& scene, float dt_seconds) {
    const float safe_dt = std::max(0.0F, dt_seconds);
    const std::uint32_t minute_of_day = scene.MinuteOfDay();
    const std::uint32_t minutes_per_day = std::max(60U, scene.world_time.minutes_per_day);

    for (Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        const float simulated_seconds = OffscreenFastForwardSeconds(scene, entity, safe_dt);
        const std::uint32_t delta_minutes = static_cast<std::uint32_t>(
            std::round((simulated_seconds / 60.0F) * std::clamp(scene.world_time.day_cycle_speed * 60.0F, 0.05F, 60.0F)));
        const std::uint32_t effective_minute = WrappedMinute(minute_of_day, delta_minutes, minutes_per_day);

        const bool scripted_override =
            entity.scripted_behavior.enabled &&
            entity.scripted_behavior.schedule_override &&
            !entity.scripted_behavior.current_state.empty();
        if (!scripted_override) {
            const ScheduleEntry* active_entry = ActiveScheduleEntry(entity.schedule, effective_minute);
            if (active_entry != nullptr) {
                entity.schedule.current_activity = active_entry->activity;
                entity.schedule.current_location = active_entry->location;
            } else {
                entity.schedule.current_activity = "idle";
                entity.schedule.current_location = "anywhere";
            }
        }

        ApplyActivityOverrides(entity);
        TickNeeds(entity, simulated_seconds / 3600.0F);
        ApplyNavigation(scene, entity, simulated_seconds);
    }
}

bool NPCController::ForceActivity(
    Scene& scene,
    std::uint64_t npc_id,
    const std::string& activity,
    const std::string& location,
    float duration_hours) {
    Entity* npc = FindNpc(scene, npc_id);
    if (npc == nullptr || activity.empty()) {
        return false;
    }

    npc->schedule.current_activity = activity;
    npc->schedule.current_location = location.empty() ? npc->schedule.current_location : location;

    if (duration_hours > 0.0F) {
        TickNeeds(*npc, duration_hours);
        ApplyActivityOverrides(*npc);
        ApplyNavigation(scene, *npc, duration_hours * 3600.0F);
    }

    return true;
}
