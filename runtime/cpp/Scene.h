#pragma once

#include "Entity.h"

#include <string>
#include <vector>

struct Scene {
    std::vector<Entity> entities{};
    float elapsed_seconds = 0.0F;

    void Update(float dt_seconds);
    bool Save(const std::string& path) const;
    bool Load(const std::string& path);
};
