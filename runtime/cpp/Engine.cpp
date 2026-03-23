#include "Engine.h"

#include "Logger.h"
#include "SceneLoader.h"

#include <array>
#include <chrono>
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

        if (renderer_.IsKeyPressed(GLFW_KEY_ESCAPE)) {
            const bool saved = scene_.Save(scene_path_);
            if (saved) {
                GF_LOG_INFO("Scene saved: " + scene_path_);
            } else {
                GF_LOG_INFO("Scene save failed: " + scene_path_);
            }
            break;
        }

        const auto now = std::chrono::steady_clock::now();
        const std::chrono::duration<double> frame_delta = now - previous_time;
        previous_time = now;
        accumulator += frame_delta.count();

        while (accumulator >= fixed_dt) {
            Update(static_cast<float>(fixed_dt));
            accumulator -= fixed_dt;
        }

        renderer_.RenderFrame(scene_);

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

    if (SceneLoader::Load(scene_path_, scene_)) {
        GF_LOG_INFO("Loaded scene: " + scene_path_);
    } else {
        SeedFallbackScene();
        GF_LOG_INFO("Using fallback scene entities");
    }

    renderer_.Init();
    GF_LOG_INFO("Render loop started");
}

void Engine::Update(float dt_seconds) {
    scene_.Update(dt_seconds);
}

void Engine::SeedFallbackScene() {
    scene_.entities.clear();
    scene_.elapsed_seconds = 0.0F;

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
}

void Engine::Shutdown() {
    renderer_.Shutdown();
    GF_LOG_INFO("Runtime shutdown complete");
    Logger::Shutdown();
}
