#pragma once

#include <glm/ext/matrix_clip_space.hpp>
#include <glm/ext/matrix_transform.hpp>
#include <glm/geometric.hpp>
#include <glm/mat4x4.hpp>
#include <glm/vec3.hpp>

#include <algorithm>
#include <cmath>

struct Camera {
    glm::vec3 position{0.0F, 0.0F, 3.0F};
    float yaw = -90.0F;
    float pitch = 0.0F;
    float fov_degrees = 70.0F;
    float aspect_ratio = 16.0F / 9.0F;
    float near_plane = 0.1F;
    float far_plane = 200.0F;

    float position_damping = 14.0F;
    float rotation_damping = 18.0F;
    float head_bob_frequency_hz = 1.85F;
    float head_bob_amplitude = 0.028F;
    float head_bob_lateral_amplitude = 0.009F;
    float head_bob_speed_threshold = 0.08F;

    mutable glm::vec3 smoothed_position{0.0F, 0.0F, 3.0F};
    mutable float smoothed_yaw = -90.0F;
    mutable float smoothed_pitch = 0.0F;
    mutable bool smoothing_initialized = false;
    mutable float head_bob_phase = 0.0F;

    [[nodiscard]] glm::vec3 Forward() const {
        const float yaw_radians = glm::radians(yaw);
        const float pitch_radians = glm::radians(pitch);

        glm::vec3 forward{};
        forward.x = std::cos(yaw_radians) * std::cos(pitch_radians);
        forward.y = std::sin(pitch_radians);
        forward.z = std::sin(yaw_radians) * std::cos(pitch_radians);
        return glm::normalize(forward);
    }

    [[nodiscard]] glm::vec3 SmoothedForward() const {
        const float yaw_radians = glm::radians(smoothed_yaw);
        const float pitch_radians = glm::radians(smoothed_pitch);

        glm::vec3 forward{};
        forward.x = std::cos(yaw_radians) * std::cos(pitch_radians);
        forward.y = std::sin(pitch_radians);
        forward.z = std::sin(yaw_radians) * std::cos(pitch_radians);
        return glm::normalize(forward);
    }

    void UpdateSmoothing(float dt_seconds) const {
        if (!smoothing_initialized) {
            smoothed_position = position;
            smoothed_yaw = yaw;
            smoothed_pitch = pitch;
            smoothing_initialized = true;
            head_bob_phase = 0.0F;
            return;
        }

        const float clamped_dt = std::clamp(dt_seconds, 0.0005F, 0.05F);

        const float position_blend = 1.0F - std::exp(-position_damping * clamped_dt);
        smoothed_position += (position - smoothed_position) * position_blend;

        float yaw_delta = std::fmod(yaw - smoothed_yaw, 360.0F);
        if (yaw_delta > 180.0F) {
            yaw_delta -= 360.0F;
        } else if (yaw_delta < -180.0F) {
            yaw_delta += 360.0F;
        }

        const float rotation_blend = 1.0F - std::exp(-rotation_damping * clamped_dt);
        smoothed_yaw += yaw_delta * rotation_blend;
        smoothed_pitch += (pitch - smoothed_pitch) * rotation_blend;
        smoothed_pitch = std::clamp(smoothed_pitch, -89.0F, 89.0F);

        const glm::vec3 motion = (position - smoothed_position);
        const float speed = glm::length(motion) / clamped_dt;
        if (speed > head_bob_speed_threshold) {
            head_bob_phase += clamped_dt * head_bob_frequency_hz * 6.2831853F;
            if (head_bob_phase > 6.2831853F) {
                head_bob_phase = std::fmod(head_bob_phase, 6.2831853F);
            }
        }
    }

    [[nodiscard]] glm::mat4 GetViewMatrix() const {
        constexpr float kFrameDt = 1.0F / 60.0F;
        UpdateSmoothing(kFrameDt);

        glm::vec3 up{0.0F, 1.0F, 0.0F};
        glm::vec3 bob_offset{0.0F, 0.0F, 0.0F};

        const glm::vec3 forward = SmoothedForward();
        const glm::vec3 right = glm::normalize(glm::cross(forward, up));

        const float bob_vertical = std::sin(head_bob_phase) * head_bob_amplitude;
        const float bob_lateral = std::cos(head_bob_phase * 0.5F) * head_bob_lateral_amplitude;
        bob_offset += up * bob_vertical;
        bob_offset += right * bob_lateral;

        return glm::lookAt(smoothed_position + bob_offset, smoothed_position + bob_offset + forward, up);
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
};
