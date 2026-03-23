#pragma once

#include "Scene.h"
#include "Timer.h"
#include "VulkanRenderer.h"

class Engine {
public:
    void Run();

private:
    void Init();
    void Update(float dt_seconds);
    void Shutdown();

    VulkanRenderer renderer_{};
    Scene scene_{};
    Timer timer_{};
    float elapsed_seconds_ = 0.0F;
};
