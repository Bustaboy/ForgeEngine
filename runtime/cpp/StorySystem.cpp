#include "StorySystem.h"

#include "EconomySystem.h"
#include "FactionSystem.h"
#include "Logger.h"
#include "NarratorSystem.h"
#include "CutsceneSystem.h"
#include "RelationshipSystem.h"
#include "Scene.h"

#include <algorithm>
#include <cstdint>
#include <cmath>
#include <exception>

namespace {
float Clamp100(float value) {
    return std::clamp(value, -100.0F, 100.0F);
}

bool ApplyRipple(Scene& scene, const StoryRipple& ripple) {
    if (ripple.type == "faction_reputation") {
        if (ripple.target_id.empty() || std::abs(ripple.value) < 0.0001F) {
            return false;
        }
        FactionSystem::AddPlayerReputation(scene, ripple.target_id, ripple.value, ripple.reason);
        return true;
    }
    if (ripple.type == "relationship") {
        if (ripple.target_id.empty() || ripple.dimension.empty()) {
            return false;
        }
        try {
            const std::uint64_t npc_id = std::stoull(ripple.target_id);
            return RelationshipSystem::SetDimension(scene, npc_id, ripple.dimension, ripple.value, false);
        } catch (const std::exception&) {
            return false;
        }
    }
    if (ripple.type == "economy_supply") {
        if (ripple.target_id.empty()) {
            return false;
        }
        EconomySystem::RegisterProduction(scene, ripple.target_id, static_cast<int>(std::round(std::max(0.0F, ripple.value))));
        return true;
    }
    if (ripple.type == "economy_demand") {
        if (ripple.target_id.empty()) {
            return false;
        }
        EconomySystem::RegisterConsumption(scene, ripple.target_id, static_cast<int>(std::round(std::max(0.0F, ripple.value))));
        return true;
    }
    if (ripple.type == "economy_route_raid") {
        if (ripple.target_id.empty()) {
            return false;
        }
        EconomySystem::RegisterRouteRaid(scene, ripple.target_id);
        return true;
    }
    return false;
}

void MarkBeatComplete(Scene& scene, const std::string& beat_id) {
    if (beat_id.empty()) {
        return;
    }
    for (StoryBeat& beat : scene.story.campaign_beats) {
        if (beat.id == beat_id) {
            beat.completed = true;
            return;
        }
    }
}

const StoryBeat* FindBeatById(const Scene& scene, const std::string& beat_id) {
    for (const StoryBeat& beat : scene.story.campaign_beats) {
        if (beat.id == beat_id) {
            return &beat;
        }
    }
    return nullptr;
}

void MaybeStartCutscene(Scene& scene, const StoryEvent& event) {
    const StoryBeat* beat = FindBeatById(scene, event.beat_id);
    if (beat == nullptr || !beat->cutscene_trigger) {
        return;
    }
    CutsceneSystem::TryStartForBeat(scene, *beat, event.event_id, event.narrator_line);
}
}  // namespace

namespace StorySystem {

void EnsureDefaults(Scene& scene) {
    if (!scene.story.campaign_beats.empty()) {
        return;
    }
    scene.story.campaign_beats.push_back(StoryBeat{
        "arrival",
        "Arrival at the Fringe",
        "The player reaches the settlement and meets local factions.",
        {"first_contract"},
        false,
        false,
    });
}

bool TriggerEventById(Scene& scene, const char* event_id) {
    if (event_id == nullptr || *event_id == '\0') {
        return false;
    }
    for (StoryEvent& event : scene.story.pending_events) {
        if (event.applied || event.event_id != event_id) {
            continue;
        }
        bool changed = false;
        for (const StoryRipple& ripple : event.ripples) {
            changed = ApplyRipple(scene, ripple) || changed;
        }
        event.applied = changed;
        if (changed) {
            MarkBeatComplete(scene, event.beat_id);
            scene.story.event_history.push_back(event.event_id);
            scene.recent_actions.push_back("story_event:" + event.event_id);
            if (!event.narrator_line.empty()) {
                NarratorSystem::QueueLine(scene, event.narrator_line, "story_event:" + event.event_id);
            }
            MaybeStartCutscene(scene, event);
            GF_LOG_INFO("Story event applied: " + event.event_id + " (" + event.title + ")");
        }
        return changed;
    }
    return false;
}

void Update(Scene& scene, float /*dt_seconds*/) {
    EnsureDefaults(scene);
    for (StoryEvent& event : scene.story.pending_events) {
        if (event.applied) {
            continue;
        }
        bool changed = false;
        for (const StoryRipple& ripple : event.ripples) {
            changed = ApplyRipple(scene, ripple) || changed;
        }
        if (!changed) {
            continue;
        }
        event.applied = true;
        MarkBeatComplete(scene, event.beat_id);
        scene.story.event_history.push_back(event.event_id);
        scene.recent_actions.push_back("story_event:" + event.event_id);
        if (!event.narrator_line.empty()) {
            NarratorSystem::QueueLine(scene, event.narrator_line, "story_event:" + event.event_id);
        }
        MaybeStartCutscene(scene, event);
    }
}

}  // namespace StorySystem
