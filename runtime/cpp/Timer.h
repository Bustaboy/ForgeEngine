#pragma once

#include <string>

class Timer {
public:
    void BeginFrame();
    [[nodiscard]] bool ShouldUpdateFps() const;
    [[nodiscard]] int Fps() const;
    [[nodiscard]] const std::string& FrameTimeMsText() const;

private:
    double second_accumulator_ = 0.0;
    int frame_counter_ = 0;
    int fps_ = 0;
    bool fps_updated_ = false;
    std::string frame_time_ms_{};
};
