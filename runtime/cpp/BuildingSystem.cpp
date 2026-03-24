#include "BuildingSystem.h"

#include "InventorySystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>

namespace {
constexpr float kGroundY = 0.0F;
constexpr float kRayParallelEpsilon = 1e-5F;
constexpr float kMaxPlacementRayDistance = 1000.0F;
constexpr float kGhostAlpha = 0.4F;
constexpr glm::vec4 kGhostValidColor{0.35F, 0.95F, 0.45F, kGhostAlpha};
constexpr glm::vec4 kGhostBlockedColor{0.95F, 0.25F, 0.25F, kGhostAlpha};

std::optional<Entity> BuildCandidateFromRay(const Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction) {
    if (std::abs(ray_direction.y) < kRayParallelEpsilon) {
        return std::nullopt;
    }

    const float t = (kGroundY - ray_origin.y) / ray_direction.y;
    if (t <= 0.0F || t > kMaxPlacementRayDistance || !std::isfinite(t)) {
        return std::nullopt;
    }

    const glm::vec3 hit_point = ray_origin + ray_direction * t;
    Entity candidate{};
    const BuildTemplate build_template = SelectBuildTemplate(scene);
    candidate.id = NextEntityId(scene);
    candidate.transform.pos = {
        std::round(hit_point.x),
        kGroundY,
        std::round(hit_point.z),
    };
    candidate.transform.scale = build_template.world_scale;
    candidate.renderable.color = build_template.color;
    candidate.velocity = {0.0F, 0.0F, 0.0F};
    candidate.buildable.type = build_template.type;
    candidate.buildable.grid_size = build_template.grid_size;
    return candidate;
}

std::string RequiredInventoryItem(const BuildTemplate& build_template) {
    if (std::string(build_template.type) == "SmallHouse") {
        return "SmallHouseItem";
    }
    if (std::string(build_template.type) == "FarmPlot") {
        return "FarmPlotItem";
    }
    return std::string(build_template.type) + "Item";
}

bool IsPlacementBlockedByOverlap(const Scene& scene, const Entity& candidate) {
    for (const Entity& existing : scene.entities) {
        if (OverlapsOnGroundXZ(candidate, existing)) {
            return true;
        }
    }
    return false;
}
}  // namespace

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

std::optional<Entity> GetGhostPreview(const Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction) {
    if (!scene.build_mode_enabled) {
        return std::nullopt;
    }

    std::optional<Entity> candidate = BuildCandidateFromRay(scene, ray_origin, ray_direction);
    if (!candidate.has_value()) {
        return std::nullopt;
    }

    std::string faction_build_reason;
    const bool blocked_by_faction = !FactionSystem::CanBuildInCurrentBiome(scene, faction_build_reason);
    const bool blocked_by_overlap = IsPlacementBlockedByOverlap(scene, candidate.value());
    candidate->renderable.color = (blocked_by_faction || blocked_by_overlap) ? kGhostBlockedColor : kGhostValidColor;
    return candidate;
}

void UpdateGhostPreview(Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction) {
    scene.build_ghost_preview = GetGhostPreview(scene, ray_origin, ray_direction);
}

bool TryPlaceBuildingFromRay(Scene& scene, const glm::vec3& ray_origin, const glm::vec3& ray_direction) {
    if (!scene.build_mode_enabled) {
        return false;
    }

    std::optional<Entity> candidate = BuildCandidateFromRay(scene, ray_origin, ray_direction);
    if (!candidate.has_value()) {
        return false;
    }

    std::string faction_build_reason;
    if (!FactionSystem::CanBuildInCurrentBiome(scene, faction_build_reason)) {
        GF_LOG_INFO(faction_build_reason);
        return false;
    }

    if (IsPlacementBlockedByOverlap(scene, candidate.value())) {
        return false;
    }

    const BuildTemplate build_template = SelectBuildTemplate(scene);
    const std::string item_name = RequiredInventoryItem(build_template);
    constexpr int kPlacementCost = 1;
    if (!InventorySystem::RemoveItem(scene.player_inventory, item_name, kPlacementCost)) {
        GF_LOG_INFO("Not enough resources");
        return false;
    }

    EconomySystem::RegisterConsumption(scene, item_name, kPlacementCost);
    GF_LOG_INFO("Consumed " + item_name + " x" + std::to_string(kPlacementCost));
    scene.entities.push_back(candidate.value());
    scene.MarkNavmeshDirty();
    return true;
}
