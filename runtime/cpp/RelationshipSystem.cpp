#include "RelationshipSystem.h"

#include "Entity.h"
#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <sstream>

namespace {
float ClampDimension(float value) {
    return std::clamp(value, RelationshipSystem::kRelationshipDimensionMin, RelationshipSystem::kRelationshipDimensionMax);
}

float ClampDayScale(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

float MemoryFadeFactor(const RelationshipMemory& memory, std::uint32_t current_day) {
    if (memory.decay_per_day <= 0.0F) {
        return 1.0F;
    }
    if (current_day <= memory.day_recorded) {
        return 1.0F;
    }

    const std::uint32_t days_elapsed = current_day - memory.day_recorded;
    const float fade = std::max(0.0F, 1.0F - memory.decay_per_day * static_cast<float>(days_elapsed));
    return fade;
}

float* DimensionPtr(RelationshipProfile& profile, const std::string& dimension) {
    if (dimension == "trust") {
        return &profile.trust;
    }
    if (dimension == "respect") {
        return &profile.respect;
    }
    if (dimension == "grudge") {
        return &profile.grudge;
    }
    if (dimension == "debt") {
        return &profile.debt;
    }
    if (dimension == "loyalty") {
        return &profile.loyalty;
    }
    return nullptr;
}

const float* DimensionPtr(const RelationshipProfile& profile, const std::string& dimension) {
    if (dimension == "trust") {
        return &profile.trust;
    }
    if (dimension == "respect") {
        return &profile.respect;
    }
    if (dimension == "grudge") {
        return &profile.grudge;
    }
    if (dimension == "debt") {
        return &profile.debt;
    }
    if (dimension == "loyalty") {
        return &profile.loyalty;
    }
    return nullptr;
}

RelationshipProfile BuildDefaultProfile() {
    RelationshipProfile profile{};
    profile.trust = 0.0F;
    profile.respect = 0.0F;
    profile.grudge = 0.0F;
    profile.debt = 0.0F;
    profile.loyalty = 0.0F;
    return profile;
}

void AddMemory(Scene& scene, std::uint64_t npc_id, const RelationshipMemory& memory) {
    RelationshipProfile& profile = scene.relationships[npc_id];
    profile.memories.push_back(memory);
    if (profile.memories.size() > 14U) {
        profile.memories.erase(profile.memories.begin(), profile.memories.begin() + static_cast<std::ptrdiff_t>(profile.memories.size() - 14U));
    }
}

void SyncLegacyMap(Scene& scene, std::uint64_t npc_id) {
    scene.npc_relationships[npc_id] = RelationshipSystem::CompositeScore(scene, npc_id);
}

}  // namespace

namespace RelationshipSystem {

void EnsureSceneRelationships(Scene& scene) {
    for (const Entity& entity : scene.entities) {
        if (!entity.dialog.IsValid()) {
            continue;
        }
        EnsureNpcRelationship(scene, entity);
    }

    for (const auto& [npc_id, legacy_value] : scene.npc_relationships) {
        auto [it, inserted] = scene.relationships.try_emplace(npc_id, BuildDefaultProfile());
        if (inserted) {
            it->second.trust = ClampDimension(legacy_value);
            it->second.respect = ClampDimension(legacy_value * 0.75F);
            it->second.loyalty = ClampDimension(legacy_value * 0.65F);
            it->second.grudge = ClampDimension(std::max(0.0F, -legacy_value));
        }
    }

    for (const auto& [npc_id, _] : scene.relationships) {
        (void)_;
        SyncLegacyMap(scene, npc_id);
    }
}

void EnsureNpcRelationship(Scene& scene, const Entity& npc) {
    EnsureNpcRelationship(scene, npc.id);
    auto npc_it = scene.relationships.find(npc.id);
    if (npc_it == scene.relationships.end()) {
        return;
    }

    RelationshipProfile& profile = npc_it->second;
    profile.trust = ClampDimension(profile.trust + npc.relationship.trust_bias);
    profile.respect = ClampDimension(profile.respect + npc.relationship.respect_bias);
    profile.loyalty = ClampDimension(profile.loyalty + npc.relationship.loyalty_bias);
    profile.grudge = ClampDimension(profile.grudge * std::max(0.2F, npc.relationship.grudge_sensitivity));
    profile.debt = ClampDimension(profile.debt * std::max(0.2F, npc.relationship.debt_sensitivity));
    SyncLegacyMap(scene, npc.id);
}

void EnsureNpcRelationship(Scene& scene, std::uint64_t npc_id) {
    if (npc_id == 0) {
        return;
    }

    scene.relationships.try_emplace(npc_id, BuildDefaultProfile());
    SyncLegacyMap(scene, npc_id);
}

float GetDimension(const Scene& scene, std::uint64_t npc_id, const std::string& dimension) {
    const auto it = scene.relationships.find(npc_id);
    if (it == scene.relationships.end()) {
        return 0.0F;
    }

    const float* value = DimensionPtr(it->second, dimension);
    if (value == nullptr) {
        return 0.0F;
    }
    return ClampDimension(*value);
}

float CompositeScore(const Scene& scene, std::uint64_t npc_id) {
    const auto it = scene.relationships.find(npc_id);
    if (it == scene.relationships.end()) {
        return 0.0F;
    }

    const RelationshipProfile& profile = it->second;
    const float bonded = (profile.trust * 0.36F) + (profile.respect * 0.26F) + (profile.loyalty * 0.30F);
    const float friction = (profile.grudge * 0.32F) + (std::max(0.0F, profile.debt) * 0.12F);
    return ClampDimension(bonded - friction);
}

std::string Summary(const Scene& scene, std::uint64_t npc_id) {
    const auto it = scene.relationships.find(npc_id);
    if (it == scene.relationships.end()) {
        return "NPC #" + std::to_string(npc_id) + " relationship not found.";
    }

    const RelationshipProfile& profile = it->second;
    std::ostringstream out;
    out << "NPC #" << npc_id
        << " trust=" << static_cast<int>(std::round(profile.trust))
        << " respect=" << static_cast<int>(std::round(profile.respect))
        << " grudge=" << static_cast<int>(std::round(profile.grudge))
        << " debt=" << static_cast<int>(std::round(profile.debt))
        << " loyalty=" << static_cast<int>(std::round(profile.loyalty))
        << " affinity=" << static_cast<int>(std::round(CompositeScore(scene, npc_id)));
    return out.str();
}

void ApplyDialogEffect(Scene& scene, std::uint64_t npc_id, const DialogEffect& effect, const std::string& context) {
    if (npc_id == 0) {
        return;
    }

    EnsureNpcRelationship(scene, npc_id);
    RelationshipProfile& profile = scene.relationships[npc_id];

    if (std::abs(effect.relationship_delta) > 0.0001F) {
        const float delta = effect.relationship_delta;
        if (delta >= 0.0F) {
            profile.trust = ClampDimension(profile.trust + delta);
            profile.respect = ClampDimension(profile.respect + delta * 0.65F);
            profile.grudge = ClampDimension(profile.grudge - delta * 0.45F);
            profile.loyalty = ClampDimension(profile.loyalty + delta * 0.50F);
        } else {
            const float magnitude = std::abs(delta);
            profile.trust = ClampDimension(profile.trust + delta);
            profile.respect = ClampDimension(profile.respect + delta * 0.30F);
            profile.grudge = ClampDimension(profile.grudge + magnitude * 0.95F);
            profile.loyalty = ClampDimension(profile.loyalty + delta * 0.45F);
        }

        AddMemory(scene, npc_id, RelationshipMemory{
            context,
            "dialog outcome",
            delta,
            delta * 0.55F,
            delta < 0.0F ? std::abs(delta) : -delta * 0.35F,
            0.0F,
            delta * 0.45F,
            scene.day_count,
            0.06F,
        });
    }

    if (!effect.inventory_item.empty() && effect.inventory_delta != 0) {
        const float item_weight = std::clamp(static_cast<float>(std::abs(effect.inventory_delta)) * 0.15F, 0.20F, 4.0F);
        if (effect.inventory_delta > 0) {
            profile.debt = ClampDimension(profile.debt + item_weight);
            profile.loyalty = ClampDimension(profile.loyalty + item_weight * 0.30F);
        } else {
            profile.debt = ClampDimension(profile.debt - item_weight * 1.2F);
            profile.respect = ClampDimension(profile.respect + item_weight * 0.35F);
        }
        AddMemory(scene, npc_id, RelationshipMemory{
            context + ":inventory",
            effect.inventory_item,
            0.0F,
            effect.inventory_delta > 0 ? 0.4F : 0.25F,
            0.0F,
            effect.inventory_delta > 0 ? item_weight : -item_weight,
            0.1F,
            scene.day_count,
            0.03F,
        });
    }

    profile.last_interaction_day = scene.day_count;
    SyncLegacyMap(scene, npc_id);
}

bool ChoicePassesRelationshipGate(
    const Scene& scene,
    std::uint64_t npc_id,
    const DialogChoice& choice,
    std::string& out_reason) {
    if (choice.required_relationship_dimension.empty()) {
        return true;
    }

    const float current = GetDimension(scene, npc_id, choice.required_relationship_dimension);
    if (current >= choice.min_required_relationship) {
        return true;
    }

    out_reason =
        "Choice locked. Requires " + choice.required_relationship_dimension + " >= " +
        std::to_string(static_cast<int>(std::round(choice.min_required_relationship))) +
        " (current " + std::to_string(static_cast<int>(std::round(current))) + ").";
    return false;
}

int ApplyTradeAdjustmentForEntity(const Scene& scene, const Entity& npc, int base_amount) {
    if (base_amount <= 0) {
        return base_amount;
    }

    const float trust = GetDimension(scene, npc.id, "trust");
    const float respect = GetDimension(scene, npc.id, "respect");
    const float grudge = GetDimension(scene, npc.id, "grudge");
    const float debt = GetDimension(scene, npc.id, "debt");

    float multiplier = 1.0F;
    multiplier += std::clamp((trust + respect) / 520.0F, -0.18F, 0.22F);
    multiplier -= std::clamp(grudge / 380.0F, 0.0F, 0.28F);
    multiplier += std::clamp(debt / 700.0F, -0.08F, 0.12F);

    return std::max(1, static_cast<int>(std::round(static_cast<float>(base_amount) * multiplier)));
}

void OnFactionReputationChanged(Scene& scene, const std::string& faction_id, float delta) {
    if (faction_id.empty() || std::abs(delta) < 0.0001F) {
        return;
    }

    for (const Entity& entity : scene.entities) {
        if (entity.faction.faction_id != faction_id) {
            continue;
        }

        EnsureNpcRelationship(scene, entity.id);
        RelationshipProfile& profile = scene.relationships[entity.id];
        profile.loyalty = ClampDimension(profile.loyalty + delta * 0.22F);
        profile.respect = ClampDimension(profile.respect + delta * 0.15F);
        if (delta < 0.0F) {
            profile.grudge = ClampDimension(profile.grudge + std::abs(delta) * 0.25F);
        }
        SyncLegacyMap(scene, entity.id);
    }
}

bool SetDimension(Scene& scene, std::uint64_t npc_id, const std::string& dimension, float value, bool absolute) {
    EnsureNpcRelationship(scene, npc_id);
    auto it = scene.relationships.find(npc_id);
    if (it == scene.relationships.end()) {
        return false;
    }

    float* target = DimensionPtr(it->second, dimension);
    if (target == nullptr) {
        return false;
    }

    const float next = absolute ? value : (*target + value);
    *target = ClampDimension(next);
    SyncLegacyMap(scene, npc_id);
    return true;
}

void Update(Scene& scene, float dt_seconds) {
    EnsureSceneRelationships(scene);
    const float safe_dt = std::max(0.0F, dt_seconds);
    const float decay_scale = ClampDayScale(safe_dt * 0.02F);

    for (auto& [npc_id, profile] : scene.relationships) {
        float memory_trust = 0.0F;
        float memory_respect = 0.0F;
        float memory_grudge = 0.0F;
        float memory_debt = 0.0F;
        float memory_loyalty = 0.0F;

        for (const RelationshipMemory& memory : profile.memories) {
            const float fade = MemoryFadeFactor(memory, scene.day_count);
            memory_trust += memory.trust_delta * fade;
            memory_respect += memory.respect_delta * fade;
            memory_grudge += memory.grudge_delta * fade;
            memory_debt += memory.debt_delta * fade;
            memory_loyalty += memory.loyalty_delta * fade;
        }

        profile.trust = ClampDimension(profile.trust + (memory_trust * decay_scale * 0.10F));
        profile.respect = ClampDimension(profile.respect + (memory_respect * decay_scale * 0.08F));
        profile.grudge = ClampDimension(std::max(0.0F, profile.grudge + (memory_grudge * decay_scale * 0.12F) - decay_scale * 0.10F));
        profile.debt = ClampDimension(profile.debt + (memory_debt * decay_scale * 0.09F) - decay_scale * 0.05F);
        profile.loyalty = ClampDimension(profile.loyalty + (memory_loyalty * decay_scale * 0.08F));

        const float natural_trust_settle = 0.5F * decay_scale;
        if (profile.trust > 0.0F) {
            profile.trust = std::max(0.0F, profile.trust - natural_trust_settle);
        }

        SyncLegacyMap(scene, npc_id);
    }
}

}  // namespace RelationshipSystem
