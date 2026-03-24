#include "WeatherSystem.h"

#include "RelationshipSystem.h"
#include "Scene.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <string>

namespace {

struct WeatherProfile {
    const char* weather_id;
    float movement_speed_multiplier;
    float economy_supply_multiplier;
    float economy_demand_multiplier;
    float relationship_delta_per_day;
    const char* dialog_tone;
    float light_multiplier;
};

constexpr std::array<WeatherProfile, 7> kProfiles = {{
    {"sunny", 1.00F, 1.02F, 0.98F, 0.15F, "warm", 1.00F},
    {"rain", 0.86F, 0.95F, 1.03F, -0.10F, "reflective", 0.88F},
    {"storm", 0.72F, 0.84F, 1.15F, -0.22F, "tense", 0.72F},
    {"snow", 0.74F, 0.88F, 1.11F, -0.15F, "reserved", 0.78F},
    {"sandstorm", 0.66F, 0.82F, 1.18F, -0.25F, "curt", 0.68F},
    {"fog", 0.81F, 0.93F, 1.06F, -0.08F, "hushed", 0.80F},
    {"windy", 0.90F, 0.96F, 1.01F, 0.03F, "brisk", 0.94F},
}};

const WeatherProfile& ProfileFor(const std::string& weather) {
    const auto it = std::find_if(kProfiles.begin(), kProfiles.end(), [&](const WeatherProfile& candidate) {
        return weather == candidate.weather_id;
    });
    return it != kProfiles.end() ? *it : kProfiles.front();
}

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

float Blend(float current, float target, float t) {
    return current + (target - current) * Clamp01(t);
}

std::string NormalizeBiome(const std::string& biome) {
    if (biome.empty()) {
        return "temperate";
    }
    return biome;
}

std::string SelectNextWeather(const Scene& scene, const std::vector<std::string>& types) {
    if (types.empty()) {
        return "sunny";
    }

    std::size_t current_index = 0;
    for (std::size_t i = 0; i < types.size(); ++i) {
        if (types[i] == scene.weather.current_weather) {
            current_index = i;
            break;
        }
    }

    const std::size_t phase = static_cast<std::size_t>(scene.day_count + static_cast<std::uint32_t>(types.size()));
    const std::size_t step = 1U + (phase % (types.size() > 1U ? (types.size() - 1U) : 1U));
    return types[(current_index + step) % types.size()];
}

void RefreshDerivedModifiers(Scene& scene) {
    const WeatherProfile& current = ProfileFor(scene.weather.current_weather);
    const WeatherProfile& target = ProfileFor(scene.weather.target_weather);
    const float blend = scene.weather.transition_progress;

    scene.weather.movement_speed_multiplier = Blend(current.movement_speed_multiplier, target.movement_speed_multiplier, blend);
    scene.weather.economy_supply_multiplier = Blend(current.economy_supply_multiplier, target.economy_supply_multiplier, blend);
    scene.weather.economy_demand_multiplier = Blend(current.economy_demand_multiplier, target.economy_demand_multiplier, blend);
    scene.weather.relationship_delta_per_day = Blend(current.relationship_delta_per_day, target.relationship_delta_per_day, blend);
    scene.weather.dialog_tone = blend >= 0.5F ? target.dialog_tone : current.dialog_tone;

    scene.weather.light_multiplier = std::clamp(Blend(current.light_multiplier, target.light_multiplier, blend), 0.55F, 1.05F);
}

}  // namespace

namespace WeatherSystem {

std::vector<std::string> BiomeWeatherTypes(const std::string& biome) {
    const std::string normalized = NormalizeBiome(biome);
    if (normalized.find("desert") != std::string::npos || normalized.find("arid") != std::string::npos) {
        return {"sunny", "windy", "sandstorm", "fog"};
    }
    if (normalized.find("tundra") != std::string::npos || normalized.find("snow") != std::string::npos || normalized.find("alpine") != std::string::npos) {
        return {"snow", "windy", "sunny", "fog", "storm"};
    }
    if (normalized.find("coast") != std::string::npos || normalized.find("island") != std::string::npos || normalized.find("swamp") != std::string::npos) {
        return {"rain", "storm", "fog", "sunny", "windy"};
    }
    return {"sunny", "rain", "storm", "fog", "windy", "snow"};
}

void EnsureDefaults(Scene& scene) {
    auto& weather = scene.weather;
    const std::vector<std::string> options = BiomeWeatherTypes(scene.biome);
    const auto matches_option = [&](const std::string& value) {
        return std::find(options.begin(), options.end(), value) != options.end();
    };

    if (weather.current_weather.empty() || !matches_option(weather.current_weather)) {
        weather.current_weather = options.empty() ? "sunny" : options.front();
    }
    if (weather.target_weather.empty() || !matches_option(weather.target_weather)) {
        weather.target_weather = weather.current_weather;
    }

    weather.transition_progress = Clamp01(weather.transition_progress);
    weather.intensity = std::clamp(weather.intensity, 0.0F, 1.0F);
    weather.seconds_until_next_transition = std::max(5.0F, weather.seconds_until_next_transition);
    weather.transition_duration_seconds = std::max(2.0F, weather.transition_duration_seconds);
    weather.last_relationship_day_applied = std::max(1U, weather.last_relationship_day_applied);
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);
    auto& weather = scene.weather;

    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.5F);
    weather.seconds_until_next_transition -= safe_dt;
    if (weather.seconds_until_next_transition <= 0.0F) {
        const std::vector<std::string> options = BiomeWeatherTypes(scene.biome);
        weather.target_weather = SelectNextWeather(scene, options);
        weather.transition_progress = 0.0F;
        weather.seconds_until_next_transition = 48.0F + static_cast<float>((scene.day_count % 5U) * 9U);
    }

    if (weather.transition_progress < 1.0F) {
        weather.transition_progress = Clamp01(weather.transition_progress + (safe_dt / weather.transition_duration_seconds));
        if (weather.transition_progress >= 1.0F) {
            weather.current_weather = weather.target_weather;
            weather.intensity = std::clamp(0.25F + (scene.day_progress * 0.45F), 0.1F, 0.9F);
        }
    }

    if (scene.day_count > weather.last_relationship_day_applied) {
        const std::uint32_t days_elapsed = scene.day_count - weather.last_relationship_day_applied;
        const float total_delta = weather.relationship_delta_per_day * static_cast<float>(days_elapsed);
        if (std::abs(total_delta) > 0.0001F) {
            for (auto& [npc_id, _] : scene.relationships) {
                (void)npc_id;
                RelationshipSystem::SetDimension(scene, npc_id, "respect", total_delta * 0.6F, false);
                RelationshipSystem::SetDimension(scene, npc_id, "trust", total_delta, false);
                if (total_delta < 0.0F) {
                    RelationshipSystem::SetDimension(scene, npc_id, "grudge", std::abs(total_delta) * 0.7F, false);
                }
            }
        }
        weather.last_relationship_day_applied = scene.day_count;
    }

    RefreshDerivedModifiers(scene);
}

float MovementSpeedMultiplier(const Scene& scene) {
    return std::clamp(scene.weather.movement_speed_multiplier, 0.5F, 1.15F);
}

float EconomySupplyMultiplier(const Scene& scene) {
    return std::clamp(scene.weather.economy_supply_multiplier, 0.6F, 1.2F);
}

float EconomyDemandMultiplier(const Scene& scene) {
    return std::clamp(scene.weather.economy_demand_multiplier, 0.7F, 1.3F);
}

std::string DialogTone(const Scene& scene) {
    return scene.weather.dialog_tone.empty() ? "neutral" : scene.weather.dialog_tone;
}

std::string ApplyDialogTone(const Scene& scene, const std::string& line) {
    const std::string tone = DialogTone(scene);
    if (tone == "neutral" || line.empty()) {
        return line;
    }
    return "(" + tone + ") " + line;
}

}  // namespace WeatherSystem
