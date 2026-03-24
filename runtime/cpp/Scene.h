#pragma once

#include "BuildingSystem.h"
#include "EconomySystem.h"
#include "Entity.h"
#include "NavmeshSystem.h"

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

struct FactionDefinition {
    std::string id{};
    std::string display_name{};
    std::string category{};
    std::string biome_hint{};
    std::string style_hint{};
    float min_reputation_to_build = -100.0F;
    float dialog_gate_reputation = 0.0F;
    float trade_bonus_threshold = 50.0F;
    std::map<std::string, float> relationships{};
};

struct RelationshipMemory {
    std::string event_id{};
    std::string summary{};
    float trust_delta = 0.0F;
    float respect_delta = 0.0F;
    float grudge_delta = 0.0F;
    float debt_delta = 0.0F;
    float loyalty_delta = 0.0F;
    std::uint32_t day_recorded = 0;
    float decay_per_day = 0.04F;
};

struct RelationshipProfile {
    float trust = 0.0F;
    float respect = 0.0F;
    float grudge = 0.0F;
    float debt = 0.0F;
    float loyalty = 0.0F;
    std::uint32_t last_interaction_day = 0;
    std::vector<RelationshipMemory> memories{};
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
    std::map<std::string, FactionDefinition> factions{};
    std::map<std::string, float> player_reputation{};
    std::map<std::uint64_t, RelationshipProfile> relationships{};
    EconomyState economy{};
    NavmeshData navmesh{};
    std::vector<std::uint64_t> active_npc_ids{};
    std::map<std::uint64_t, NpcNavigationState> npc_navigation{};
    glm::vec3 player_proxy_position{0.0F, 0.0F, 0.0F};
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
    void MarkNavmeshDirty();
};
