#include "Timer.h"

#include <chrono>
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <sstream>

void Timer::BeginFrame() {
    static auto previous = std::chrono::steady_clock::now();
    const auto now = std::chrono::steady_clock::now();
    const std::chrono::duration<double> delta = now - previous;
    previous = now;

    const double delta_seconds = delta.count();
    second_accumulator_ += delta_seconds;
    ++frame_counter_;

    std::ostringstream stream;
    stream << std::fixed << std::setprecision(2) << (delta_seconds * 1000.0);
    frame_time_ms_ = stream.str();

    fps_updated_ = false;
    if (second_accumulator_ >= 1.0) {
        fps_ = frame_counter_;
        frame_counter_ = 0;
        second_accumulator_ = 0.0;
        fps_updated_ = true;
    }
}

bool Timer::ShouldUpdateFps() const {
    return fps_updated_;
}

int Timer::Fps() const {
    return fps_;
}

const std::string& Timer::FrameTimeMsText() const {
    return frame_time_ms_;
}

std::string Timer::DayClockText(float day_progress, std::uint32_t day_count) const {
    constexpr int kMinutesPerDay = 24 * 60;
    const float wrapped_day_progress = day_progress - std::floor(day_progress);
    const int total_minutes = static_cast<int>(std::floor(wrapped_day_progress * static_cast<float>(kMinutesPerDay)));
    const int hours = (total_minutes / 60) % 24;
    const int minutes = total_minutes % 60;

    std::ostringstream stream;
    stream << "Day " << std::max(1U, day_count) << " - " << std::setw(2) << std::setfill('0') << hours
           << ":" << std::setw(2) << std::setfill('0') << minutes;
    return stream.str();
}
