#pragma once

#include "BuildingSystem.h"
#include "EconomySystem.h"
#include "Entity.h"
#include "NavmeshSystem.h"

#include <glm/vec3.hpp>
#include <glm/vec2.hpp>
#include <glm/vec4.hpp>

#include <string>
#include <cstdint>
#include <cstddef>
#include <deque>
#include <map>
#include <optional>
#include <vector>

// Memory guardrail caps shared between Scene.cpp (enforcement) and Engine.cpp (reporting).
namespace SceneLimits {
    inline constexpr std::size_t kRecentActionsCap = 160U;
    inline constexpr std::size_t kFreeWillMapCap   = 512U;
    inline constexpr std::size_t kCombatStateCap   = 48U;
}

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

struct StoryBibleEntry {
    std::string id{};
    std::string title{};
    std::string summary{};
    std::vector<std::string> tags{};
};

struct StoryBeat {
    std::string id{};
    std::string title{};
    std::string summary{};
    std::vector<std::string> next_ids{};
    bool completed = false;
    bool cutscene_trigger = false;
};

struct StoryRipple {
    std::string type{};
    std::string target_id{};
    std::string dimension{};
    float value = 0.0F;
    std::string reason{};
};

struct StoryEvent {
    std::string event_id{};
    std::string beat_id{};
    std::string title{};
    std::string summary{};
    std::string narrator_line{};
    std::vector<StoryRipple> ripples{};
    bool applied = false;
};

struct StoryState {
    std::vector<StoryBibleEntry> lore_entries{};
    std::vector<StoryBibleEntry> major_npcs{};
    std::vector<StoryBibleEntry> key_events{};
    std::vector<StoryBibleEntry> faction_notes{};
    std::vector<StoryBeat> campaign_beats{};
    std::vector<StoryEvent> pending_events{};
    std::vector<std::string> event_history{};
};

struct NarratorState {
    bool enabled = true;
    std::string voice_id = "default";
    VoiceProfileComponent voice_profile{};
    std::deque<std::string> pending_lines{};
    std::vector<std::string> spoken_history{};
};

struct CutsceneState {
    bool enabled = true;
    bool active = false;
    std::string source_beat_id{};
    std::string source_event_id{};
    std::uint64_t target_npc_id = 0;
    std::string target_npc_animation = "idle";
    glm::vec3 camera_start{0.0F, 1.7F, -4.0F};
    glm::vec3 camera_mid{0.0F, 2.2F, -2.0F};
    glm::vec3 camera_end{0.0F, 1.8F, -1.0F};
    float duration_seconds = 4.0F;
    float elapsed_seconds = 0.0F;
    std::string narrator_line{};
    std::vector<std::string> played_cutscene_history{};
};

struct WeatherState {
    std::string current_weather = "sunny";
    std::string target_weather = "sunny";
    float transition_progress = 1.0F;
    float intensity = 0.25F;
    float seconds_until_next_transition = 75.0F;
    float transition_duration_seconds = 14.0F;
    float movement_speed_multiplier = 1.0F;
    float economy_supply_multiplier = 1.0F;
    float economy_demand_multiplier = 1.0F;
    float relationship_delta_per_day = 0.0F;
    float light_multiplier = 1.0F;
    std::string dialog_tone = "neutral";
    std::uint32_t last_relationship_day_applied = 1;
};


struct FreeWillSparkRequest {
    std::uint64_t npc_id = 0;
    bool forced_by_console = false;
};

struct FreeWillState {
    bool enabled = true;
    bool llm_enabled = true;
    std::string model_path = "";
    std::uint32_t max_sparks_per_npc_per_day = 3;
    float min_seconds_between_global_sparks = 1.0F;
    float spark_chance_per_second = 0.0015F;
    std::uint32_t last_processed_day = 1;
    float global_cooldown_remaining = 0.0F;
    std::uint32_t rng_seed = 0xC0FFEEU;
    std::map<std::uint64_t, std::uint32_t> daily_spark_count{};
    std::map<std::uint64_t, std::string> last_spark_line_by_npc{};
    std::deque<FreeWillSparkRequest> pending_sparks{};
};


struct CombatUnitState {
    std::uint64_t entity_id = 0;
    std::uint32_t team_id = 0;
    glm::vec3 world_position{0.0F, 0.0F, 0.0F};
    float health = 0.0F;
    float max_health = 0.0F;
    float stamina = 0.0F;
    float max_stamina = 0.0F;
    float attack_cooldown_seconds = 0.0F;
    float dodge_cooldown_seconds = 0.0F;
    std::string action_state = "idle";
    bool alive = true;
};

struct CombatState {
    bool active = false;
    bool combat_mode_enabled = false;
    std::uint32_t grid_width = 8;
    std::uint32_t grid_height = 8;
    std::vector<CombatUnitState> units{};
    float input_move_x = 0.0F;
    float input_move_z = 0.0F;
    std::string queued_action{};
    std::string queued_target{};
    std::string trigger_source{};
    std::string last_resolution{};
};

struct SettlementState {
    std::string village_name = "River Town";
    std::uint32_t total_population = 0;
    std::map<std::string, float> shared_resources{{"food", 80.0F}, {"stockpile", 45.0F}, {"wood", 20.0F}, {"stone", 16.0F}};
    float morale = 62.0F;
    float tick_interval_seconds = 6.0F;
    float accumulated_tick_seconds = 0.0F;
};

struct WorldTime {
    float elapsed_seconds = 0.0F;
    float day_progress = 0.25F;
    float day_cycle_speed = 0.01F;
    std::uint32_t day_count = 1;
    std::uint32_t minutes_per_day = 1440;
};

struct SceneSprite2D {
    std::string asset_id{};
    std::string entity_type{};
    glm::vec2 position{0.0F, 0.0F};
    glm::vec2 size{1.0F, 1.0F};
    glm::vec4 tint{1.0F, 1.0F, 1.0F, 1.0F};
    float rotation_radians = 0.0F;
    float layer = 0.0F;
};

struct SceneTilemap2D {
    std::string id{};
    std::string tileset_asset_id{};
    glm::vec2 origin{0.0F, 0.0F};
    glm::vec2 tile_size{1.0F, 1.0F};
    int columns = 0;
    int rows = 0;
    float layer = -0.1F;
    std::vector<int> tiles{};
};

struct SceneCamera2D {
    glm::vec2 center{0.0F, 0.0F};
    glm::vec2 viewport_world_size{32.0F, 18.0F};
    float pixels_per_unit = 32.0F;
    bool pixel_snap = true;
};

struct SceneRender2D {
    std::string render_mode = "2D";
    bool enabled = false;
    SceneCamera2D camera{};
    std::map<std::string, std::string> entity_sprite_map{};
    std::vector<SceneSprite2D> sprites{};
    std::vector<SceneTilemap2D> tilemaps{};
};



struct ScenePostProcessingSettings {
    bool enabled = false;
    bool bloom_enabled = true;
    bool vignette_enabled = true;
    bool color_grading_enabled = true;
    bool outline_enabled = false;
    float bloom_strength = 0.20F;
    float vignette_strength = 0.18F;
    float color_grade_saturation = 1.0F;
    float color_grade_contrast = 1.0F;
    float outline_strength = 0.35F;
};

struct SceneQualityMetadata {
    int score = 0;
    float estimated_vram_mb = 0.0F;
    int sprite_count = 0;
    int vram_warning_threshold_mb = 768;
    int sprite_warning_threshold = 280;
    std::vector<std::string> warnings{};
};

struct RuntimeOptimizationSettings {
    bool enabled = false;
    bool lod_distance_culling_enabled = false;
    bool draw_call_batching_enabled = false;
    bool shader_variant_cache_enabled = false;
    bool memory_guardrails_enabled = false;
    bool texture_atlas_enabled = false;
    bool texture_compression_enabled = false;
    float lod_near_distance_m = 16.0F;
    float lod_far_distance_m = 42.0F;
    float sprite_cull_distance_m = 52.0F;
    float mesh_cull_distance_m = 70.0F;
    int safe_entity_count = 1400;
    int safe_texture_count = 320;
    int safe_vram_mb = 2048;
    std::string texture_atlas_manifest = "Assets/Generated/runtime_atlas/atlas_manifest.v1.json";
    std::string shader_variant_manifest = "Assets/Generated/shaders/shader_variant_cache.v1.json";
};

struct SceneOptimizationOverrides {
    int project_health_score = 50;
    std::string lightweight_mode = "balanced";
    struct GuardrailSettings {
        bool hard_block_enabled = false;
        int soft_warning_threshold = 50;
        int hard_block_threshold = 30;
    } guardrails{};
    RuntimeOptimizationSettings runtime{};
};

struct Scene {
    std::vector<Entity> entities{};
    Inventory player_inventory{};
    std::map<std::uint64_t, float> npc_relationships{};
    std::uint64_t active_dialog_npc_id = 0;
    WorldTime world_time{};
    float elapsed_seconds = 0.0F;
    float day_progress = 0.25F;
    float day_cycle_speed = 0.01F;
    std::uint32_t day_count = 1;
    std::string biome = "temperate";
    std::string world_style_guide = "grounded stylized frontier";
    WeatherState weather{};
    SettlementState settlement{};
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
    StoryState story{};
    NarratorState narrator{};
    CutsceneState cutscene{};
    FreeWillState free_will{};
    CombatState combat{};
    SceneRender2D render_2d{};
    ScenePostProcessingSettings post_processing{};
    SceneQualityMetadata quality_metadata{};
    SceneOptimizationOverrides optimization_overrides{};

    void Update(float dt_seconds);
    [[nodiscard]] bool ToggleBuildMode();
    [[nodiscard]] std::optional<Entity> GetBuildGhostPreviewFromRay(
        const glm::vec3& ray_origin,
        const glm::vec3& ray_direction) const;
    [[nodiscard]] bool TryPlaceBuildingFromRay(const glm::vec3& ray_origin, const glm::vec3& ray_direction);
    bool Save(const std::string& path) const;
    bool Load(const std::string& path);
    bool ApplyPatch(const std::string& patch_json);
    void MarkNavmeshDirty();
    [[nodiscard]] std::uint32_t MinuteOfDay() const;
};
