#pragma once

#include <cstdint>
#include <glm/vec2.hpp>
#include <glm/vec3.hpp>
#include <string>
#include <vector>

struct Scene;

struct NavmeshData {
    float cell_size = 1.0F;
    int min_x = -12;
    int min_z = -12;
    int width = 24;
    int height = 24;
    std::vector<std::uint8_t> walkable{};
    std::uint64_t revision = 0;
    std::size_t last_buildable_count = 0;
    bool dirty = true;

    [[nodiscard]] int Index(int x, int z) const {
        return z * width + x;
    }

    [[nodiscard]] bool IsInside(int x, int z) const {
        return x >= 0 && z >= 0 && x < width && z < height;
    }
};

struct NavPathNode {
    int x = 0;
    int z = 0;
};

struct NpcNavigationState {
    std::string mode = "idle";
    std::string route_id{};
    bool heading_to_destination = true;
    std::vector<NavPathNode> path{};
    std::size_t path_index = 0;
    glm::vec3 target_world{0.0F, 0.0F, 0.0F};
    float desired_speed = 1.4F;
};

namespace NavmeshSystem {

void MarkDirty(Scene& scene);
void Rebuild(Scene& scene);
void Update(Scene& scene, float dt_seconds);
[[nodiscard]] bool BuildPath(Scene& scene, std::uint64_t entity_id, const glm::vec3& target_world, const std::string& mode, const std::string& route_id);

}  // namespace NavmeshSystem
