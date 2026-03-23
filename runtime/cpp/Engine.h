#pragma once

#include "Scene.h"
#include "Timer.h"
#include "VulkanRenderer.h"

#include <string>

class Engine {
public:
    void Run();

private:
    void Init();
    void Update(float dt_seconds);
    void Shutdown();
    void SeedFallbackScene();

    VulkanRenderer renderer_{};
    Scene scene_{};
    Timer timer_{};
    std::string scene_path_ = "scene_scaffold.json";
};
