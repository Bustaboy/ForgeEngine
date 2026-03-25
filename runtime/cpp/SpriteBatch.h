#pragma once

#include "Scene.h"

#include <glm/mat4x4.hpp>
#include <vulkan/vulkan.h>

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

class SpriteBatch {
public:
    struct DrawPacket {
        glm::mat4 model{1.0F};
        glm::vec4 tint{1.0F};
        std::uint32_t texture_index = 0;
    };

    struct BuildResult {
        std::vector<DrawPacket> draws{};
        std::vector<VkDrawIndirectCommand> indirect_commands{};
    };

    void LoadApprovedAssets(const std::string& approved_assets_root);
    bool RebuildTextureIndexForScene(const Scene& scene);
    [[nodiscard]] BuildResult Build(const Scene& scene) const;
    [[nodiscard]] std::size_t BindlessTextureCount() const;
    [[nodiscard]] const std::vector<std::string>& BindlessTexturePaths() const;

private:
    std::unordered_map<std::string, std::string> approved_texture_path_by_asset_id_{};
    std::unordered_map<std::string, std::uint32_t> bindless_index_by_asset_id_{};
    std::vector<std::string> bindless_asset_ids_{};
    std::vector<std::string> bindless_texture_paths_{};

};
