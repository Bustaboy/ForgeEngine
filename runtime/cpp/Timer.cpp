#include "Timer.h"

#include <chrono>
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
