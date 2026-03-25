#include "SpriteBatch.h"

#include <glm/common.hpp>
#include <glm/gtc/matrix_transform.hpp>

#include <algorithm>
#include <cmath>
#include <filesystem>

namespace {
float SnapToPixel(float value, float pixels_per_unit) {
    if (pixels_per_unit <= 0.0F) {
        return value;
    }
    return std::round(value * pixels_per_unit) / pixels_per_unit;
}
}  // namespace

void SpriteBatch::LoadApprovedAssets(const std::string& approved_assets_root) {
    namespace fs = std::filesystem;

    bindless_index_by_asset_id_.clear();
    bindless_asset_ids_.clear();

    const fs::path root(approved_assets_root);
    if (!fs::exists(root) || !fs::is_directory(root)) {
        return;
    }

    for (const fs::directory_entry& entry : fs::recursive_directory_iterator(root)) {
        if (!entry.is_regular_file()) {
            continue;
        }

        const fs::path extension = entry.path().extension();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") {
            continue;
        }

        RegisterBindlessAsset(entry.path().stem().string());
    }
}

SpriteBatch::BuildResult SpriteBatch::Build(const Scene& scene) const {
    BuildResult result{};

    if (!scene.render_2d.enabled) {
        return result;
    }

    result.draws.reserve(scene.render_2d.sprites.size());

    for (const SceneSprite2D& sprite : scene.render_2d.sprites) {
        DrawPacket packet{};

        const float snapped_x = scene.render_2d.camera.pixel_snap
            ? SnapToPixel(sprite.position.x - scene.render_2d.camera.center.x, scene.render_2d.camera.pixels_per_unit)
            : (sprite.position.x - scene.render_2d.camera.center.x);
        const float snapped_y = scene.render_2d.camera.pixel_snap
            ? SnapToPixel(sprite.position.y - scene.render_2d.camera.center.y, scene.render_2d.camera.pixels_per_unit)
            : (sprite.position.y - scene.render_2d.camera.center.y);

        glm::mat4 model(1.0F);
        model = glm::translate(model, glm::vec3(snapped_x, snapped_y, sprite.layer));
        model = glm::rotate(model, sprite.rotation_radians, glm::vec3(0.0F, 0.0F, 1.0F));
        model = glm::scale(model, glm::vec3(std::max(0.001F, sprite.size.x), std::max(0.001F, sprite.size.y), 1.0F));

        packet.model = model;
        packet.tint = sprite.tint;

        const auto it = bindless_index_by_asset_id_.find(sprite.asset_id);
        packet.texture_index = (it != bindless_index_by_asset_id_.end()) ? it->second : 0U;
        result.draws.push_back(packet);
    }

    result.indirect_commands.reserve(result.draws.size());
    for (std::size_t i = 0; i < result.draws.size(); ++i) {
        VkDrawIndirectCommand draw{};
        draw.vertexCount = 6;
        draw.instanceCount = 1;
        draw.firstVertex = 0;
        draw.firstInstance = static_cast<std::uint32_t>(i);
        result.indirect_commands.push_back(draw);
    }

    return result;
}

std::size_t SpriteBatch::BindlessTextureCount() const {
    return bindless_asset_ids_.size();
}

std::uint32_t SpriteBatch::RegisterBindlessAsset(const std::string& asset_id) {
    if (asset_id.empty()) {
        return 0;
    }

    const auto it = bindless_index_by_asset_id_.find(asset_id);
    if (it != bindless_index_by_asset_id_.end()) {
        return it->second;
    }

    const std::uint32_t index = static_cast<std::uint32_t>(bindless_asset_ids_.size());
    bindless_asset_ids_.push_back(asset_id);
    bindless_index_by_asset_id_[asset_id] = index;
    return index;
}
