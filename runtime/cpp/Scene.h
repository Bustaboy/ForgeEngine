#pragma once

#include "Entity.h"

#include <string>
#include <cstdint>
#include <vector>

struct Scene {
    std::vector<Entity> entities{};
    float elapsed_seconds = 0.0F;
    float day_progress = 0.25F;
    float day_cycle_speed = 0.01F;
    std::uint32_t day_count = 1;
    DirectionalLight directional_light{};

    void Update(float dt_seconds);
    bool Save(const std::string& path) const;
    bool Load(const std::string& path);
};
