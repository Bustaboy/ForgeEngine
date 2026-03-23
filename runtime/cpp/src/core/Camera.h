#pragma once

#include <glm/ext/matrix_clip_space.hpp>
#include <glm/ext/matrix_transform.hpp>
#include <glm/geometric.hpp>
#include <glm/mat4x4.hpp>
#include <glm/vec2.hpp>
#include <glm/vec3.hpp>

#include <algorithm>
#include <cmath>

struct CameraInputState {
    glm::vec2 mouse_delta{0.0F, 0.0F};
    glm::vec3 raw_position{0.0F, 0.0F, 0.0F};
    float horizontal_speed = 0.0F;
};

struct Camera {
    glm::vec3 position{0.0F, 0.0F, 3.0F};
    float yaw = -90.0F;
    float pitch = 0.0F;
    float fov_degrees = 70.0F;
    float aspect_ratio = 16.0F / 9.0F;
    float near_plane = 0.1F;
    float far_plane = 200.0F;

    float dampingFactor = 10.0F;
    float bobAmplitude = 0.015F;
    float bobFrequency = 1.8F;
    float bobPhase = 0.0F;
    float smoothedYaw = -90.0F;
    float smoothedPitch = 0.0F;
    glm::vec3 smoothedPosition{0.0F, 0.0F, 3.0F};

    void Update(float dt, const CameraInputState& input) {
        const float clamped_dt = std::max(0.0F, dt);
        const float damping = std::max(0.0F, dampingFactor);
        const float blend = 1.0F - std::exp(-damping * clamped_dt);

        smoothedYaw += (yaw - smoothedYaw) * blend;
        smoothedPitch += (pitch - smoothedPitch) * blend;
        smoothedPosition += (input.raw_position - smoothedPosition) * blend;

        const float speed_factor = std::clamp(input.horizontal_speed / 3.5F, 0.0F, 1.0F);
        if (speed_factor > 0.001F) {
            bobPhase += clamped_dt * bobFrequency * speed_factor * 6.28318530718F;
            if (bobPhase > 6.28318530718F) {
                bobPhase = std::fmod(bobPhase, 6.28318530718F);
            }
        }

        const float bob_strength = bobAmplitude * speed_factor;
        smoothedPosition.y += std::sin(bobPhase) * bob_strength;
        smoothedPosition.x += std::cos(bobPhase * 0.5F) * bob_strength * 0.35F;
    }

    [[nodiscard]] glm::vec3 Forward() const {
        return ForwardFromAngles(yaw, pitch);
    }

    [[nodiscard]] glm::mat4 GetViewMatrix() const {
        return glm::lookAt(
            smoothedPosition,
            smoothedPosition + ForwardFromAngles(smoothedYaw, smoothedPitch),
            glm::vec3(0.0F, 1.0F, 0.0F));
    }

    [[nodiscard]] glm::mat4 GetProjectionMatrix() const {
        glm::mat4 projection = glm::perspective(
            glm::radians(fov_degrees),
            aspect_ratio,
            near_plane,
            far_plane);
        projection[1][1] *= -1.0F;
        return projection;
    }

private:
    [[nodiscard]] static glm::vec3 ForwardFromAngles(float yaw_degrees, float pitch_degrees) {
        const float yaw_radians = glm::radians(yaw_degrees);
        const float pitch_radians = glm::radians(pitch_degrees);

        glm::vec3 forward{};
        forward.x = std::cos(yaw_radians) * std::cos(pitch_radians);
        forward.y = std::sin(pitch_radians);
        forward.z = std::sin(yaw_radians) * std::cos(pitch_radians);
        return glm::normalize(forward);
    }
};
