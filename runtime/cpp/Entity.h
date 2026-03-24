#pragma once

#include <glm/vec3.hpp>
#include <glm/vec4.hpp>
#include <glm/vec2.hpp>

#include <cstdint>
#include <string>

struct Transform {
    glm::vec3 pos{0.0F, 0.0F, 0.0F};
    glm::vec3 rot{0.0F, 0.0F, 0.0F};
    glm::vec3 scale{0.20F, 0.20F, 1.0F};
};

struct Renderable {
    glm::vec4 color{0.35F, 0.85F, 0.65F, 1.0F};
};

struct BuildableComponent {
    std::string type{};
    glm::ivec2 grid_size{1, 1};

    [[nodiscard]] bool IsValid() const {
        return !type.empty();
    }
};

struct Entity {
    std::uint64_t id = 0;
    Transform transform{};
    Renderable renderable{};
    BuildableComponent buildable{};
    glm::vec3 velocity{0.0F, 0.0F, 0.0F};
};

struct DirectionalLight {
    glm::vec3 direction{0.0F, -1.0F, 0.0F};
    glm::vec3 color{1.0F, 1.0F, 1.0F};
    float intensity = 1.0F;
};
