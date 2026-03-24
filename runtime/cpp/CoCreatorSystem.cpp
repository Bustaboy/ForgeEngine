#include "CoCreatorSystem.h"

#include <algorithm>

void CoCreatorSystem::QueueMutations(Scene& scene, const std::vector<CoCreatorQueuedMutation>& mutations) {
    for (const CoCreatorQueuedMutation& mutation : mutations) {
        if (mutation.mutation_json.empty()) {
            continue;
        }
        scene.co_creator_queue.push_back(mutation);
    }
}

bool CoCreatorSystem::PopNextMutation(Scene& scene, CoCreatorQueuedMutation& out_mutation) {
    if (scene.co_creator_queue.empty()) {
        return false;
    }

    out_mutation = scene.co_creator_queue.front();
    scene.co_creator_queue.pop_front();
    return true;
}

void CoCreatorSystem::TrimHistory(Scene& scene, std::size_t max_recent_actions) {
    if (scene.recent_actions.size() <= max_recent_actions) {
        return;
    }

    const std::size_t trim_count = scene.recent_actions.size() - max_recent_actions;
    scene.recent_actions.erase(scene.recent_actions.begin(), scene.recent_actions.begin() + static_cast<std::ptrdiff_t>(trim_count));
}
