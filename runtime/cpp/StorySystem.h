#pragma once

struct Scene;

namespace StorySystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
bool TriggerEventById(Scene& scene, const char* event_id);

}  // namespace StorySystem
