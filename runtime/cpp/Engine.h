#pragma once

#include "Scene.h"
#include "Timer.h"
#include "VulkanRenderer.h"
#include "src/core/Camera.h"
#include "src/input/InputManager.h"

#include <glm/vec3.hpp>

#include <array>
#include <cstdint>
#include <string>

class Engine {
public:
    void RunHeadlessSmoke();
    struct PerfGuardrailsState {
        bool low_power_mode = false;
        double smoothed_frame_seconds = 1.0 / 60.0;
        double current_fixed_dt_seconds = 1.0 / 60.0;
        double frame_budget_seconds = 1.0 / 60.0;
        std::uint64_t total_frames = 0;
        std::uint64_t throttled_frames = 0;
        std::uint64_t dropped_simulation_steps = 0;
        std::uint64_t simulation_steps_last_frame = 0;
    };

    void Run();

private:
    void Init();
    void Update(float dt_seconds, const InputManager& input);
    void Shutdown();
    void SeedFallbackScene();

    VulkanRenderer renderer_{};
    Scene scene_{};
    Timer timer_{};
    Camera camera_{};
    glm::vec3 camera_velocity_{0.0F, 0.0F, 0.0F};
    bool was_build_toggle_pressed_ = false;
    bool was_left_mouse_pressed_ = false;
    bool was_interact_pressed_ = false;
    std::array<bool, 3> was_dialog_choice_pressed_ = {false, false, false};
    bool debug_overlay_enabled_ = true;
    std::string overlay_status_message_ = "Ready";
    std::string scene_path_ = "scene_scaffold.json";
    PerfGuardrailsState perf_state_{};
};
