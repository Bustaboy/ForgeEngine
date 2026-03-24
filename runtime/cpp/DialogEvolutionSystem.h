#pragma once

#include <cstdint>
#include <string>

struct DialogChoice;
struct DialogNode;
struct Entity;
struct Scene;

namespace DialogEvolutionSystem {

void RecordPlayerChoice(Scene& scene, Entity& npc, const DialogNode& node, const DialogChoice& choice);
void RecordWorldEvent(Scene& scene, Entity& npc, const std::string& event_label);
bool EvolveNpcDialog(Scene& scene, std::uint64_t npc_id, const std::string& trigger_reason, bool consume_co_creator_queue);

}  // namespace DialogEvolutionSystem
