#include "src/input/InputManager.h"

void InputManager::AttachWindow(GLFWwindow* window) {
    window_ = window;
    mouse_delta_ = {0.0F, 0.0F};
    first_mouse_sample_ = true;

    if (window_ != nullptr) {
        glfwSetInputMode(window_, GLFW_CURSOR, GLFW_CURSOR_DISABLED);
        glfwGetCursorPos(window_, &last_mouse_x_, &last_mouse_y_);
    }
}

void InputManager::BeginFrame() {
    mouse_delta_ = {0.0F, 0.0F};

    if (window_ == nullptr) {
        return;
    }

    double mouse_x = 0.0;
    double mouse_y = 0.0;
    glfwGetCursorPos(window_, &mouse_x, &mouse_y);

    if (first_mouse_sample_) {
        last_mouse_x_ = mouse_x;
        last_mouse_y_ = mouse_y;
        first_mouse_sample_ = false;
        return;
    }

    mouse_delta_.x = static_cast<float>(mouse_x - last_mouse_x_);
    mouse_delta_.y = static_cast<float>(mouse_y - last_mouse_y_);
    last_mouse_x_ = mouse_x;
    last_mouse_y_ = mouse_y;
}

bool InputManager::IsKeyPressed(int key) const {
    return window_ != nullptr && glfwGetKey(window_, key) == GLFW_PRESS;
}

glm::vec2 InputManager::MouseDelta() const {
    return mouse_delta_;
}
