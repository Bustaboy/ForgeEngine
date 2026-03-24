#pragma once

#include <string>

struct Entity;
struct Scene;

namespace FactionSystem {

constexpr float kReputationMin = -100.0F;
constexpr float kReputationMax = 100.0F;

void EnsureSceneFactions(Scene& scene);
float GetReputation(const Scene& scene, const std::string& faction_id);
float AddPlayerReputation(Scene& scene, const std::string& faction_id, float delta, const std::string& reason);
bool CanBuildInCurrentBiome(const Scene& scene, std::string& out_reason);
std::string DialogToneForEntity(const Scene& scene, const Entity& npc);
int ApplyTradeAdjustmentForEntity(const Scene& scene, const Entity& npc, int base_amount);

}  // namespace FactionSystem
