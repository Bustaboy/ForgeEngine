#include "LivingNpcSystem.h"

#include <algorithm>

namespace {
float ClampNeed(float value) {
    return std::clamp(value, 0.0F, 100.0F);
}

std::uint32_t MinuteOfDay(const Scene& scene) {
    const std::uint32_t minutes_per_day = std::max(1U, scene.world_time.minutes_per_day);
    const float minute_float = std::clamp(scene.world_time.day_progress, 0.0F, 1.0F) * static_cast<float>(minutes_per_day);
    const auto minute = static_cast<std::uint32_t>(minute_float);
    return std::min(minutes_per_day - 1, minute);
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
}

void LivingNpcSystem::EnsureDefaults(Scene& scene) {
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
                ScheduleEntry{1020U, 1320U, "free_time", "town"},
                ScheduleEntry{1320U, 1440U, "sleep", "home"},
            };
        }
    }
}

void LivingNpcSystem::Update(Scene& scene, float dt_seconds) {
    const float safe_dt = std::max(0.0F, dt_seconds);
    const std::uint32_t minute_of_day = MinuteOfDay(scene);

    for (Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        const ScheduleEntry* active_entry = ActiveScheduleEntry(entity.schedule, minute_of_day);
        if (active_entry != nullptr) {
            entity.schedule.current_activity = active_entry->activity;
            entity.schedule.current_location = active_entry->location;
        } else {
            entity.schedule.current_activity = "idle";
            entity.schedule.current_location = "anywhere";
        }

        const std::string& activity = entity.schedule.current_activity;
        const float hunger_delta = (activity == "sleep" ? 0.6F : 1.6F) * safe_dt;
        entity.needs.hunger = ClampNeed(entity.needs.hunger + hunger_delta);

        if (activity == "sleep") {
            entity.needs.energy = ClampNeed(entity.needs.energy + 4.0F * safe_dt);
        } else if (activity == "work") {
            entity.needs.energy = ClampNeed(entity.needs.energy - 1.8F * safe_dt);
        } else {
            entity.needs.energy = ClampNeed(entity.needs.energy - 0.8F * safe_dt);
        }

        if (activity == "free_time") {
            entity.needs.social = ClampNeed(entity.needs.social + 1.3F * safe_dt);
            entity.needs.fun = ClampNeed(entity.needs.fun + 1.5F * safe_dt);
        } else {
            entity.needs.social = ClampNeed(entity.needs.social - 0.7F * safe_dt);
            entity.needs.fun = ClampNeed(entity.needs.fun - 0.9F * safe_dt);
        }
    }
}
