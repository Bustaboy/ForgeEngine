#pragma once

#include <string>
#include <cstdint>

class Timer {
public:
    void BeginFrame();
    void UpdateWorldTime(float dt_seconds);
    [[nodiscard]] bool ShouldUpdateFps() const;
    [[nodiscard]] int Fps() const;
    [[nodiscard]] const std::string& FrameTimeMsText() const;
    [[nodiscard]] float DayProgress() const;
    [[nodiscard]] float CycleSpeed() const;
    [[nodiscard]] std::uint32_t DayCount() const;
    [[nodiscard]] std::string DayClockText() const;
    void SetDayProgress(float day_progress);
    void SetCycleSpeed(float cycle_speed);
    void SetDayCount(std::uint32_t day_count);

private:
    double second_accumulator_ = 0.0;
    int frame_counter_ = 0;
    int fps_ = 0;
    bool fps_updated_ = false;
    std::string frame_time_ms_{};
    float day_progress_ = 0.25F;
    float cycle_speed_ = 0.01F;
    std::uint32_t day_count_ = 1;
};
