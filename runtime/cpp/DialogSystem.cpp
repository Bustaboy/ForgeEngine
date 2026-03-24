#include "DialogSystem.h"

#include "DialogEvolutionSystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "InventorySystem.h"
#include "Logger.h"
#include "VoiceSystem.h"
#include "RelationshipSystem.h"
#include "Scene.h"
#include "WeatherSystem.h"
#include "CombatSystem.h"

#include <glm/geometric.hpp>
#include <algorithm>
#include <cmath>
#include <string>
#include <vector>

namespace {
Entity* FindEntityById(Scene& scene, std::uint64_t id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == id) {
            return &entity;
        }
    }
    return nullptr;
}

const DialogNode* FindNodeById(const DialogComponent& dialog, const std::string& node_id) {
    for (const DialogNode& node : dialog.nodes) {
        if (node.id == node_id) {
            return &node;
        }
    }
    return nullptr;
}

void LogNode(Scene& scene, std::uint64_t npc_id, const DialogNode& node) {
    const std::string toned_line = WeatherSystem::ApplyDialogTone(scene, node.text);
    GF_LOG_INFO("NPC: " + toned_line);
    VoiceSystem::QueueNpcLine(scene, npc_id, toned_line, "dialog_node");
    for (std::size_t i = 0; i < node.choices.size(); ++i) {
        GF_LOG_INFO(std::to_string(i + 1U) + ") " + node.choices[i].text);
    }
}

bool ApplyEffect(Scene& scene, std::uint64_t npc_id, const DialogEffect& effect) {
    bool any_effect_applied = false;

    if (!effect.inventory_item.empty() && effect.inventory_delta != 0) {
        if (effect.inventory_delta > 0) {
            if (InventorySystem::AddItem(scene.player_inventory, effect.inventory_item, effect.inventory_delta)) {
                GF_LOG_INFO("Received " + std::to_string(effect.inventory_delta) + " " + effect.inventory_item + ".");
                any_effect_applied = true;
            }
        } else {
            const int remove_amount = std::abs(effect.inventory_delta);
            if (InventorySystem::RemoveItem(scene.player_inventory, effect.inventory_item, remove_amount)) {
                GF_LOG_INFO("Removed " + std::to_string(remove_amount) + " " + effect.inventory_item + ".");
                any_effect_applied = true;
            } else {
                GF_LOG_INFO("Not enough " + effect.inventory_item + " to remove.");
            }
        }
    }

    RelationshipSystem::ApplyDialogEffect(scene, npc_id, effect, "dialog_effect");
    if (effect.relationship_delta != 0.0F) {
        GF_LOG_INFO("Relationship changed. " + RelationshipSystem::Summary(scene, npc_id));
        any_effect_applied = true;
    }

    if (any_effect_applied) {
        GF_LOG_INFO(InventorySystem::InventorySummary(scene.player_inventory));
    }

    return any_effect_applied;
}

bool StartDialogWithNpc(Scene& scene, Entity& npc) {
    if (!npc.dialog.IsValid()) {
        return false;
    }

    const std::string start_node_id = npc.dialog.start_node_id.empty() ? npc.dialog.nodes.front().id : npc.dialog.start_node_id;
    const DialogNode* start_node = FindNodeById(npc.dialog, start_node_id);
    if (start_node == nullptr) {
        return false;
    }

    scene.active_dialog_npc_id = npc.id;
    npc.dialog.active_node_id = start_node->id;
    npc.dialog.in_progress = true;
    RelationshipSystem::EnsureNpcRelationship(scene, npc);

    GF_LOG_INFO("Started dialog with NPC #" + std::to_string(npc.id) + ".");
    if (!npc.faction.faction_id.empty()) {
        const std::string tone = FactionSystem::DialogToneForEntity(scene, npc);
        const float reputation = FactionSystem::GetReputation(scene, npc.faction.faction_id);
        GF_LOG_INFO("Faction tone: " + tone + " (reputation: " + std::to_string(reputation) + ").");
    }
    GF_LOG_INFO("Weather tone: " + WeatherSystem::DialogTone(scene) + " (" + scene.weather.current_weather + ").");
    LogNode(scene, npc.id, *start_node);
    return true;
}

void EndDialog(Scene& scene, Entity& npc) {
    npc.dialog.in_progress = false;
    npc.dialog.active_node_id.clear();
    scene.active_dialog_npc_id = 0;
    GF_LOG_INFO("Dialog ended.");
}
}  // namespace

namespace DialogSystem {

bool TryStartDialog(Scene& scene, const glm::vec3& player_position, float max_interaction_distance) {
    if (scene.active_dialog_npc_id != 0) {
        Entity* active_npc = FindEntityById(scene, scene.active_dialog_npc_id);
        if (active_npc == nullptr || !active_npc->dialog.in_progress) {
            scene.active_dialog_npc_id = 0;
        } else {
            const glm::vec3 delta = active_npc->transform.pos - player_position;
            if (glm::dot(delta, delta) <= (max_interaction_distance * max_interaction_distance)) {
                const DialogNode* current = FindNodeById(active_npc->dialog, active_npc->dialog.active_node_id);
                if (current != nullptr) {
                    LogNode(scene, active_npc->id, *current);
                }
                return true;
            }
            EndDialog(scene, *active_npc);
        }
    }

    float best_distance_squared = max_interaction_distance * max_interaction_distance;
    Entity* best_npc = nullptr;
    for (Entity& entity : scene.entities) {
        if (!entity.dialog.IsValid()) {
            continue;
        }

        const glm::vec3 delta = entity.transform.pos - player_position;
        const float distance_squared = glm::dot(delta, delta);
        if (distance_squared <= best_distance_squared) {
            best_distance_squared = distance_squared;
            best_npc = &entity;
        }
    }

    if (best_npc == nullptr) {
        GF_LOG_INFO("No NPC in interaction range.");
        return false;
    }

    return StartDialogWithNpc(scene, *best_npc);
}

bool HandleChoiceInput(Scene& scene, int choice_index) {
    if (scene.active_dialog_npc_id == 0 || choice_index < 0) {
        return false;
    }

    Entity* npc = FindEntityById(scene, scene.active_dialog_npc_id);
    if (npc == nullptr || !npc->dialog.in_progress) {
        scene.active_dialog_npc_id = 0;
        return false;
    }

    const DialogNode* current_node = FindNodeById(npc->dialog, npc->dialog.active_node_id);
    if (current_node == nullptr) {
        EndDialog(scene, *npc);
        return false;
    }

    if (choice_index >= static_cast<int>(current_node->choices.size())) {
        GF_LOG_INFO("Invalid dialog choice.");
        LogNode(scene, npc->id, *current_node);
        return false;
    }

    const DialogChoice& choice = current_node->choices[choice_index];
    if (!choice.required_faction_id.empty()) {
        const float reputation = FactionSystem::GetReputation(scene, choice.required_faction_id);
        if (reputation < choice.min_required_reputation) {
            GF_LOG_INFO(
                "Choice locked. Requires faction reputation " + std::to_string(choice.min_required_reputation) +
                " with " + choice.required_faction_id + ".");
            LogNode(scene, npc->id, *current_node);
            return false;
        }
    }
    std::string relationship_reason;
    if (!RelationshipSystem::ChoicePassesRelationshipGate(scene, npc->id, choice, relationship_reason)) {
        GF_LOG_INFO(relationship_reason);
        LogNode(scene, npc->id, *current_node);
        return false;
    }

    GF_LOG_INFO("Player: " + choice.text);
    DialogEvolutionSystem::RecordPlayerChoice(scene, *npc, *current_node, choice);
    if (!npc->dialog.past_choices.empty() && (npc->dialog.past_choices.size() % 3U) == 0U) {
        DialogEvolutionSystem::EvolveNpcDialog(scene, npc->id, "choice_memory_threshold", true);
    }
    DialogEffect adjusted_effect = choice.effect;
    if (adjusted_effect.inventory_delta > 0) {
        adjusted_effect.inventory_delta = FactionSystem::ApplyTradeAdjustmentForEntity(scene, *npc, adjusted_effect.inventory_delta);
        adjusted_effect.inventory_delta = RelationshipSystem::ApplyTradeAdjustmentForEntity(scene, *npc, adjusted_effect.inventory_delta);
        if (!adjusted_effect.inventory_item.empty()) {
            const float market_price = EconomySystem::PriceFor(scene, adjusted_effect.inventory_item);
            const float scarcity_scale = std::clamp(6.0F / std::max(0.5F, market_price), 0.45F, 1.8F);
            adjusted_effect.inventory_delta = std::max(1, static_cast<int>(std::round(adjusted_effect.inventory_delta * scarcity_scale)));
        }
    }
    ApplyEffect(scene, npc->id, adjusted_effect);
    if (!npc->faction.faction_id.empty() && adjusted_effect.relationship_delta != 0.0F) {
        FactionSystem::AddPlayerReputation(scene, npc->faction.faction_id, adjusted_effect.relationship_delta, "dialog");
    }

    if (choice.next_node_id.rfind("combat:", 0) == 0) {
        std::vector<std::uint64_t> participants{};
        participants.push_back(npc->id);
        for (const Entity& entity : scene.entities) {
            if (entity.buildable.IsValid() || entity.id == npc->id) {
                continue;
            }
            participants.insert(participants.begin(), entity.id);
            break;
        }
        const bool started = CombatSystem::StartEncounter(scene, participants, 8U, 8U, "dialog_choice");
        GF_LOG_INFO(started ? "Dialog escalated into combat." : "Dialog combat trigger failed.");
        EndDialog(scene, *npc);
        return started;
    }

    if (choice.next_node_id.empty()) {
        EndDialog(scene, *npc);
        return true;
    }

    const DialogNode* next_node = FindNodeById(npc->dialog, choice.next_node_id);
    if (next_node == nullptr) {
        EndDialog(scene, *npc);
        return true;
    }

    npc->dialog.active_node_id = next_node->id;
    if (next_node->choices.empty()) {
        GF_LOG_INFO("NPC: " + next_node->text);
        VoiceSystem::QueueNpcLine(scene, npc->id, next_node->text, "dialog_terminal");
        EndDialog(scene, *npc);
        return true;
    }

    LogNode(scene, npc->id, *next_node);
    return true;
}

}  // namespace DialogSystem
