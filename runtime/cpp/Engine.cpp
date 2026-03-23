#include "Engine.h"

#include "Logger.h"

#include <array>
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

    constexpr std::array<float, 5> kInitialX = {-0.85F, -0.45F, 0.0F, 0.45F, 0.85F};
    constexpr std::array<float, 5> kVelocityX = {0.30F, 0.25F, 0.20F, 0.15F, 0.10F};
    constexpr std::array<float, 5> kScale = {0.18F, 0.16F, 0.20F, 0.14F, 0.17F};
    constexpr std::array<glm::vec4, 5> kBaseColors = {
        glm::vec4(0.95F, 0.35F, 0.35F, 1.0F),
        glm::vec4(0.35F, 0.95F, 0.45F, 1.0F),
        glm::vec4(0.35F, 0.55F, 0.95F, 1.0F),
        glm::vec4(0.95F, 0.85F, 0.35F, 1.0F),
        glm::vec4(0.85F, 0.35F, 0.95F, 1.0F),
    };

    for (std::size_t i = 0; i < kInitialX.size(); ++i) {
        Entity entity{};
        entity.id = i + 1;
        entity.transform.pos = {kInitialX[i], 0.0F, 0.0F};
        entity.transform.scale = {kScale[i], kScale[i], 1.0F};
        entity.renderable.color = kBaseColors[i];
        entity.velocity = {kVelocityX[i], 0.0F, 0.0F};
        scene_.entities.push_back(entity);
    }

    renderer_.Init();
    GF_LOG_INFO("Render loop started");
}

void Engine::Update(float dt_seconds) {
    elapsed_seconds_ += dt_seconds;

    for (std::size_t i = 0; i < scene_.entities.size(); ++i) {
        Entity& entity = scene_.entities[i];

        entity.transform.pos.x += entity.velocity.x * dt_seconds;
        entity.transform.pos.y = std::sin((elapsed_seconds_ * 1.35F) + static_cast<float>(i) * 0.85F) * 0.45F;
        entity.transform.rot.z = elapsed_seconds_ * (0.3F + static_cast<float>(i) * 0.15F);

        if (entity.transform.pos.x > 1.2F) {
            entity.transform.pos.x = -1.2F;
        }

        const float pulse_r = 0.5F + 0.5F * std::sin(elapsed_seconds_ * (0.9F + static_cast<float>(i) * 0.1F));
        const float pulse_g = 0.5F + 0.5F * std::sin(elapsed_seconds_ * (1.1F + static_cast<float>(i) * 0.07F));
        const float pulse_b = 0.5F + 0.5F * std::sin(elapsed_seconds_ * (1.3F + static_cast<float>(i) * 0.05F));

        entity.renderable.color.r = 0.25F + 0.75F * pulse_r;
        entity.renderable.color.g = 0.25F + 0.75F * pulse_g;
        entity.renderable.color.b = 0.25F + 0.75F * pulse_b;
        entity.renderable.color.a = 1.0F;
    }
}

void Engine::Shutdown() {
    renderer_.Shutdown();
    GF_LOG_INFO("Runtime shutdown complete");
    Logger::Shutdown();
}
