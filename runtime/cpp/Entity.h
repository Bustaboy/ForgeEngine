#pragma once

#include <array>
#include <cstdint>

struct Transform {
    std::array<float, 3> pos{0.0F, 0.0F, 0.0F};
    std::array<float, 3> rot{0.0F, 0.0F, 0.0F};
    std::array<float, 3> scale{0.20F, 0.20F, 1.0F};
};

struct Renderable {
    std::array<float, 3> color{0.35F, 0.85F, 0.65F};
};

struct Entity {
    std::uint64_t id = 0;
    Transform transform{};
    Renderable renderable{};
    std::array<float, 3> velocity{0.0F, 0.0F, 0.0F};
};
