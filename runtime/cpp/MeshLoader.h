#pragma once

#include <glm/vec2.hpp>
#include <glm/vec3.hpp>

#include <cstdint>
#include <string>
#include <vector>

struct LoadedMeshVertex {
    glm::vec3 position{0.0F};
    glm::vec3 normal{0.0F, 1.0F, 0.0F};
    glm::vec2 uv{0.0F};
};

struct LoadedMesh {
    std::vector<LoadedMeshVertex> vertices{};
    std::vector<std::uint32_t> indices{};
    glm::vec3 bounds_min{-0.5F, -0.5F, -0.5F};
    glm::vec3 bounds_max{0.5F, 0.5F, 0.5F};
};

class MeshLoader {
public:
    static bool LoadSimpleGltf(const std::string& path, std::uint32_t primitive_index, LoadedMesh& out_mesh, std::string& error);
};
