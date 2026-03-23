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

    float yaw_damping = 14.0F;
    float pitch_damping = 14.0F;
    float position_damping = 10.0F;
    float head_bob_frequency = 8.5F;
    float head_bob_amplitude = 0.035F;

    [[nodiscard]] glm::vec3 Forward() const {
        SyncSmoothedState();
        return DirectionFromAngles(smoothed_yaw_, smoothed_pitch_);
    }

    [[nodiscard]] glm::mat4 GetViewMatrix() const {
        SyncSmoothedState();
        const glm::vec3 bobbed_position = smoothed_position_ + glm::vec3(0.0F, ComputeHeadBobOffset(), 0.0F);
        return glm::lookAt(bobbed_position, bobbed_position + DirectionFromAngles(smoothed_yaw_, smoothed_pitch_), glm::vec3(0.0F, 1.0F, 0.0F));
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
    [[nodiscard]] static glm::vec3 DirectionFromAngles(float yaw_degrees, float pitch_degrees) {
        const float yaw_radians = glm::radians(yaw_degrees);
        const float pitch_radians = glm::radians(pitch_degrees);

        glm::vec3 forward{};
        forward.x = std::cos(yaw_radians) * std::cos(pitch_radians);
        forward.y = std::sin(pitch_radians);
        forward.z = std::sin(yaw_radians) * std::cos(pitch_radians);
        return glm::normalize(forward);
    }

    void SyncSmoothedState() const {
        constexpr float dt = 1.0F / 60.0F;

        if (!smoothing_initialized_) {
            smoothed_position_ = position;
            smoothed_yaw_ = yaw;
            smoothed_pitch_ = pitch;
            previous_target_position_ = position;
            smoothing_initialized_ = true;
            return;
        }

        const float yaw_blend = std::min(1.0F, yaw_damping * dt);
        const float pitch_blend = std::min(1.0F, pitch_damping * dt);
        const float position_blend = std::min(1.0F, position_damping * dt);

        smoothed_yaw_ += (yaw - smoothed_yaw_) * yaw_blend;
        smoothed_pitch_ += (pitch - smoothed_pitch_) * pitch_blend;
        smoothed_position_ += (position - smoothed_position_) * position_blend;

        const glm::vec3 target_delta = position - previous_target_position_;
        const float horizontal_speed = glm::length(glm::vec2(target_delta.x, target_delta.z)) / std::max(dt, 0.0001F);
        previous_target_position_ = position;

        const float bob_activation = std::clamp(horizontal_speed / 3.5F, 0.0F, 1.0F);
        head_bob_time_ += dt * (0.35F + bob_activation);
        head_bob_weight_ += (bob_activation - head_bob_weight_) * std::min(1.0F, 9.0F * dt);
    }

    [[nodiscard]] float ComputeHeadBobOffset() const {
        return std::sin(head_bob_time_ * head_bob_frequency * 6.28318530718F) * head_bob_amplitude * head_bob_weight_;
    }

    mutable bool smoothing_initialized_ = false;
    mutable glm::vec3 smoothed_position_{0.0F, 0.0F, 3.0F};
    mutable glm::vec3 previous_target_position_{0.0F, 0.0F, 3.0F};
    mutable float smoothed_yaw_ = -90.0F;
    mutable float smoothed_pitch_ = 0.0F;
    mutable float head_bob_time_ = 0.0F;
    mutable float head_bob_weight_ = 0.0F;
};
