#include "TilemapChunk.h"

std::vector<SceneSprite2D> TilemapChunk::ExpandVisibleTiles(const Scene& scene) const {
    std::vector<SceneSprite2D> sprites;
    if (!scene.render_2d.enabled) {
        return sprites;
    }

    for (const SceneTilemap2D& tilemap : scene.render_2d.tilemaps) {
        if (tilemap.columns <= 0 || tilemap.rows <= 0 || tilemap.tiles.empty()) {
            continue;
        }

        for (int row = 0; row < tilemap.rows; ++row) {
            for (int col = 0; col < tilemap.columns; ++col) {
                const std::size_t idx = static_cast<std::size_t>(row * tilemap.columns + col);
                if (idx >= tilemap.tiles.size() || tilemap.tiles[idx] < 0) {
                    continue;
                }

                SceneSprite2D sprite{};
                sprite.asset_id = tilemap.tileset_asset_id;
                sprite.position = glm::vec2(
                    tilemap.origin.x + (static_cast<float>(col) + 0.5F) * tilemap.tile_size.x,
                    tilemap.origin.y + (static_cast<float>(row) + 0.5F) * tilemap.tile_size.y);
                sprite.size = tilemap.tile_size;
                sprite.layer = tilemap.layer;
                sprites.push_back(sprite);
            }
        }
    }

    return sprites;
}
