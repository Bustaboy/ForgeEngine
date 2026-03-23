#pragma once

#include "../src/core/Scene.h"
#include "../src/utils/Logger.h"

inline void UpdateGameplay(Scene& scene, float dt) {
    GF_LOG_INFO("generated gameplay hook update dt=" + std::to_string(dt) + " entities=" + std::to_string(scene.entities.size()));
}
