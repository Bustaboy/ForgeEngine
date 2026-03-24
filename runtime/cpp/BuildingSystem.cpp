#include "BuildingSystem.h"

#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <cstddef>

BuildTemplate SelectBuildTemplate(const Scene& scene) {
    std::size_t buildable_count = 0;
    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            ++buildable_count;
        }
    }

    if ((buildable_count % 2U) == 0U) {
        return BuildTemplate{
            "SmallHouse",
            {2, 2},
            {2.0F, 1.0F, 2.0F},
            {0.84F, 0.58F, 0.34F, 1.0F},
        };
    }

    return BuildTemplate{
        "FarmPlot",
        {3, 2},
        {3.0F, 0.35F, 2.0F},
        {0.28F, 0.70F, 0.30F, 1.0F},
    };
}

bool OverlapsOnGroundXZ(const Entity& candidate, const Entity& existing) {
    const float candidate_half_x = std::abs(candidate.transform.scale.x) * 0.5F;
    const float candidate_half_z = std::abs(candidate.transform.scale.z) * 0.5F;
    const float existing_half_x = std::abs(existing.transform.scale.x) * 0.5F;
    const float existing_half_z = std::abs(existing.transform.scale.z) * 0.5F;

    const float dx = std::abs(candidate.transform.pos.x - existing.transform.pos.x);
    const float dz = std::abs(candidate.transform.pos.z - existing.transform.pos.z);
    return dx < (candidate_half_x + existing_half_x) && dz < (candidate_half_z + existing_half_z);
}

std::uint64_t NextEntityId(const Scene& scene) {
    std::uint64_t max_id = 0;
    for (const Entity& entity : scene.entities) {
        max_id = std::max(max_id, entity.id);
    }
    return max_id + 1;
}
