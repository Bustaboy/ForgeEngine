#pragma once

#include <cstdint>
#include <string>

struct Scene;

class FreeWillSystem {
public:
    static void EnsureDefaults(Scene& scene);
    static void Update(Scene& scene, float dt_seconds);
    static bool TriggerSpark(Scene& scene, std::uint64_t npc_id, bool forced_by_console);
    [[nodiscard]] static std::string BuildHybridDecisionSummary(Scene& scene, std::uint64_t npc_id);
};
