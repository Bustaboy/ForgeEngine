#include "DialogEvolutionSystem.h"

#include "CoCreatorSystem.h"
#include "FactionSystem.h"
#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cstddef>
#include <nlohmann/json.hpp>

namespace {
using json = nlohmann::json;

Entity* FindEntityById(Scene& scene, std::uint64_t entity_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id) {
            return &entity;
        }
    }
    return nullptr;
}

std::string MakeToneFromReputation(float reputation) {
    if (reputation >= 45.0F) {
        return "warm";
    }
    if (reputation <= -25.0F) {
        return "guarded";
    }
    return "neutral";
}

std::string NextBranchNodeId(const DialogComponent& dialog) {
    return "evolved_" + std::to_string(dialog.nodes.size() + 1U);
}

void TrimMemory(std::vector<std::string>& values, std::size_t max_size) {
    if (values.size() <= max_size) {
        return;
    }
    const std::size_t trim_count = values.size() - max_size;
    values.erase(values.begin(), values.begin() + static_cast<std::ptrdiff_t>(trim_count));
}

bool ApplyCoCreatorBranchMutation(Scene& scene, Entity& npc) {
    CoCreatorQueuedMutation mutation{};
    if (!CoCreatorSystem::PopNextMutation(scene, mutation)) {
        return false;
    }

    json payload;
    try {
        payload = json::parse(mutation.mutation_json);
    } catch (const std::exception&) {
        return false;
    }

    if (!payload.is_object()) {
        return false;
    }

    const std::string type = payload.value("type", std::string{});
    if (type != "dialog_add_branch") {
        return false;
    }

    const std::uint64_t npc_id = payload.value("npc_id", 0ULL);
    if (npc_id != 0ULL && npc_id != npc.id) {
        return false;
    }

    if (!npc.dialog.IsValid()) {
        return false;
    }

    const std::string from_node_id = payload.value(
        "from_node_id",
        npc.dialog.start_node_id.empty() ? npc.dialog.nodes.front().id : npc.dialog.start_node_id);
    const std::string choice_text = payload.value("choice_text", std::string{"Let's continue."});
    const std::string node_text = payload.value("branch_text", std::string{"I have something new to share."});
    const std::string required_faction_id = payload.value("required_faction_id", npc.faction.faction_id);
    const float min_rep = payload.value("min_required_reputation", -100.0F);
    const float rel_delta = payload.value("choice_relationship_delta", 0.5F);

    const std::string next_node_id = NextBranchNodeId(npc.dialog);
    DialogNode new_node{};
    new_node.id = next_node_id;
    new_node.text = node_text;
    new_node.choices = {
        DialogChoice{"I understand.", "", DialogEffect{"", 0, rel_delta}},
    };
    npc.dialog.nodes.push_back(new_node);

    for (DialogNode& node : npc.dialog.nodes) {
        if (node.id != from_node_id) {
            continue;
        }
        node.choices.push_back(DialogChoice{choice_text, next_node_id, DialogEffect{"", 0, 0.0F}, required_faction_id, min_rep});
        npc.dialog.world_events.push_back("co_creator:" + mutation.title);
        TrimMemory(npc.dialog.world_events, 24U);
        return true;
    }

    return false;
}

void CreateProceduralBranch(Scene& scene, Entity& npc, const std::string& trigger_reason) {
    if (!npc.dialog.IsValid()) {
        return;
    }

    const std::string faction_id = npc.faction.faction_id;
    const float reputation = faction_id.empty() ? 0.0F : FactionSystem::GetReputation(scene, faction_id);
    const std::string tone = MakeToneFromReputation(reputation);
    npc.dialog.reputation_influence[faction_id.empty() ? "global" : faction_id] = reputation;

    const std::string memory_choice = npc.dialog.past_choices.empty() ? "your recent actions" : npc.dialog.past_choices.back();
    const std::string memory_event = npc.dialog.world_events.empty() ? "the day is still young" : npc.dialog.world_events.back();
    const std::string next_node_id = NextBranchNodeId(npc.dialog);

    DialogNode branch{};
    branch.id = next_node_id;
    branch.text =
        "(" + tone + ") In the " + scene.biome + " frontier, word of \"" + memory_choice +
        "\" spread after " + memory_event + ". It fits our " + scene.world_style_guide + " way.";
    branch.choices = {
        DialogChoice{"Tell me how this changes things.", "", DialogEffect{"", 0, 1.0F}},
    };
    npc.dialog.nodes.push_back(branch);

    const std::string attach_node_id = npc.dialog.start_node_id.empty() ? npc.dialog.nodes.front().id : npc.dialog.start_node_id;
    for (DialogNode& node : npc.dialog.nodes) {
        if (node.id != attach_node_id) {
            continue;
        }

        DialogChoice new_choice{};
        new_choice.text = tone == "guarded"
            ? "Can we rebuild trust after what happened?"
            : "Any new developments around camp?";
        new_choice.next_node_id = next_node_id;
        new_choice.required_faction_id = faction_id;
        new_choice.min_required_reputation = tone == "guarded" ? -10.0F : -100.0F;
        node.choices.push_back(new_choice);
        break;
    }

    npc.dialog.world_events.push_back(trigger_reason);
    TrimMemory(npc.dialog.world_events, 24U);
}

}  // namespace

namespace DialogEvolutionSystem {

void RecordPlayerChoice(Scene& scene, Entity& npc, const DialogNode& node, const DialogChoice& choice) {
    npc.dialog.past_choices.push_back(choice.text);
    TrimMemory(npc.dialog.past_choices, 24U);

    if (!npc.faction.faction_id.empty()) {
        npc.dialog.reputation_influence[npc.faction.faction_id] = FactionSystem::GetReputation(scene, npc.faction.faction_id);
    }

    scene.recent_actions.push_back("dialog_choice:npc=" + std::to_string(npc.id) + ":node=" + node.id + ":choice=" + choice.text);
    if (scene.recent_actions.size() > 48U) {
        const std::size_t trim_count = scene.recent_actions.size() - 48U;
        scene.recent_actions.erase(scene.recent_actions.begin(), scene.recent_actions.begin() + static_cast<std::ptrdiff_t>(trim_count));
    }
}

void RecordWorldEvent(Scene& scene, Entity& npc, const std::string& event_label) {
    if (event_label.empty()) {
        return;
    }
    npc.dialog.world_events.push_back(event_label);
    TrimMemory(npc.dialog.world_events, 24U);
    scene.recent_actions.push_back("world_event:npc=" + std::to_string(npc.id) + ":" + event_label);
}

bool EvolveNpcDialog(Scene& scene, std::uint64_t npc_id, const std::string& trigger_reason, bool consume_co_creator_queue) {
    Entity* npc = FindEntityById(scene, npc_id);
    if (npc == nullptr || !npc->dialog.IsValid()) {
        return false;
    }

    bool changed = false;
    if (consume_co_creator_queue) {
        changed = ApplyCoCreatorBranchMutation(scene, *npc);
    }
    if (!changed) {
        CreateProceduralBranch(scene, *npc, trigger_reason);
        changed = true;
    }

    if (changed) {
        GF_LOG_INFO("Dialog evolved for NPC #" + std::to_string(npc_id) + " (" + trigger_reason + ").");
    }
    return changed;
}

}  // namespace DialogEvolutionSystem
