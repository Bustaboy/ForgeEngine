#include "SpriteBatch.h"

#include <glm/common.hpp>
#include <glm/gtc/matrix_transform.hpp>

#include <algorithm>
#include <cctype>
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

    approved_texture_path_by_asset_id_.clear();

    const fs::path root(approved_assets_root);
    if (!fs::exists(root) || !fs::is_directory(root)) {
        return;
    }

    for (const fs::directory_entry& entry : fs::recursive_directory_iterator(root)) {
        if (!entry.is_regular_file()) {
            continue;
        }

        fs::path extension = entry.path().extension();
        std::string extension_lower = extension.string();
        std::transform(extension_lower.begin(), extension_lower.end(), extension_lower.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        if (extension_lower != ".png" && extension_lower != ".jpg" && extension_lower != ".jpeg") {
            continue;
        }

        approved_texture_path_by_asset_id_[entry.path().stem().string()] = entry.path().string();
    }
}

bool SpriteBatch::RebuildTextureIndexForScene(const Scene& scene) {
    std::unordered_map<std::string, std::uint32_t> new_index_by_asset_id{};
    std::vector<std::string> new_asset_ids{};
    std::vector<std::string> new_texture_paths{};

    new_asset_ids.push_back("__default_white__");
    new_texture_paths.push_back("");
    new_index_by_asset_id["__default_white__"] = 0;

    auto register_if_approved = [&](const std::string& asset_id) {
        if (asset_id.empty()) {
            return;
        }

        if (new_index_by_asset_id.find(asset_id) != new_index_by_asset_id.end()) {
            return;
        }

        const auto approved_it = approved_texture_path_by_asset_id_.find(asset_id);
        if (approved_it == approved_texture_path_by_asset_id_.end()) {
            return;
        }

        const std::uint32_t index = static_cast<std::uint32_t>(new_asset_ids.size());
        new_asset_ids.push_back(asset_id);
        new_texture_paths.push_back(approved_it->second);
        new_index_by_asset_id[asset_id] = index;
    };

    if (scene.render_2d.enabled) {
        for (const SceneSprite2D& sprite : scene.render_2d.sprites) {
            register_if_approved(sprite.asset_id);
        }

        for (const SceneTilemap2D& tilemap : scene.render_2d.tilemaps) {
            register_if_approved(tilemap.tileset_asset_id);
        }
    }

    if (new_asset_ids == bindless_asset_ids_ && new_texture_paths == bindless_texture_paths_) {
        return false;
    }

    bindless_index_by_asset_id_ = std::move(new_index_by_asset_id);
    bindless_asset_ids_ = std::move(new_asset_ids);
    bindless_texture_paths_ = std::move(new_texture_paths);
    return true;
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

const std::vector<std::string>& SpriteBatch::BindlessTexturePaths() const {
    return bindless_texture_paths_;
}
