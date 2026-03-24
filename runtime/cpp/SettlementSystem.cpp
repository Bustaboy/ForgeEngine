#include "SettlementSystem.h"

#include "EconomySystem.h"
#include "Scene.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <map>
#include <string>

namespace {

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

float ClampMorale(float value) {
    return std::clamp(value, 0.0F, 100.0F);
}

float ResourceValue(const std::map<std::string, float>& resources, const std::string& key, float fallback = 0.0F) {
    const auto it = resources.find(key);
    if (it == resources.end()) {
        return fallback;
    }
    return it->second;
}

std::string NormalizeType(const std::string& type) {
    std::string normalized = type;
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return normalized;
}

}  // namespace

namespace SettlementSystem {

void EnsureDefaults(Scene& scene) {
    auto& settlement = scene.settlement;
    if (settlement.village_name.empty()) {
        settlement.village_name = "River Town";
    }
    settlement.total_population = std::max(0U, settlement.total_population);
    settlement.morale = ClampMorale(settlement.morale);
    settlement.accumulated_tick_seconds = std::max(0.0F, settlement.accumulated_tick_seconds);
    settlement.tick_interval_seconds = std::clamp(settlement.tick_interval_seconds, 1.0F, 60.0F);

    settlement.shared_resources.try_emplace("food", 80.0F);
    settlement.shared_resources.try_emplace("stockpile", 45.0F);
    settlement.shared_resources.try_emplace("wood", 20.0F);
    settlement.shared_resources.try_emplace("stone", 16.0F);

    for (auto& [resource, amount] : settlement.shared_resources) {
        (void)resource;
        amount = std::max(0.0F, amount);
    }
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    auto& settlement = scene.settlement;

    const int npc_count_estimate = static_cast<int>(std::count_if(scene.entities.begin(), scene.entities.end(), [](const Entity& entity) {
        return !entity.buildable.IsValid();
    }));
    const float scale = npc_count_estimate >= 250 ? 1.8F : (npc_count_estimate >= 120 ? 1.35F : 1.0F);
    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.5F);
    settlement.accumulated_tick_seconds += safe_dt;
    const float tick_seconds = std::clamp(std::max(1.0F, settlement.tick_interval_seconds * scale), 1.0F, 120.0F);
    if (settlement.accumulated_tick_seconds < tick_seconds) {
        return;
    }
    settlement.accumulated_tick_seconds = std::fmod(settlement.accumulated_tick_seconds, tick_seconds);

    int npc_count = 0;
    float building_food = 0.0F;
    float building_stockpile = 0.0F;
    float building_morale = 0.0F;
    int house_capacity = 0;

    for (const Entity& entity : scene.entities) {
        if (!entity.buildable.IsValid()) {
            ++npc_count;
            continue;
        }

        const std::string type = NormalizeType(entity.buildable.type);
        if (type.find("farm") != std::string::npos || type.find("garden") != std::string::npos) {
            building_food += 1.2F;
            building_morale += 0.3F;
        }
        if (type.find("house") != std::string::npos || type.find("hut") != std::string::npos) {
            house_capacity += 3;
            building_stockpile += 0.7F;
            building_morale += 0.8F;
        }
        if (type.find("depot") != std::string::npos || type.find("store") != std::string::npos || type.find("stock") != std::string::npos) {
            building_stockpile += 1.6F;
            building_morale += 0.2F;
        }
        if (type.find("waystation") != std::string::npos || type.find("market") != std::string::npos) {
            building_stockpile += 0.8F;
            building_morale += 1.0F;
        }
    }

    settlement.total_population = static_cast<std::uint32_t>(std::max(0, npc_count));

    float npc_food = 0.0F;
    float npc_stockpile = 0.0F;
    float npc_morale = 0.0F;
    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }

        const std::string job = NormalizeType(entity.schedule.job_id);
        const std::string activity = NormalizeType(entity.schedule.current_activity);
        const bool at_work = activity == "work" || activity == "commute";

        if (job.find("farm") != std::string::npos || job.find("grow") != std::string::npos) {
            npc_food += at_work ? 0.7F : 0.25F;
        } else if (job.find("build") != std::string::npos || job.find("craft") != std::string::npos || job.find("smith") != std::string::npos) {
            npc_stockpile += at_work ? 0.8F : 0.2F;
        } else if (job.find("guard") != std::string::npos || job.find("watch") != std::string::npos) {
            npc_morale += 0.25F;
            npc_stockpile += at_work ? 0.15F : 0.05F;
        } else if (job != "unassigned") {
            npc_stockpile += at_work ? 0.35F : 0.1F;
        }

        npc_morale += (entity.needs.social >= 55.0F ? 0.08F : -0.10F);
        npc_morale += (entity.needs.energy >= 45.0F ? 0.06F : -0.15F);
        npc_morale -= (entity.needs.hunger > 75.0F ? 0.25F : 0.02F);
    }

    const float weather_supply_scale = std::clamp(scene.weather.economy_supply_multiplier, 0.7F, 1.2F);
    const float weather_morale_penalty = std::clamp(scene.weather.relationship_delta_per_day * 7.0F, -3.0F, 2.0F);

    const float food_gain = (building_food + npc_food) * weather_supply_scale;
    const float stockpile_gain = (building_stockpile + npc_stockpile) * ((weather_supply_scale + 1.0F) * 0.5F);
    const float food_consumption = static_cast<float>(settlement.total_population) * 0.30F;

    settlement.shared_resources["food"] = std::max(0.0F, ResourceValue(settlement.shared_resources, "food") + food_gain - food_consumption);
    settlement.shared_resources["stockpile"] = std::max(0.0F, ResourceValue(settlement.shared_resources, "stockpile") + stockpile_gain);

    settlement.shared_resources["wood"] = std::max(0.0F, ResourceValue(settlement.shared_resources, "wood") + stockpile_gain * 0.35F);
    settlement.shared_resources["stone"] = std::max(0.0F, ResourceValue(settlement.shared_resources, "stone") + stockpile_gain * 0.25F);

    const float food_pressure = ResourceValue(settlement.shared_resources, "food") < (static_cast<float>(settlement.total_population) * 4.0F) ? -4.0F : 1.0F;
    const float housing_pressure = house_capacity < npc_count ? -2.0F : 0.7F;
    const float morale_target = 52.0F + building_morale + npc_morale + weather_morale_penalty + food_pressure + housing_pressure;
    settlement.morale = ClampMorale((settlement.morale * 0.75F) + (ClampMorale(morale_target) * 0.25F));

    EconomySystem::RegisterProduction(scene, "wood", static_cast<int>(std::round(std::max(0.0F, stockpile_gain * 0.22F))));
    EconomySystem::RegisterProduction(scene, "stone", static_cast<int>(std::round(std::max(0.0F, stockpile_gain * 0.16F))));
    EconomySystem::RegisterProduction(scene, "FarmPlotItem", static_cast<int>(std::round(std::max(0.0F, food_gain * 0.08F))));

    if (food_consumption > food_gain) {
        EconomySystem::RegisterConsumption(scene, "FarmPlotItem", static_cast<int>(std::round((food_consumption - food_gain) * 0.20F)));
    }
}

}  // namespace SettlementSystem
