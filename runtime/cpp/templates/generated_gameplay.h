#pragma once

#include "Scene.h"

#include "Logger.h"

inline void UpdateGameplay(Scene& scene, float dt) {
    (void)scene;
    (void)dt;
    static int frame_counter = 0;
    ++frame_counter;
    if (frame_counter >= 60) {
        frame_counter = 0;
        GF_LOG_INFO("UpdateGameplay tick");
    }
}
