#pragma once

#include "Entity.h"

#include <string>

struct Scene;

namespace VoiceSystem {

struct VoiceRuntimeSettings {
    std::string voice_id = "default";
    float pitch = 0.0F;
    float rate = 0.0F;
    float volume = 1.0F;
};

void EnsureDefaults(Scene& scene);
[[nodiscard]] VoiceRuntimeSettings ResolveNarratorVoice(const Scene& scene, const std::string& line);
[[nodiscard]] VoiceRuntimeSettings ResolveNpcVoice(const Scene& scene, const Entity& npc, const std::string& line);
bool QueueNarratorLine(Scene& scene, const std::string& line, const std::string& source_tag);
bool QueueNpcLine(Scene& scene, std::uint64_t npc_id, const std::string& line, const std::string& source_tag);
void Update(Scene& scene, float dt_seconds);

}  // namespace VoiceSystem
