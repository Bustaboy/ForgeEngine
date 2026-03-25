#pragma once

#include "Scene.h"
#include "SpriteBatch.h"

#include <vector>

class TilemapChunk {
public:
    [[nodiscard]] std::vector<SceneSprite2D> ExpandVisibleTiles(const Scene& scene) const;
};
