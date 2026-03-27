#pragma once

#include <cstdint>
#include <optional>
#include <string>

struct Entity;
struct Scene;

struct RAGSparkDirective {
    std::string line{};
    std::string activity{"free_time"};
    std::string location{"town"};
    float duration_hours = 0.25F;
};

namespace RAGSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
[[nodiscard]] std::optional<RAGSparkDirective> RetrieveSparkFlavor(Scene& scene, const Entity& npc);
[[nodiscard]] std::string BuildDebugSummary(Scene& scene, std::uint64_t npc_id);

}  // namespace RAGSystem
