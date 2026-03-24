#pragma once

#include <glm/vec3.hpp>
#include <glm/vec4.hpp>
#include <glm/vec2.hpp>

#include <cstdint>
#include <map>
#include <string>
#include <vector>

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

struct InventoryComponent {
    std::map<std::string, int> items{};
};

using Inventory = InventoryComponent;

struct FactionComponent {
    std::string faction_id{};
    std::string role{};
};

struct ReputationComponent {
    std::map<std::string, float> values{};
};

struct RelationshipComponent {
    float trust_bias = 0.0F;
    float respect_bias = 0.0F;
    float loyalty_bias = 0.0F;
    float grudge_sensitivity = 1.0F;
    float debt_sensitivity = 1.0F;
};

struct DialogEffect {
    std::string inventory_item{};
    int inventory_delta = 0;
    float relationship_delta = 0.0F;
};

struct DialogChoice {
    std::string text{};
    std::string next_node_id{};
    DialogEffect effect{};
    std::string required_faction_id{};
    float min_required_reputation = -100.0F;
    std::string required_relationship_dimension{};
    float min_required_relationship = -100.0F;
};

struct DialogNode {
    std::string id{};
    std::string text{};
    std::vector<DialogChoice> choices{};

    [[nodiscard]] bool IsValid() const {
        return !id.empty() && !text.empty();
    }
};

struct DialogComponent {
    std::vector<DialogNode> nodes{};
    std::string start_node_id{};
    std::string active_node_id{};
    bool in_progress = false;
    std::vector<std::string> past_choices{};
    std::vector<std::string> world_events{};
    std::map<std::string, float> reputation_influence{};

    [[nodiscard]] bool IsValid() const {
        if (nodes.empty()) {
            return false;
        }
        for (const DialogNode& node : nodes) {
            if (!node.IsValid()) {
                return false;
            }
        }
        return true;
    }
};

struct VoiceProfileComponent {
    std::string profile_id = "auto";
    std::string gender = "neutral";
    std::string build = "average";
    std::string personality = "neutral";
    std::string style = "neutral";
    std::string base_voice_id = "default";
    float pitch = 0.0F;
    float rate = 0.0F;
    float volume = 1.0F;
};

struct ScheduleEntry {
    std::uint32_t start_minute = 0;
    std::uint32_t end_minute = 0;
    std::string activity = "idle";
    std::string location = "anywhere";
};

struct ScheduleComponent {
    std::uint64_t home_entity_id = 0;
    std::uint64_t workplace_entity_id = 0;
    glm::vec3 home_position{0.0F, 0.0F, 0.0F};
    glm::vec3 workplace_position{0.0F, 0.0F, 0.0F};
    std::string job_id = "unassigned";
    std::vector<ScheduleEntry> daily_schedule{};
    std::string current_activity = "idle";
    std::string current_location = "anywhere";
};

struct NeedsComponent {
    float hunger = 20.0F;
    float energy = 80.0F;
    float social = 60.0F;
    float fun = 55.0F;
};


struct ProceduralAnimationComponent {
    float motion_phase = 0.0F;
    float left_foot_offset = 0.0F;
    float right_foot_offset = 0.0F;
};

struct Entity {
    std::uint64_t id = 0;
    Transform transform{};
    Renderable renderable{};
    BuildableComponent buildable{};
    InventoryComponent inventory{};
    FactionComponent faction{};
    ReputationComponent reputation{};
    RelationshipComponent relationship{};
    DialogComponent dialog{};
    VoiceProfileComponent voice_profile{};
    ScheduleComponent schedule{};
    NeedsComponent needs{};
    glm::vec3 velocity{0.0F, 0.0F, 0.0F};
    ProceduralAnimationComponent animation{};
};

struct DirectionalLight {
    glm::vec3 direction{0.0F, -1.0F, 0.0F};
    glm::vec3 color{1.0F, 1.0F, 1.0F};
    float intensity = 1.0F;
};
