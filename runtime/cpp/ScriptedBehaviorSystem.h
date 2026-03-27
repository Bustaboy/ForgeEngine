#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <vector>

struct Scene;
struct Entity;

class ScriptedBehaviorSystem {
public:
    static void EnsureDefaults(Scene& scene);
    static void RefreshDefinitions(Scene& scene);
    static void Update(Scene& scene, float dt_seconds);
    static void RecordSparkDecision(Scene& scene);
    static bool SetBehavior(
        Scene& scene,
        std::uint64_t entity_id,
        const std::string& state,
        const std::map<std::string, float>& parameters,
        bool schedule_override,
        std::uint64_t target_entity_id);
    [[nodiscard]] static bool IsBehaviorSuitable(const Scene& scene, const Entity& entity);
    [[nodiscard]] static std::string BuildPerformanceStatus(const Scene& scene);
    [[nodiscard]] static std::vector<std::string> ListBehaviors(Scene& scene);
};
