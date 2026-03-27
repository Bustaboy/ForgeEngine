#include "LivingNpcSystem.h"

#include "NPCController.h"
#include "ScriptedBehaviorSystem.h"

void LivingNpcSystem::EnsureDefaults(Scene& scene) {
    NPCController::EnsureDefaults(scene);
    ScriptedBehaviorSystem::EnsureDefaults(scene);
}

void LivingNpcSystem::Update(Scene& scene, float dt_seconds) {
    NPCController::Update(scene, dt_seconds);
    ScriptedBehaviorSystem::Update(scene, dt_seconds);
}
