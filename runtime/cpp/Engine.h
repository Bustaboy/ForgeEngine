#pragma once

#include "Scene.h"
#include "Timer.h"
#include "VulkanRenderer.h"
#include "src/core/Camera.h"
#include "src/input/InputManager.h"

#include <glm/vec3.hpp>

#include <string>

class Engine {
public:
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
    std::string scene_path_ = "scene_scaffold.json";
};
