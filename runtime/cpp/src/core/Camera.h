#pragma once

#include <glm/ext/matrix_clip_space.hpp>
#include <glm/ext/matrix_transform.hpp>
#include <glm/geometric.hpp>
#include <glm/mat4x4.hpp>
#include <glm/vec3.hpp>

#include <cmath>

struct Camera {
    glm::vec3 position{0.0F, 0.0F, 3.0F};
    float yaw = -90.0F;
    float pitch = 0.0F;
    float fov_degrees = 70.0F;
    float aspect_ratio = 16.0F / 9.0F;
    float near_plane = 0.1F;
    float far_plane = 200.0F;

    [[nodiscard]] glm::vec3 Forward() const {
        const float yaw_radians = glm::radians(yaw);
        const float pitch_radians = glm::radians(pitch);

        glm::vec3 forward{};
        forward.x = std::cos(yaw_radians) * std::cos(pitch_radians);
        forward.y = std::sin(pitch_radians);
        forward.z = std::sin(yaw_radians) * std::cos(pitch_radians);
        return glm::normalize(forward);
    }

    [[nodiscard]] glm::mat4 GetViewMatrix() const {
        return glm::lookAt(position, position + Forward(), glm::vec3(0.0F, 1.0F, 0.0F));
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
