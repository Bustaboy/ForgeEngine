#pragma once

#include <string>

struct Scene;
struct StoryBeat;

namespace CutsceneSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
bool TryStartForBeat(Scene& scene, const StoryBeat& beat, const std::string& source_event_id, const std::string& narrator_line_override);

}  // namespace CutsceneSystem
