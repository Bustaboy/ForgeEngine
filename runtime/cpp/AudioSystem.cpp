#include "AudioSystem.h"

#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <sstream>
#include <glm/geometric.hpp>

namespace {

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

bool IsPerformanceLikeMode(const Scene& scene) {
    const std::string& mode = scene.optimization_overrides.lightweight_mode;
    return mode == "performance" || mode == "aggressive" || mode == "ultra";
}

struct SpatialMix {
    float gain = 1.0F;
    float pan = 0.0F;
    bool spatialized = false;
};

bool ShouldFallbackTo2D(const Scene& scene, bool performance_mode) {
    if (!scene.audio.spatial_audio_enabled) {
        return true;
    }
    return performance_mode && scene.audio.disable_distant_spatial_in_performance_mode;
}

float RuntimeSpatialDistance(const Scene& scene, bool performance_mode) {
    return performance_mode ? scene.audio.performance_spatial_max_distance : scene.audio.spatial_max_distance;
}

SpatialMix ComputeSpatialMix(const Scene& scene, const glm::vec3& source_position, bool performance_mode) {
    const float max_distance = std::max(4.0F, RuntimeSpatialDistance(scene, performance_mode));
    const glm::vec3 listener = scene.player_proxy_position;
    const glm::vec3 delta = source_position - listener;
    const float distance = glm::length(delta);

    SpatialMix mix{};
    mix.spatialized = !ShouldFallbackTo2D(scene, performance_mode) && distance <= max_distance;
    if (mix.spatialized) {
        mix.gain = std::clamp(1.0F - (distance / max_distance), 0.1F, 1.0F);
        mix.pan = std::clamp(delta.x / std::max(1.0F, max_distance), -1.0F, 1.0F);
    }
    return mix;
}

void RecordSfxEvent(Scene& scene, const std::string& effect, const SpatialMix& mix, bool looped) {
    const bool performance_mode = IsPerformanceLikeMode(scene);
    const int runtime_voice_limit = std::min(scene.audio.runtime_spatial_voice_limit, scene.audio.spatial_voice_hard_limit);
    if (mix.spatialized) {
        if (static_cast<int>(scene.audio.runtime_active_spatial_voices) >= runtime_voice_limit) {
            scene.audio.runtime_dropped_spatial_voices += 1U;
            return;
        }
        scene.audio.runtime_active_spatial_voices += 1U;
    }

    ++scene.audio.sfx_play_counter;
    std::ostringstream event;
    event << (looped ? "loop:" : "oneshot:") << effect
          << " mode=" << (mix.spatialized ? "3d" : "2d")
          << " gain=" << std::round((mix.gain * scene.audio.sfx_volume * scene.audio.master_volume) * 100.0F)
          << " pan=" << std::round(mix.pan * 100.0F)
          << " perf=" << (performance_mode ? "on" : "off");
    scene.audio.last_sfx_event = event.str();
    scene.recent_actions.push_back("audio_sfx:" + scene.audio.last_sfx_event);
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

float CombatSignal(const Scene& scene) {
    float signal = 0.0F;
    if (scene.combat.active) {
        signal += 0.6F;
    }
    if (scene.realtime_combat.active) {
        signal += 0.6F;
    }
    if (!scene.realtime_combat.last_action.empty() || !scene.combat.last_action.empty()) {
        signal += 0.2F;
    }
    return Clamp01(signal);
}

std::string ZoneFromScene(const Scene& scene) {
    if (!scene.audio.reverb_zone_type.empty()) {
        return scene.audio.reverb_zone_type;
    }
    if (scene.biome.find("cave") != std::string::npos) {
        return "cave";
    }
    if (scene.biome.find("indoor") != std::string::npos || scene.biome.find("city") != std::string::npos) {
        return "indoor";
    }
    return "outdoor";
}

float BaseReverbForZone(const std::string& zone) {
    if (zone == "indoor") {
        return 0.45F;
    }
    if (zone == "cave") {
        return 0.70F;
    }
    if (zone == "workshop") {
        return 0.32F;
    }
    return 0.12F;
}

std::string ReverbPresetForZone(const std::string& zone) {
    if (zone == "indoor") {
        return "room";
    }
    if (zone == "cave") {
        return "cavern";
    }
    if (zone == "workshop") {
        return "metallic_short";
    }
    return "open";
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
    scene.audio.combat_ducking_strength = Clamp01(scene.audio.combat_ducking_strength);
    scene.audio.ui_ducking_strength = Clamp01(scene.audio.ui_ducking_strength);
    scene.audio.procedural_intensity = Clamp01(scene.audio.procedural_intensity);
    scene.audio.reverb_wet_mix = Clamp01(scene.audio.reverb_wet_mix);
    scene.audio.max_spatial_voices = std::clamp(scene.audio.max_spatial_voices, 4, 64);
    scene.audio.performance_spatial_voices = std::clamp(scene.audio.performance_spatial_voices, 2, scene.audio.max_spatial_voices);
    scene.audio.spatial_voice_hard_limit = std::clamp(scene.audio.spatial_voice_hard_limit, 4, 96);
    scene.audio.spatial_max_distance = std::clamp(scene.audio.spatial_max_distance, 6.0F, 120.0F);
    scene.audio.performance_spatial_max_distance =
        std::clamp(scene.audio.performance_spatial_max_distance, 4.0F, scene.audio.spatial_max_distance);
    scene.audio.ambient_weather_timer_seconds = std::max(0.0F, scene.audio.ambient_weather_timer_seconds);
    scene.audio.ui_duck_timer_seconds = std::max(0.0F, scene.audio.ui_duck_timer_seconds);

    if (scene.audio.exploration_music_track.empty()) {
        scene.audio.exploration_music_track = "music_exploration";
    }
    if (scene.audio.combat_music_track.empty()) {
        scene.audio.combat_music_track = "music_combat_intense";
    }
    if (scene.audio.ambient_track.empty()) {
        scene.audio.ambient_track = "ambient_exploration_loop";
    }
    if (scene.audio.last_weather_for_audio.empty()) {
        scene.audio.last_weather_for_audio = scene.weather.current_weather.empty() ? "sunny" : scene.weather.current_weather;
    }
    if (scene.audio.reverb_zone_type.empty()) {
        scene.audio.reverb_zone_type = "outdoor";
    }
    if (scene.audio.runtime_reverb_preset.empty()) {
        scene.audio.runtime_reverb_preset = "open";
    }
}

void Update(Scene& scene, float dt_seconds) {
    const float safe_dt = std::clamp(dt_seconds, 0.0F, 0.25F);
    EnsureDefaults(scene);

    scene.audio.mood_tag = MoodTag(scene);
    const bool combat_active = scene.combat.active || scene.realtime_combat.active;
    const bool perf_mode = IsPerformanceLikeMode(scene);
    const bool reduced_audio_mode = perf_mode || scene.optimization_overrides.lightweight_mode == "balanced";
    scene.audio.runtime_spatial_voice_limit = perf_mode ? scene.audio.performance_spatial_voices : scene.audio.max_spatial_voices;
    scene.audio.runtime_active_spatial_voices = 0U;
    scene.audio.runtime_dropped_spatial_voices = 0U;
    scene.audio.runtime_music_suppressed = perf_mode && scene.audio.disable_music_in_performance_mode;
    scene.audio.runtime_music_duck_gain = 1.0F;
    scene.audio.runtime_ambient_duck_gain = 1.0F;
    scene.audio.runtime_reverb_send = 0.0F;
    scene.audio.runtime_duck_state = "none";
    scene.audio.runtime_procedural_wind_gain = 0.0F;
    scene.audio.runtime_procedural_rain_gain = 0.0F;
    scene.audio.runtime_procedural_rumble_gain = 0.0F;
    scene.audio.runtime_procedural_state = "idle";

    scene.audio.ambient_weather_timer_seconds = std::max(0.0F, scene.audio.ambient_weather_timer_seconds - safe_dt);
    scene.audio.ui_duck_timer_seconds = std::max(0.0F, scene.audio.ui_duck_timer_seconds - safe_dt);
    if (scene.weather.current_weather != scene.audio.last_weather_for_audio) {
        const std::string weather_effect = scene.weather.current_weather == "storm" ? "weather_thunder_roll" :
            scene.weather.current_weather == "rain" ? "weather_rain_spatter" :
            scene.weather.current_weather == "windy" ? "weather_wind_gust" :
            scene.weather.current_weather == "snow" ? "weather_snow_gust" : "weather_shift";
        RecordSfxEvent(scene, weather_effect, ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(5.0F, 0.0F, 0.0F), perf_mode), false);
        scene.audio.last_weather_for_audio = scene.weather.current_weather;
        scene.audio.ambient_weather_timer_seconds = 2.5F;
    } else if (scene.audio.ambient_weather_timer_seconds <= 0.0F &&
               (scene.weather.current_weather == "rain" || scene.weather.current_weather == "storm" || scene.weather.current_weather == "windy")) {
        const std::string loop_effect = scene.weather.current_weather == "rain" ? "weather_rain_loop" :
            scene.weather.current_weather == "storm" ? "weather_storm_loop" : "weather_wind_loop";
        RecordSfxEvent(scene, loop_effect, ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(-6.0F, 0.0F, 2.0F), perf_mode), true);
        scene.audio.ambient_weather_timer_seconds = 4.0F;
    }

    if (!scene.realtime_combat.last_action.empty() && scene.realtime_combat.last_action != scene.audio.last_realtime_action) {
        scene.audio.last_realtime_action = scene.realtime_combat.last_action;
        const std::string& action = scene.realtime_combat.last_action;
        const std::string effect = action.find("dodge") != std::string::npos ? "combat_dodge" :
            action.find("hit") != std::string::npos ? "combat_hit_reaction" :
            action.find("attack") != std::string::npos ? "combat_attack" : "combat_action";
        RecordSfxEvent(scene, effect, ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(1.5F, 0.0F, 0.0F), perf_mode), false);
    }

    if (!scene.combat.last_action.empty() && scene.combat.last_action != scene.audio.last_combat_action) {
        scene.audio.last_combat_action = scene.combat.last_action;
        const std::string effect = scene.combat.last_action.find("hit") != std::string::npos ? "combat_hit_reaction" :
            scene.combat.last_action.find("attack") != std::string::npos ? "combat_attack" :
            scene.combat.last_action == "move" ? "combat_footstep" : "combat_ui";
        RecordSfxEvent(scene, effect, ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(-1.25F, 0.0F, 0.0F), perf_mode), false);
    }

    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            continue;
        }
        float& cooldown = scene.audio.npc_footstep_cooldowns[entity.id];
        cooldown = std::max(0.0F, cooldown - safe_dt);
        const float planar_speed_sq = (entity.velocity.x * entity.velocity.x) + (entity.velocity.z * entity.velocity.z);
        if (planar_speed_sq > 0.5F && cooldown <= 0.0F) {
            RecordSfxEvent(scene, "npc_footstep", ComputeSpatialMix(scene, entity.transform.pos, perf_mode), false);
            cooldown = perf_mode ? 0.55F : 0.32F;
        } else if (entity.schedule.current_activity == "socialize" && cooldown <= 0.0F) {
            RecordSfxEvent(scene, "npc_interaction", ComputeSpatialMix(scene, entity.transform.pos, perf_mode), false);
            cooldown = 1.5F;
        }
    }

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

    const float combat_signal = scene.audio.runtime_duck_test ? 1.0F : CombatSignal(scene);
    const bool ui_recent = scene.audio.ui_duck_timer_seconds > 0.0F;
    const bool ducking_runtime_enabled = scene.audio.ducking_enabled && !reduced_audio_mode;
    if (ducking_runtime_enabled) {
        scene.audio.runtime_ambient_duck_gain = 1.0F - (combat_signal * scene.audio.combat_ducking_strength);
        scene.audio.runtime_music_duck_gain = 1.0F - ((ui_recent ? 1.0F : 0.0F) * scene.audio.ui_ducking_strength);
        scene.audio.runtime_duck_state = combat_signal > 0.1F ? "combat_duck" : (ui_recent ? "ui_duck" : "none");
    } else {
        scene.audio.runtime_duck_state = reduced_audio_mode ? "disabled_perf" : "disabled";
    }

    const bool reverb_runtime_enabled = scene.audio.reverb_enabled && !reduced_audio_mode;
    const std::string zone = ZoneFromScene(scene);
    scene.audio.reverb_zone_type = zone;
    if (reverb_runtime_enabled) {
        scene.audio.runtime_reverb_send = BaseReverbForZone(zone) * scene.audio.reverb_wet_mix;
        scene.audio.runtime_reverb_preset = ReverbPresetForZone(zone);
    } else {
        scene.audio.runtime_reverb_send = 0.0F;
        scene.audio.runtime_reverb_preset = "disabled";
    }

    const bool procedural_runtime_enabled = scene.audio.procedural_audio_enabled && !perf_mode;
    if (procedural_runtime_enabled) {
        const float intensity = scene.audio.procedural_intensity;
        const float gust_seed = std::sin(scene.world_time.elapsed_seconds * 0.47F) * 0.5F + 0.5F;
        scene.audio.runtime_procedural_wind_gain = intensity * gust_seed;
        const float rain_weight = (scene.weather.current_weather == "rain" || scene.weather.current_weather == "storm")
            ? scene.weather.intensity
            : 0.0F;
        scene.audio.runtime_procedural_rain_gain = intensity * Clamp01(rain_weight);
        scene.audio.runtime_procedural_rumble_gain = intensity * 0.5F * combat_signal;
        scene.audio.runtime_procedural_state = "wind/rain/rumble";
        if (reduced_audio_mode) {
            scene.audio.runtime_procedural_rumble_gain = 0.0F;
            scene.audio.runtime_procedural_state = "wind/rain";
        }
    } else {
        scene.audio.runtime_procedural_state = perf_mode ? "disabled_perf" : "disabled";
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
        scene.audio.ui_duck_timer_seconds = 0.35F;
        LogTransition(scene, bus, track);
        out_message = "UI sound queued: '" + track + "'.";
        return true;
    }

    out_message = "Usage: /audio_play music|ambient|ui <track>";
    return false;
}

bool PlaySfx(Scene& scene, const std::string& effect, std::string& out_message) {
    EnsureDefaults(scene);
    if (effect.empty()) {
        out_message = "Usage: /audio_play_sfx <effect>";
        return false;
    }
    const bool perf_mode = IsPerformanceLikeMode(scene);
    RecordSfxEvent(scene, effect, ComputeSpatialMix(scene, scene.player_proxy_position, perf_mode), false);
    out_message = "SFX queued: '" + effect + "' (" +
        (scene.audio.last_sfx_event.find("mode=3d") != std::string::npos ? "3D" : "2D") + ").";
    return true;
}

bool SpatialTest(Scene& scene, std::string& out_message) {
    EnsureDefaults(scene);
    const bool perf_mode = IsPerformanceLikeMode(scene);
    const std::uint64_t seed = ++scene.audio.last_spatial_test_seed;
    const float offset = static_cast<float>((seed % 6U) + 2U);
    RecordSfxEvent(scene, "spatial_test_left", ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(-offset, 0.0F, 0.0F), perf_mode), false);
    RecordSfxEvent(scene, "spatial_test_right", ComputeSpatialMix(scene, scene.player_proxy_position + glm::vec3(offset, 0.0F, 0.0F), perf_mode), false);
    out_message = "Spatial test emitted (seed=" + std::to_string(seed) + ", voices=" +
        std::to_string(scene.audio.runtime_active_spatial_voices) + "/" + std::to_string(scene.audio.runtime_spatial_voice_limit) + ").";
    return true;
}

bool TriggerDuckTest(Scene& scene, std::string& out_message) {
    EnsureDefaults(scene);
    scene.audio.runtime_duck_test = !scene.audio.runtime_duck_test;
    out_message = std::string("Audio duck test: ") + (scene.audio.runtime_duck_test ? "ON" : "OFF");
    return true;
}

bool SetReverbZone(Scene& scene, const std::string& zone_type, std::string& out_message) {
    EnsureDefaults(scene);
    if (zone_type != "outdoor" && zone_type != "indoor" && zone_type != "cave" && zone_type != "workshop") {
        out_message = "Usage: /audio_reverb_zone <outdoor|indoor|cave|workshop>";
        return false;
    }
    scene.audio.reverb_zone_type = zone_type;
    out_message = "Reverb zone set to '" + zone_type + "'.";
    return true;
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
            << " duck=" << scene.audio.runtime_duck_state
            << " music_gain=" << std::round(scene.audio.runtime_music_duck_gain * 100.0F)
            << " ambient_gain=" << std::round(scene.audio.runtime_ambient_duck_gain * 100.0F)
            << " reverb=" << scene.audio.runtime_reverb_preset
            << " reverb_send=" << std::round(scene.audio.runtime_reverb_send * 100.0F)
            << " procedural=" << scene.audio.runtime_procedural_state
            << " spatial_voices=" << scene.audio.runtime_active_spatial_voices << "/" << scene.audio.runtime_spatial_voice_limit
            << " dropped=" << scene.audio.runtime_dropped_spatial_voices
            << " last_sfx='" << (scene.audio.last_sfx_event.empty() ? "none" : scene.audio.last_sfx_event) << "'";
    return summary.str();
}

}  // namespace AudioSystem
