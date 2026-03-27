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

struct RAGNarrativeFlavor {
    std::string checkpoint{"none"};
    std::string dialog_tone{"neutral"};
    std::string msq_branch{"default"};
    std::string event_color{"grounded"};
    float similarity = -1.0F;
    std::string source{"none"};
};

namespace RAGSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
[[nodiscard]] std::optional<RAGSparkDirective> RetrieveSparkFlavor(Scene& scene, const Entity& npc);
[[nodiscard]] std::optional<RAGNarrativeFlavor> RetrieveNarrativeFlavor(
    Scene& scene,
    const std::string& checkpoint,
    const Entity* focus_npc = nullptr);
[[nodiscard]] bool EvaluateNarrativeCheckpoint(Scene& scene, const std::string& checkpoint);
[[nodiscard]] std::string BuildDebugSummary(Scene& scene, std::uint64_t npc_id);

}  // namespace RAGSystem
