#include "NarratorSystem.h"

#include "Logger.h"
#include "Scene.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstdlib>
#include <future>
#include <string>

namespace {
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

int SpeakBlocking(const std::string& voice_id, const std::string& line) {
#if defined(_WIN32)
    const std::string escaped_line = EscapeSingleQuotes(line);
    const std::string escaped_voice = EscapeSingleQuotes(voice_id == "default" ? "" : voice_id);
    const std::string command =
        "powershell -NoProfile -Command \"$voice='" + escaped_voice +
        "';$line='" + escaped_line +
        "';$speaker=New-Object -ComObject SAPI.SpVoice;"
        "if($voice.Length -gt 0){foreach($v in $speaker.GetVoices()){if($v.GetDescription() -eq $voice){$speaker.Voice=$v;break}}};"
        "$speaker.Speak($line) | Out-Null\"";
    return std::system(command.c_str());
#elif defined(__APPLE__)
    const std::string escaped_line = EscapeShellDoubleQuotes(line);
    if (voice_id.empty() || voice_id == "default") {
        return std::system(("say \"" + escaped_line + "\"").c_str());
    }
    const std::string escaped_voice = EscapeShellDoubleQuotes(voice_id);
    return std::system(("say -v \"" + escaped_voice + "\" \"" + escaped_line + "\"").c_str());
#else
    const std::string escaped_line = EscapeShellDoubleQuotes(line);
    if (std::system("command -v spd-say >/dev/null 2>&1") == 0) {
        return std::system(("spd-say \"" + escaped_line + "\"").c_str());
    }
    if (std::system("command -v espeak >/dev/null 2>&1") == 0) {
        if (voice_id.empty() || voice_id == "default") {
            return std::system(("espeak \"" + escaped_line + "\"").c_str());
        }
        const std::string escaped_voice = EscapeShellDoubleQuotes(voice_id);
        return std::system(("espeak -v \"" + escaped_voice + "\" \"" + escaped_line + "\"").c_str());
    }
    GF_LOG_INFO("Narrator TTS unavailable (install spd-say or espeak on Linux).");
    return -1;
#endif
}
}  // namespace

namespace NarratorSystem {

void EnsureDefaults(Scene& scene) {
    if (scene.narrator.voice_id.empty()) {
        scene.narrator.voice_id = "default";
    }
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

    if (g_has_active_tts) {
        if (g_active_tts.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
            const int result = g_active_tts.get();
            if (result != 0) {
                GF_LOG_INFO("Narrator TTS command returned code: " + std::to_string(result));
            }
            g_has_active_tts = false;
        } else {
            return;
        }
    }

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

    const std::string voice_id = scene.narrator.voice_id;
    g_active_tts = std::async(std::launch::async, [voice_id, line]() {
        return SpeakBlocking(voice_id, line);
    });
    g_has_active_tts = true;
}

}  // namespace NarratorSystem
