#pragma once

#include <cstdint>
#include <string>

struct DialogChoice;
struct DialogEffect;
struct Entity;
struct Scene;

namespace RelationshipSystem {

constexpr float kRelationshipDimensionMin = -100.0F;
constexpr float kRelationshipDimensionMax = 100.0F;

void EnsureSceneRelationships(Scene& scene);
void Update(Scene& scene, float dt_seconds);
void EnsureNpcRelationship(Scene& scene, const Entity& npc);
void EnsureNpcRelationship(Scene& scene, std::uint64_t npc_id);
[[nodiscard]] float GetDimension(const Scene& scene, std::uint64_t npc_id, const std::string& dimension);
[[nodiscard]] float CompositeScore(const Scene& scene, std::uint64_t npc_id);
[[nodiscard]] std::string Summary(const Scene& scene, std::uint64_t npc_id);
void ApplyDialogEffect(Scene& scene, std::uint64_t npc_id, const DialogEffect& effect, const std::string& context);
[[nodiscard]] bool ChoicePassesRelationshipGate(
    const Scene& scene,
    std::uint64_t npc_id,
    const DialogChoice& choice,
    std::string& out_reason);
[[nodiscard]] int ApplyTradeAdjustmentForEntity(const Scene& scene, const Entity& npc, int base_amount);
void OnFactionReputationChanged(Scene& scene, const std::string& faction_id, float delta);
[[nodiscard]] bool SetDimension(Scene& scene, std::uint64_t npc_id, const std::string& dimension, float value, bool absolute);

}  // namespace RelationshipSystem

