#pragma once

#include "Scene.h"

class LivingNpcSystem {
public:
    static void EnsureDefaults(Scene& scene);
    static void Update(Scene& scene, float dt_seconds);
};
