#include "FactionSystem.h"

#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cctype>
#include <cmath>

namespace {
float ClampReputation(float value) {
    return std::clamp(value, FactionSystem::kReputationMin, FactionSystem::kReputationMax);
}

std::string ToLower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return value;
}

}  // namespace

namespace FactionSystem {

void EnsureSceneFactions(Scene& scene) {
    if (scene.factions.empty()) {
        scene.factions["guild_builders"] = FactionDefinition{
            "guild_builders",
            "Guild Builders",
            "profession",
            "temperate",
            "grounded stylized frontier",
            -15.0F,
            10.0F,
            60.0F,
            {{"river_clans", 25.0F}, {"border_watch", -20.0F}},
        };
        scene.factions["river_clans"] = FactionDefinition{
            "river_clans",
            "River Clans",
            "culture",
            "temperate",
            "cozy handcrafted",
            -25.0F,
            0.0F,
            55.0F,
            {{"guild_builders", 25.0F}, {"border_watch", -35.0F}},
        };
        scene.factions["border_watch"] = FactionDefinition{
            "border_watch",
            "Border Watch",
            "country",
            "desert",
            "disciplined frontier",
            15.0F,
            20.0F,
            70.0F,
            {{"guild_builders", -20.0F}, {"river_clans", -35.0F}},
        };
    }

    for (const auto& [faction_id, faction] : scene.factions) {
        (void)faction;
        scene.player_reputation.try_emplace(faction_id, 0.0F);
    }

    for (Entity& entity : scene.entities) {
        if (entity.faction.faction_id.empty() && entity.dialog.IsValid()) {
            entity.faction.faction_id = "guild_builders";
            entity.faction.role = "local_npc";
        }
    }
}

float GetReputation(const Scene& scene, const std::string& faction_id) {
    const auto it = scene.player_reputation.find(faction_id);
    if (it == scene.player_reputation.end()) {
        return 0.0F;
    }
    return ClampReputation(it->second);
}

float AddPlayerReputation(Scene& scene, const std::string& faction_id, float delta, const std::string& reason) {
    if (faction_id.empty() || std::abs(delta) < 0.0001F) {
        return 0.0F;
    }

    float& value = scene.player_reputation[faction_id];
    value = ClampReputation(value + delta);

    const auto faction_it = scene.factions.find(faction_id);
    const std::string faction_name = faction_it != scene.factions.end() ? faction_it->second.display_name : faction_id;
    GF_LOG_INFO(
        "Faction reputation with " + faction_name + " changed to " + std::to_string(value) +
        (reason.empty() ? std::string{} : (" (" + reason + ")")));

    if (faction_it != scene.factions.end()) {
        for (const auto& [other_faction_id, relationship] : faction_it->second.relationships) {
            if (std::abs(relationship) < 0.0001F) {
                continue;
            }
            const float linked_delta = std::clamp((relationship / 100.0F) * delta, -2.0F, 2.0F);
            if (std::abs(linked_delta) < 0.0001F) {
                continue;
            }
            float& linked_value = scene.player_reputation[other_faction_id];
            linked_value = ClampReputation(linked_value + linked_delta);
        }
    }

    return value;
}

bool CanBuildInCurrentBiome(const Scene& scene, std::string& out_reason) {
    const std::string biome = ToLower(scene.biome);
    for (const auto& [faction_id, faction] : scene.factions) {
        const std::string hint = ToLower(faction.biome_hint);
        if (!hint.empty() && biome.find(hint) == std::string::npos) {
            continue;
        }

        const float rep = GetReputation(scene, faction_id);
        if (rep < faction.min_reputation_to_build) {
            out_reason = "Need reputation " + std::to_string(static_cast<int>(std::round(faction.min_reputation_to_build))) +
                " with " + faction.display_name + " to build here (current " +
                std::to_string(static_cast<int>(std::round(rep))) + ").";
            return false;
        }
        break;
    }

    out_reason.clear();
    return true;
}

std::string DialogToneForEntity(const Scene& scene, const Entity& npc) {
    if (npc.faction.faction_id.empty()) {
        return "neutral";
    }

    const float rep = GetReputation(scene, npc.faction.faction_id);
    if (rep <= -30.0F) {
        return "cold";
    }
    if (rep >= 45.0F) {
        return "warm";
    }
    return "neutral";
}

int ApplyTradeAdjustmentForEntity(const Scene& scene, const Entity& npc, int base_amount) {
    if (base_amount <= 0 || npc.faction.faction_id.empty()) {
        return base_amount;
    }

    const auto faction_it = scene.factions.find(npc.faction.faction_id);
    const float rep = GetReputation(scene, npc.faction.faction_id);
    const float threshold = faction_it == scene.factions.end() ? 50.0F : faction_it->second.trade_bonus_threshold;

    if (rep >= threshold) {
        return base_amount + std::max(1, static_cast<int>(std::round(base_amount * 0.15F)));
    }
    if (rep <= -30.0F) {
        return std::max(0, base_amount - std::max(1, static_cast<int>(std::round(base_amount * 0.25F))));
    }
    return base_amount;
}

}  // namespace FactionSystem
