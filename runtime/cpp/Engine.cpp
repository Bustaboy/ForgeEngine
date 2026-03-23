#include "Engine.h"

#include "Logger.h"
#include "SceneLoader.h"

#include <GLFW/glfw3.h>
#include <glm/geometric.hpp>
#include <glm/vec2.hpp>
#include <algorithm>
#include <array>
#include <chrono>
#include <string>
#include <thread>

void Engine::Run() {
    Init();

    InputManager input{};
    input.AttachWindow(renderer_.GetWindow());

    constexpr double fixed_dt = 1.0 / 60.0;
    double accumulator = 0.0;
    auto previous_time = std::chrono::steady_clock::now();

    while (!renderer_.ShouldClose()) {
        timer_.BeginFrame();
        renderer_.DrawFPSOverlay(static_cast<float>(timer_.Fps()));
        renderer_.PollEvents();
        input.BeginFrame();

        if (input.IsKeyPressed(GLFW_KEY_ESCAPE)) {
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
            Update(static_cast<float>(fixed_dt), input);
            accumulator -= fixed_dt;
        }

        renderer_.RenderFrame(scene_, camera_);

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
    camera_.position = {0.0F, 0.0F, 3.0F};
    camera_.yaw = -90.0F;
    camera_.pitch = 0.0F;
    camera_.aspect_ratio = renderer_.GetAspectRatio();
    camera_.smoothedYaw = camera_.yaw;
    camera_.smoothedPitch = camera_.pitch;
    camera_.smoothedPosition = camera_.position;
    GF_LOG_INFO("Render loop started");
}

void Engine::Update(float dt_seconds, const InputManager& input) {
    constexpr float look_sensitivity = 0.09F;
    constexpr float move_speed = 3.5F;
    constexpr float smoothing = 10.0F;

    const glm::vec2 mouse_delta = input.MouseDelta();
    camera_.yaw += mouse_delta.x * look_sensitivity;
    camera_.pitch -= mouse_delta.y * look_sensitivity;
    camera_.pitch = std::clamp(camera_.pitch, -89.0F, 89.0F);

    const glm::vec3 forward = camera_.Forward();
    const glm::vec3 forward_flat = glm::normalize(glm::vec3(forward.x, 0.0F, forward.z));
    const glm::vec3 right = glm::normalize(glm::cross(forward_flat, glm::vec3(0.0F, 1.0F, 0.0F)));

    glm::vec3 input_direction{0.0F, 0.0F, 0.0F};
    if (input.IsKeyPressed(GLFW_KEY_W)) {
        input_direction += forward_flat;
    }
    if (input.IsKeyPressed(GLFW_KEY_S)) {
        input_direction -= forward_flat;
    }
    if (input.IsKeyPressed(GLFW_KEY_D)) {
        input_direction += right;
    }
    if (input.IsKeyPressed(GLFW_KEY_A)) {
        input_direction -= right;
    }

    if (glm::dot(input_direction, input_direction) > 0.0F) {
        input_direction = glm::normalize(input_direction);
    }

    const glm::vec3 target_velocity = input_direction * move_speed;
    const float blend = std::min(1.0F, smoothing * dt_seconds);
    camera_velocity_ += (target_velocity - camera_velocity_) * blend;
    camera_.position += camera_velocity_ * dt_seconds;
    camera_.aspect_ratio = renderer_.GetAspectRatio();
    CameraInputState camera_input_state{};
    camera_input_state.mouse_delta = mouse_delta;
    camera_input_state.raw_position = camera_.position;
    camera_input_state.horizontal_speed = glm::length(glm::vec2(camera_velocity_.x, camera_velocity_.z));
    camera_.Update(dt_seconds, camera_input_state);

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
