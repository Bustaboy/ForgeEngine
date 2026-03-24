#pragma once

#include <cstdint>
#include <string>

struct Scene;

class NPCController {
public:
    static void EnsureDefaults(Scene& scene);
    static void Update(Scene& scene, float dt_seconds);
    static bool ForceActivity(
        Scene& scene,
        std::uint64_t npc_id,
        const std::string& activity,
        const std::string& location,
        float duration_hours);
};
