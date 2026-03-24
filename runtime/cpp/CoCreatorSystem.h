#pragma once

#include "Scene.h"

#include <vector>

class CoCreatorSystem {
public:
    static void QueueMutations(Scene& scene, const std::vector<CoCreatorQueuedMutation>& mutations);
    static bool PopNextMutation(Scene& scene, CoCreatorQueuedMutation& out_mutation);
    static void TrimHistory(Scene& scene, std::size_t max_recent_actions = 24);
};
