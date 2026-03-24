#pragma once

#include "BuildingSystem.h"
#include "Entity.h"

#include <glm/vec3.hpp>

#include <string>
#include <cstdint>
#include <vector>

struct Scene {
    std::vector<Entity> entities{};
    float elapsed_seconds = 0.0F;
    float day_progress = 0.25F;
    float day_cycle_speed = 0.01F;
    std::uint32_t day_count = 1;
    bool build_mode_enabled = false;
    DirectionalLight directional_light{};

    void Update(float dt_seconds);
    [[nodiscard]] bool ToggleBuildMode();
    [[nodiscard]] bool TryPlaceBuildingFromRay(const glm::vec3& ray_origin, const glm::vec3& ray_direction);
    bool Save(const std::string& path) const;
    bool Load(const std::string& path);
};
