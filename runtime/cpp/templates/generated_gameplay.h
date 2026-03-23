#pragma once

#include "../src/core/Scene.h"
#include "../src/utils/Logger.h"

inline void UpdateGameplay(Scene& scene, float dt) {
    static int frame_counter = 0;
    ++frame_counter;
    if ((frame_counter % 60) == 0) {
        GF_LOG_INFO(
            "generated gameplay hook update frame=" + std::to_string(frame_counter) +
            " dt=" + std::to_string(dt) +
            " entities=" + std::to_string(scene.entities.size()));
    }
}
