#pragma once

#include <glm/vec3.hpp>
#include <glm/vec4.hpp>

#include <cstdint>

struct Transform {
    glm::vec3 pos{0.0F, 0.0F, 0.0F};
    glm::vec3 rot{0.0F, 0.0F, 0.0F};
    glm::vec3 scale{0.20F, 0.20F, 1.0F};
};

struct Renderable {
    glm::vec4 color{0.35F, 0.85F, 0.65F, 1.0F};
};

struct Entity {
    std::uint64_t id = 0;
    Transform transform{};
    Renderable renderable{};
    glm::vec3 velocity{0.0F, 0.0F, 0.0F};
};
