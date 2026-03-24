#pragma once

struct Scene;

namespace SettlementSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);

}  // namespace SettlementSystem
