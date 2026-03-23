#include "Engine.h"

#include "Logger.h"

#include <chrono>
#include <cmath>
#include <string>
#include <thread>

void Engine::Run() {
    Init();

    constexpr double fixed_dt = 1.0 / 60.0;
    double accumulator = 0.0;
    auto previous_time = std::chrono::steady_clock::now();

    while (!renderer_.ShouldClose()) {
        timer_.BeginFrame();
        renderer_.PollEvents();

        const auto now = std::chrono::steady_clock::now();
        const std::chrono::duration<double> frame_delta = now - previous_time;
        previous_time = now;
        accumulator += frame_delta.count();

        while (accumulator >= fixed_dt) {
            Update(static_cast<float>(fixed_dt));
            accumulator -= fixed_dt;
        }

        renderer_.RenderFrame(scene_, elapsed_seconds_);

        if (timer_.ShouldUpdateFps()) {
            renderer_.SetWindowTitle(
                "ForgeEngine Runtime (Vulkan-first) | FPS: " + std::to_string(timer_.Fps()) +
                " | Frame: " + timer_.FrameTimeMsText() + "ms");
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    Shutdown();
}

void Engine::Init() {
    Logger::Init();
    GF_LOG_INFO("ForgeEngine Vulkan runtime initialized");

    Entity rectangle{};
    rectangle.id = 1;
    rectangle.transform.pos = {0.0F, 0.0F, 0.0F};
    rectangle.transform.scale = {0.20F, 0.20F, 1.0F};
    rectangle.renderable.color = {0.35F, 0.85F, 0.65F};
    rectangle.velocity = {0.42F, 0.0F, 0.0F};
    scene_.entities.push_back(rectangle);

    renderer_.Init();
    GF_LOG_INFO("Render loop started");
}

void Engine::Update(float dt_seconds) {
    elapsed_seconds_ += dt_seconds;

    for (Entity& entity : scene_.entities) {
        entity.transform.pos[0] += entity.velocity[0] * dt_seconds;
        entity.transform.pos[1] = std::sin(elapsed_seconds_ * 1.35F) * 0.35F;

        if (entity.transform.pos[0] > 0.82F) {
            entity.transform.pos[0] = -0.82F;
        }

        entity.renderable.color[1] = 0.45F + 0.40F * (std::sin(elapsed_seconds_) + 1.0F) * 0.5F;
    }
}

void Engine::Shutdown() {
    renderer_.Shutdown();
    GF_LOG_INFO("Runtime shutdown complete");
    Logger::Shutdown();
}
