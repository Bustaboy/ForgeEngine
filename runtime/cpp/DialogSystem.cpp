#include "DialogSystem.h"

#include "FactionSystem.h"
#include "InventorySystem.h"
#include "Logger.h"
#include "Scene.h"

#include <glm/geometric.hpp>
#include <algorithm>
#include <cmath>
#include <string>

namespace {
constexpr float kRelationshipMin = -100.0F;
constexpr float kRelationshipMax = 100.0F;

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

void LogNode(const DialogNode& node) {
    GF_LOG_INFO("NPC: " + node.text);
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

    if (effect.relationship_delta != 0.0F) {
        float& relationship = scene.npc_relationships[npc_id];
        relationship = std::clamp(relationship + effect.relationship_delta, kRelationshipMin, kRelationshipMax);
        GF_LOG_INFO("Relationship changed to " + std::to_string(relationship) + ".");
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
    scene.npc_relationships.try_emplace(npc.id, 0.0F);

    GF_LOG_INFO("Started dialog with NPC #" + std::to_string(npc.id) + ".");
    if (!npc.faction.faction_id.empty()) {
        const std::string tone = FactionSystem::DialogToneForEntity(scene, npc);
        const float reputation = FactionSystem::GetReputation(scene, npc.faction.faction_id);
        GF_LOG_INFO("Faction tone: " + tone + " (reputation: " + std::to_string(reputation) + ").");
    }
    LogNode(*start_node);
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
                    LogNode(*current);
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
        LogNode(*current_node);
        return false;
    }

    const DialogChoice& choice = current_node->choices[choice_index];
    if (!choice.required_faction_id.empty()) {
        const float reputation = FactionSystem::GetReputation(scene, choice.required_faction_id);
        if (reputation < choice.min_required_reputation) {
            GF_LOG_INFO(
                "Choice locked. Requires faction reputation " + std::to_string(choice.min_required_reputation) +
                " with " + choice.required_faction_id + ".");
            LogNode(*current_node);
            return false;
        }
    }

    GF_LOG_INFO("Player: " + choice.text);
    DialogEffect adjusted_effect = choice.effect;
    if (adjusted_effect.inventory_delta > 0) {
        adjusted_effect.inventory_delta = FactionSystem::ApplyTradeAdjustmentForEntity(scene, *npc, adjusted_effect.inventory_delta);
    }
    ApplyEffect(scene, npc->id, adjusted_effect);
    if (!npc->faction.faction_id.empty() && adjusted_effect.relationship_delta != 0.0F) {
        FactionSystem::AddPlayerReputation(scene, npc->faction.faction_id, adjusted_effect.relationship_delta, "dialog");
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
        EndDialog(scene, *npc);
        return true;
    }

    LogNode(*next_node);
    return true;
}

}  // namespace DialogSystem
