#pragma once

#include "Entity.h"

#include <cstdint>
#include <optional>
#include <string>

struct Scene;

struct BuildTemplate {
    const char* type = "";
    glm::ivec2 grid_size{1, 1};
    glm::vec3 world_scale{1.0F, 1.0F, 1.0F};
    glm::vec4 color{1.0F, 1.0F, 1.0F, 1.0F};
};

BuildTemplate SelectBuildTemplate(const Scene& scene);
bool OverlapsOnGroundXZ(const Entity& candidate, const Entity& existing);
std::uint64_t NextEntityId(const Scene& scene);
[[nodiscard]] std::optional<Entity> GetGhostPreview(const Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction);
void UpdateGhostPreview(Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction);
bool TryPlaceBuildingFromRay(Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction);
