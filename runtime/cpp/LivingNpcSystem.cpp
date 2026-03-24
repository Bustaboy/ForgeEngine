#include "LivingNpcSystem.h"

#include "NPCController.h"

void LivingNpcSystem::EnsureDefaults(Scene& scene) {
    NPCController::EnsureDefaults(scene);
}

void LivingNpcSystem::Update(Scene& scene, float dt_seconds) {
    NPCController::Update(scene, dt_seconds);
}
