#pragma once

#include "BuildingSystem.h"
#include "Entity.h"

#include <glm/vec3.hpp>

#include <string>
#include <cstdint>
#include <deque>
#include <map>
#include <optional>
#include <vector>

struct CoCreatorQueuedMutation {
    std::string suggestion_id{};
    std::string title{};
    std::string why_this_fits{};
    std::string mutation_json{};
};

struct Scene {
    std::vector<Entity> entities{};
    Inventory player_inventory{};
    std::map<std::uint64_t, float> npc_relationships{};
    std::uint64_t active_dialog_npc_id = 0;
    float elapsed_seconds = 0.0F;
    float day_progress = 0.25F;
    float day_cycle_speed = 0.01F;
    std::uint32_t day_count = 1;
    std::string biome = "temperate";
    std::string world_style_guide = "grounded stylized frontier";
    bool build_mode_enabled = false;
    std::optional<Entity> build_ghost_preview{};
    DirectionalLight directional_light{};
    std::vector<std::string> recent_actions{};
    std::deque<CoCreatorQueuedMutation> co_creator_queue{};

    void Update(float dt_seconds);
    [[nodiscard]] bool ToggleBuildMode();
    [[nodiscard]] std::optional<Entity> GetBuildGhostPreviewFromRay(
        const glm::vec3& ray_origin,
        const glm::vec3& ray_direction) const;
    [[nodiscard]] bool TryPlaceBuildingFromRay(const glm::vec3& ray_origin, const glm::vec3& ray_direction);
    bool Save(const std::string& path) const;
    bool Load(const std::string& path);
};
