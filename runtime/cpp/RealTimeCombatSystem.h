#pragma once

#include <cstdint>
#include <string>

#include <glm/vec2.hpp>

struct Scene;

namespace RealTimeCombatSystem {

struct InputFrame {
    glm::vec2 move_axis{0.0F, 0.0F};
    bool attack_pressed = false;
    bool dodge_pressed = false;
    bool stop_pressed = false;
};

void EnsureDefaults(Scene& scene);
void SetInput(Scene& scene, const InputFrame& input);
void Update(Scene& scene, float dt_seconds);
bool Start(Scene& scene, const std::string& source);
bool QueueAction(Scene& scene, const std::string& action, std::string& out_message);
bool HitTest(Scene& scene, std::uint64_t entity_id, std::string& out_message);

}  // namespace RealTimeCombatSystem
