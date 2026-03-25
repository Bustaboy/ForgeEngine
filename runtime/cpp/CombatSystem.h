#pragma once

#include <cstdint>
#include <string>
#include <vector>

struct Scene;

namespace CombatSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
void SetMoveInput(Scene& scene, float input_x, float input_z);
bool StartEncounter(
    Scene& scene,
    const std::vector<std::uint64_t>& participant_ids,
    std::uint32_t grid_width,
    std::uint32_t grid_height,
    const std::string& source);
bool TryAction(Scene& scene, const std::string& action, const std::string& target, std::string& out_message);

}  // namespace CombatSystem
