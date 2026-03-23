#pragma once

#include <GLFW/glfw3.h>
#include <glm/vec2.hpp>

class InputManager {
public:
    InputManager() = default;

    void AttachWindow(GLFWwindow* window);
    void BeginFrame();

    [[nodiscard]] bool IsKeyPressed(int key) const;
    [[nodiscard]] glm::vec2 MouseDelta() const;

private:
    GLFWwindow* window_ = nullptr;
    glm::vec2 mouse_delta_{0.0F, 0.0F};
    double last_mouse_x_ = 0.0;
    double last_mouse_y_ = 0.0;
    bool first_mouse_sample_ = true;
};
