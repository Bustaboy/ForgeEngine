#pragma once

#include <glm/vec3.hpp>

struct Scene;

namespace DialogSystem {

bool TryStartDialog(Scene& scene, const glm::vec3& player_position, float max_interaction_distance);
bool HandleChoiceInput(Scene& scene, int choice_index);

}  // namespace DialogSystem
