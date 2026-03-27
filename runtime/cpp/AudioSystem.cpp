#include "AudioSystem.h"

#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <sstream>

namespace {

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

bool IsPerformanceLikeMode(const Scene& scene) {
    const std::string& mode = scene.optimization_overrides.lightweight_mode;
    return mode == "performance" || mode == "aggressive" || mode == "ultra";
}

std::string WeatherAmbientTrack(const Scene& scene) {
    const std::string weather = scene.weather.current_weather;
    if (weather == "rain" || weather == "storm") {
        return "ambient_rain_loop";
    }
    if (weather == "snow") {
        return "ambient_snow_wind_loop";
    }
    if (weather == "windy" || weather == "sandstorm") {
        return "ambient_wind_loop";
    }
    return "ambient_exploration_loop";
}

float RelationshipMoodScore(const Scene& scene) {
    if (scene.relationships.empty()) {
        return 0.0F;
    }

    float aggregate = 0.0F;
    for (const auto& [npc_id, profile] : scene.relationships) {
        (void)npc_id;
        aggregate += profile.trust + profile.respect - profile.grudge;
    }

    return aggregate / static_cast<float>(scene.relationships.size());
}

std::string MoodTag(const Scene& scene) {
    const float morale = scene.settlement.morale;
    const float relationship_score = RelationshipMoodScore(scene);
    if (morale < 35.0F || relationship_score < -10.0F) {
        return "tense";
    }
    if (morale > 72.0F && relationship_score > 5.0F) {
        return "hopeful";
    }
    return "neutral";
}

std::string ExplorationTrack(const Scene& scene, const std::string& mood) {
    if (scene.audio.exploration_music_track.empty()) {
        return "music_exploration";
    }

    if (mood == "tense") {
        return scene.audio.exploration_music_track + "_tense";
    }
    if (mood == "hopeful") {
        return scene.audio.exploration_music_track + "_hopeful";
    }
    return scene.audio.exploration_music_track;
}

void LogTransition(Scene& scene, const std::string& bus, const std::string& track) {
    scene.audio.last_transition_reason = bus + ":" + track;
    scene.audio.transition_counter += 1U;
    scene.recent_actions.push_back("audio_transition:" + scene.audio.last_transition_reason);
    GF_LOG_INFO("Audio transition " + scene.audio.last_transition_reason);
}

}  // namespace

namespace AudioSystem {

void EnsureDefaults(Scene& scene) {
    scene.audio.master_volume = Clamp01(scene.audio.master_volume);
    scene.audio.music_volume = Clamp01(scene.audio.music_volume);
    scene.audio.ambient_volume = Clamp01(scene.audio.ambient_volume);
    scene.audio.ui_volume = Clamp01(scene.audio.ui_volume);
    scene.audio.sfx_volume = Clamp01(scene.audio.sfx_volume);
    scene.audio.weather_influence = Clamp01(scene.audio.weather_influence);
    scene.audio.max_spatial_voices = std::clamp(scene.audio.max_spatial_voices, 4, 64);
    scene.audio.performance_spatial_voices = std::clamp(scene.audio.performance_spatial_voices, 2, scene.audio.max_spatial_voices);

    if (scene.audio.exploration_music_track.empty()) {
        scene.audio.exploration_music_track = "music_exploration";
    }
    if (scene.audio.combat_music_track.empty()) {
        scene.audio.combat_music_track = "music_combat_intense";
    }
    if (scene.audio.ambient_track.empty()) {
        scene.audio.ambient_track = "ambient_exploration_loop";
    }
}

void Update(Scene& scene, float dt_seconds) {
    (void)dt_seconds;
    EnsureDefaults(scene);

    scene.audio.mood_tag = MoodTag(scene);
    const bool combat_active = scene.combat.active || scene.realtime_combat.active;
    const bool perf_mode = IsPerformanceLikeMode(scene);
    scene.audio.runtime_spatial_voice_limit = perf_mode ? scene.audio.performance_spatial_voices : scene.audio.max_spatial_voices;
    scene.audio.runtime_music_suppressed = perf_mode && scene.audio.disable_music_in_performance_mode;

    const std::string next_ambient = WeatherAmbientTrack(scene);
    if (scene.audio.ambient_enabled && scene.audio.ambient_track != next_ambient) {
        scene.audio.ambient_track = next_ambient;
        LogTransition(scene, "ambient", scene.audio.ambient_track);
    }

    if (scene.audio.runtime_music_suppressed || !scene.audio.music_enabled) {
        if (!scene.audio.current_music_track.empty()) {
            scene.audio.current_music_track.clear();
            LogTransition(scene, "music", "silence");
        }
        scene.audio.combat_override_active = false;
        return;
    }

    std::string next_music = ExplorationTrack(scene, scene.audio.mood_tag);
    scene.audio.combat_override_active = false;
    if (scene.audio.combat_music_override && combat_active) {
        next_music = scene.audio.combat_music_track;
        scene.audio.combat_override_active = true;
    } else if (scene.audio.weather_influence > 0.3F &&
               (scene.weather.current_weather == "rain" || scene.weather.current_weather == "snow" || scene.weather.current_weather == "windy")) {
        next_music += "_weather";
    }

    if (scene.audio.current_music_track != next_music) {
        scene.audio.current_music_track = next_music;
        LogTransition(scene, "music", scene.audio.current_music_track);
    }
}

bool PlayTrack(Scene& scene, const std::string& bus, const std::string& track, std::string& out_message) {
    EnsureDefaults(scene);
    if (track.empty()) {
        out_message = "Usage: /audio_play music|ambient|ui <track>";
        return false;
    }

    if (bus == "music") {
        scene.audio.current_music_track = track;
        scene.audio.exploration_music_track = track;
        scene.audio.runtime_music_suppressed = false;
        LogTransition(scene, bus, track);
        out_message = "Music track set to '" + track + "'.";
        return true;
    }

    if (bus == "ambient") {
        scene.audio.ambient_track = track;
        LogTransition(scene, bus, track);
        out_message = "Ambient track set to '" + track + "'.";
        return true;
    }

    if (bus == "ui") {
        scene.audio.last_ui_sound = track;
        LogTransition(scene, bus, track);
        out_message = "UI sound queued: '" + track + "'.";
        return true;
    }

    out_message = "Usage: /audio_play music|ambient|ui <track>";
    return false;
}

void SetCombatMusicOverride(Scene& scene, bool enabled) {
    scene.audio.combat_music_override = enabled;
}

bool SetVolume(Scene& scene, const std::string& bus, float normalized, std::string& out_message) {
    EnsureDefaults(scene);
    const float volume = Clamp01(normalized);

    if (bus == "master") {
        scene.audio.master_volume = volume;
    } else if (bus == "music") {
        scene.audio.music_volume = volume;
    } else if (bus == "ambient") {
        scene.audio.ambient_volume = volume;
    } else if (bus == "ui") {
        scene.audio.ui_volume = volume;
    } else if (bus == "sfx") {
        scene.audio.sfx_volume = volume;
    } else {
        out_message = "Usage: /audio_set_volume <master|music|ambient|ui|sfx> <0..1>";
        return false;
    }

    std::ostringstream message;
    message << bus << " volume=" << std::round(volume * 100.0F);
    out_message = message.str();
    return true;
}

std::string Summary(const Scene& scene) {
    std::ostringstream summary;
    summary << "Audio music='" << (scene.audio.current_music_track.empty() ? "(silent)" : scene.audio.current_music_track)
            << "' ambient='" << scene.audio.ambient_track
            << "' mood=" << scene.audio.mood_tag
            << " combat_override=" << (scene.audio.combat_music_override ? "on" : "off")
            << " spatial_voices=" << scene.audio.runtime_spatial_voice_limit;
    return summary.str();
}

}  // namespace AudioSystem
