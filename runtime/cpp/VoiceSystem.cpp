#include "VoiceSystem.h"

#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstdlib>
#include <deque>
#include <future>
#include <string>
#include <vector>

namespace {
struct QueuedVoiceLine {
    bool narrator = false;
    std::uint64_t npc_id = 0;
    std::string line{};
    std::string source_tag{};
};

std::deque<QueuedVoiceLine> g_voice_queue{};
std::future<int> g_active_tts{};
bool g_has_active_tts = false;

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

float ClampValue(float value, float min_value, float max_value) {
    return std::clamp(value, min_value, max_value);
}

std::string ToLower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

std::string EscapeSingleQuotes(std::string text) {
    std::string out;
    out.reserve(text.size() + 8);
    for (char ch : text) {
        if (ch == '\'') {
            out += "''";
            continue;
        }
        out.push_back(ch);
    }
    return out;
}

std::string EscapeShellDoubleQuotes(std::string text) {
    std::string out;
    out.reserve(text.size() + 8);
    for (char ch : text) {
        if (ch == '"' || ch == '\\' || ch == '$' || ch == '`') {
            out.push_back('\\');
        }
        out.push_back(ch);
    }
    return out;
}

std::string PickVoiceFromProfile(const VoiceProfileComponent& profile) {
    if (!profile.base_voice_id.empty() && profile.base_voice_id != "auto") {
        return profile.base_voice_id;
    }

    const std::string gender = ToLower(profile.gender);
#if defined(_WIN32)
    if (gender == "female" || gender == "woman") {
        return "Microsoft Zira Desktop";
    }
    if (gender == "male" || gender == "man") {
        return "Microsoft David Desktop";
    }
    return "default";
#elif defined(__APPLE__)
    if (gender == "female" || gender == "woman") {
        return "Samantha";
    }
    if (gender == "male" || gender == "man") {
        return "Alex";
    }
    return "default";
#else
    if (gender == "female" || gender == "woman") {
        return "en+f3";
    }
    if (gender == "male" || gender == "man") {
        return "en-us";
    }
    return "en";
#endif
}

VoiceSystem::VoiceRuntimeSettings BuildVoiceSettings(const VoiceProfileComponent& profile, const std::string& line) {
    VoiceSystem::VoiceRuntimeSettings settings{};
    settings.voice_id = PickVoiceFromProfile(profile);
    settings.pitch = ClampValue(profile.pitch, -50.0F, 50.0F);
    settings.rate = ClampValue(profile.rate, -40.0F, 40.0F);
    settings.volume = ClampValue(profile.volume, 0.2F, 1.6F);

    const std::string normalized_style = ToLower(profile.style);
    if (normalized_style.find("calm") != std::string::npos) {
        settings.rate = std::min(settings.rate, -8.0F);
    } else if (normalized_style.find("urgent") != std::string::npos) {
        settings.rate = std::max(settings.rate, 10.0F);
    }

    const std::string lowered_line = ToLower(line);
    if (lowered_line.find('!') != std::string::npos) {
        settings.rate = ClampValue(settings.rate + 5.0F, -40.0F, 40.0F);
        settings.pitch = ClampValue(settings.pitch + 4.0F, -50.0F, 50.0F);
    }
    return settings;
}

int SpeakBlocking(const VoiceSystem::VoiceRuntimeSettings& settings, const std::string& line) {
#if defined(_WIN32)
    const std::string escaped_line = EscapeSingleQuotes(line);
    const std::string escaped_voice = EscapeSingleQuotes(settings.voice_id == "default" ? "" : settings.voice_id);
    const std::string command =
        "powershell -NoProfile -Command \"$voice='" + escaped_voice +
        "';$line='" + escaped_line +
        "';$speaker=New-Object -ComObject SAPI.SpVoice;"
        "if($voice.Length -gt 0){foreach($v in $speaker.GetVoices()){if($v.GetDescription() -eq $voice){$speaker.Voice=$v;break}}};"
        "$speaker.Rate=[Math]::Max(-10,[Math]::Min(10,[int]([Math]::Round(" + std::to_string(settings.rate / 4.0F) + "))));"
        "$speaker.Volume=[Math]::Max(0,[Math]::Min(100,[int]([Math]::Round(" + std::to_string(settings.volume * 100.0F) + "))));"
        "$speaker.Speak($line) | Out-Null\"";
    return std::system(command.c_str());
#elif defined(__APPLE__)
    const std::string escaped_line = EscapeShellDoubleQuotes(line);
    const std::string escaped_voice = EscapeShellDoubleQuotes(settings.voice_id);
    const int words_per_minute = static_cast<int>(std::round(180.0F + settings.rate * 2.0F));
    if (settings.voice_id.empty() || settings.voice_id == "default") {
        return std::system(("say -r " + std::to_string(std::max(90, words_per_minute)) + " \"" + escaped_line + "\"").c_str());
    }
    return std::system(("say -v \"" + escaped_voice + "\" -r " + std::to_string(std::max(90, words_per_minute)) + " \"" + escaped_line + "\"").c_str());
#else
    const std::string escaped_line = EscapeShellDoubleQuotes(line);
    if (std::system("command -v espeak >/dev/null 2>&1") == 0) {
        const std::string escaped_voice = EscapeShellDoubleQuotes(settings.voice_id.empty() ? "en" : settings.voice_id);
        const int pitch = static_cast<int>(std::round(50.0F + settings.pitch));
        const int speed = static_cast<int>(std::round(160.0F + settings.rate * 2.5F));
        return std::system((
            "espeak -v \"" + escaped_voice + "\" -p " + std::to_string(std::clamp(pitch, 0, 99)) +
            " -s " + std::to_string(std::clamp(speed, 80, 320)) +
            " \"" + escaped_line + "\"").c_str());
    }
    if (std::system("command -v spd-say >/dev/null 2>&1") == 0) {
        return std::system(("spd-say \"" + escaped_line + "\"").c_str());
    }
    GF_LOG_INFO("Voice TTS unavailable (install espeak or spd-say on Linux).");
    return -1;
#endif
}

Entity* FindEntityById(Scene& scene, std::uint64_t id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == id) {
            return &entity;
        }
    }
    return nullptr;
}

}  // namespace

namespace VoiceSystem {

void EnsureDefaults(Scene& scene) {
    if (scene.narrator.voice_id.empty()) {
        scene.narrator.voice_id = "default";
    }
    if (scene.narrator.voice_profile.base_voice_id.empty()) {
        scene.narrator.voice_profile.base_voice_id = scene.narrator.voice_id;
    }
    for (Entity& entity : scene.entities) {
        if (entity.voice_profile.base_voice_id.empty()) {
            entity.voice_profile.base_voice_id = "auto";
        }
        entity.voice_profile.volume = ClampValue(entity.voice_profile.volume, 0.2F, 1.6F);
    }
}

VoiceRuntimeSettings ResolveNarratorVoice(const Scene& scene, const std::string& line) {
    VoiceProfileComponent profile = scene.narrator.voice_profile;
    if (!scene.narrator.voice_id.empty()) {
        profile.base_voice_id = scene.narrator.voice_id;
    }
    return BuildVoiceSettings(profile, line);
}

VoiceRuntimeSettings ResolveNpcVoice(const Scene& /*scene*/, const Entity& npc, const std::string& line) {
    return BuildVoiceSettings(npc.voice_profile, line);
}

bool QueueNarratorLine(Scene& scene, const std::string& line, const std::string& source_tag) {
    const std::string normalized = Trim(line);
    if (normalized.empty()) {
        return false;
    }
    g_voice_queue.push_back(QueuedVoiceLine{true, 0, normalized, source_tag});
    scene.recent_actions.push_back("voice_narrator:" + source_tag);
    return true;
}

bool QueueNpcLine(Scene& scene, std::uint64_t npc_id, const std::string& line, const std::string& source_tag) {
    const std::string normalized = Trim(line);
    if (normalized.empty() || npc_id == 0) {
        return false;
    }
    g_voice_queue.push_back(QueuedVoiceLine{false, npc_id, normalized, source_tag});
    scene.recent_actions.push_back("voice_npc:" + std::to_string(npc_id) + ":" + source_tag);
    return true;
}

void Update(Scene& scene, float /*dt_seconds*/) {
    EnsureDefaults(scene);

    if (g_has_active_tts) {
        if (g_active_tts.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
            const int result = g_active_tts.get();
            if (result != 0) {
                GF_LOG_INFO("Voice TTS command returned code: " + std::to_string(result));
            }
            g_has_active_tts = false;
        } else {
            return;
        }
    }

    if (g_voice_queue.empty()) {
        return;
    }

    const QueuedVoiceLine next = g_voice_queue.front();
    g_voice_queue.pop_front();

    VoiceRuntimeSettings settings{};
    std::string speaker_label = "Narrator";
    if (next.narrator) {
        settings = ResolveNarratorVoice(scene, next.line);
    } else {
        Entity* npc = FindEntityById(scene, next.npc_id);
        if (npc == nullptr) {
            return;
        }
        settings = ResolveNpcVoice(scene, *npc, next.line);
        speaker_label = "NPC #" + std::to_string(next.npc_id);
    }

    GF_LOG_INFO(speaker_label + " voice(" + settings.voice_id + "): " + next.line);
    g_active_tts = std::async(std::launch::async, [settings, line = next.line]() {
        return SpeakBlocking(settings, line);
    });
    g_has_active_tts = true;
}

}  // namespace VoiceSystem
