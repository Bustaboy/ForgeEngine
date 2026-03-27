#pragma once

#include <string>

struct Scene;

namespace AudioSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
bool PlayTrack(Scene& scene, const std::string& bus, const std::string& track, std::string& out_message);
void SetCombatMusicOverride(Scene& scene, bool enabled);
bool SetVolume(Scene& scene, const std::string& bus, float normalized, std::string& out_message);
[[nodiscard]] std::string Summary(const Scene& scene);

}  // namespace AudioSystem
