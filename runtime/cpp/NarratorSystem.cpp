#include "NarratorSystem.h"

#include "Logger.h"
#include "Scene.h"
#include "VoiceSystem.h"

#include <algorithm>
#include <cctype>
#include <string>

namespace {
std::string Trim(std::string text) {
    const auto begin = std::find_if_not(text.begin(), text.end(), [](unsigned char ch) {
        return std::isspace(ch) != 0;
    });
    const auto end = std::find_if_not(text.rbegin(), text.rend(), [](unsigned char ch) {
        return std::isspace(ch) != 0;
    }).base();
    if (begin >= end) {
        return {};
    }
    return std::string(begin, end);
}

}  // namespace

namespace NarratorSystem {

void EnsureDefaults(Scene& scene) {
    VoiceSystem::EnsureDefaults(scene);
}

bool QueueLine(Scene& scene, const std::string& text, const std::string& source_tag) {
    const std::string normalized = Trim(text);
    if (normalized.empty()) {
        return false;
    }

    EnsureDefaults(scene);
    scene.narrator.pending_lines.push_back(normalized);
    scene.recent_actions.push_back("narrator_queue:" + source_tag);
    return true;
}

void Update(Scene& scene, float /*dt_seconds*/) {
    EnsureDefaults(scene);

    if (!scene.narrator.enabled || scene.narrator.pending_lines.empty()) {
        return;
    }

    const std::string line = scene.narrator.pending_lines.front();
    scene.narrator.pending_lines.pop_front();
    scene.narrator.spoken_history.push_back(line);
    if (scene.narrator.spoken_history.size() > 64) {
        scene.narrator.spoken_history.erase(scene.narrator.spoken_history.begin());
    }
    GF_LOG_INFO("Narrator: " + line);

    VoiceSystem::QueueNarratorLine(scene, line, "narrator_system");
}

}  // namespace NarratorSystem
