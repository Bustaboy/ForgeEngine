#pragma once

#include <string>
#include <vector>

struct Scene;

namespace WeatherSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
[[nodiscard]] std::vector<std::string> BiomeWeatherTypes(const std::string& biome);
[[nodiscard]] float MovementSpeedMultiplier(const Scene& scene);
[[nodiscard]] float EconomySupplyMultiplier(const Scene& scene);
[[nodiscard]] float EconomyDemandMultiplier(const Scene& scene);
[[nodiscard]] std::string DialogTone(const Scene& scene);
[[nodiscard]] std::string ApplyDialogTone(const Scene& scene, const std::string& line);

}  // namespace WeatherSystem
