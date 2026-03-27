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

struct MeshComponent {
    std::string source{};
    std::uint32_t primitive_index = 0;
    glm::vec3 bounds_min{-0.5F, -0.5F, -0.5F};
    glm::vec3 bounds_max{0.5F, 0.5F, 0.5F};

    [[nodiscard]] bool IsValid() const {
        return !source.empty();
    }
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

struct ScriptedBehaviorComponent {
    bool enabled = false;
    std::string current_state{};
    std::uint64_t target_entity_id = 0;
    bool schedule_override = false;
    std::map<std::string, float> parameters{};
};

struct ProceduralAnimationComponent {
    float motion_phase = 0.0F;
    float left_foot_offset = 0.0F;
    float right_foot_offset = 0.0F;
};

struct RealTimeCombatComponent {
    bool enabled = false;
    bool alive = true;
    std::uint32_t team_id = 0;
    float health = 100.0F;
    float max_health = 100.0F;
    float stamina = 100.0F;
    float max_stamina = 100.0F;
    float move_speed = 4.5F;
    float attack_damage = 12.0F;
    float melee_range = 1.8F;
    float ranged_range = 7.0F;
    bool ranged_enabled = false;
    float attack_cooldown_seconds = 0.55F;
    float dodge_cooldown_seconds = 1.2F;
    float dodge_duration_seconds = 0.18F;
    float hit_reaction_seconds = 0.16F;
    float stamina_regen_per_second = 18.0F;
    float stamina_attack_cost = 22.0F;
    float stamina_dodge_cost = 30.0F;
    std::string weapon_type = "melee";
    float weapon_damage_multiplier = 1.0F;
    float weapon_speed_multiplier = 1.0F;
    float weapon_range_multiplier = 1.0F;
    float light_attack_damage_multiplier = 1.0F;
    float heavy_attack_damage_multiplier = 1.45F;
    float finisher_damage_multiplier = 1.9F;
    float light_attack_stamina_multiplier = 1.0F;
    float heavy_attack_stamina_multiplier = 1.30F;
    float finisher_stamina_multiplier = 1.55F;
    float combo_window_seconds = 0.72F;
    float combo_timer_remaining = 0.0F;
    std::uint32_t combo_step = 0;
    float dodge_invulnerability_seconds = 0.11F;
    float dodge_invulnerability_remaining = 0.0F;
    glm::vec2 dodge_direction{0.0F, 1.0F};
    float attack_cooldown_remaining = 0.0F;
    float dodge_cooldown_remaining = 0.0F;
    float dodge_remaining = 0.0F;
    float hit_reaction_remaining = 0.0F;
    float hit_reaction_timer = 0.0F;
    bool in_cover = false;
    float cover_defense_bonus = 0.16F;
    float cover_accuracy_bonus = 0.12F;
    float cover_search_radius = 3.8F;
    std::uint64_t last_assist_target_id = 0;
    float squad_call_for_help_cooldown = 0.0F;
    std::string action_state = "idle";
    std::string animation_state = "idle";
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
    ScriptedBehaviorComponent scripted_behavior{};
    glm::vec3 velocity{0.0F, 0.0F, 0.0F};
    ProceduralAnimationComponent animation{};
    MeshComponent mesh{};
    RealTimeCombatComponent realtime_combat{};
};

struct DirectionalLight {
    glm::vec3 direction{0.0F, -1.0F, 0.0F};
    glm::vec3 color{1.0F, 1.0F, 1.0F};
    float intensity = 1.0F;
};
