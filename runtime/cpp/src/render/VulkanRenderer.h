#pragma once

#include "../core/Camera.h"

#include <glm/mat4x4.hpp>
#include <glm/vec4.hpp>

#include <vulkan/vulkan.h>

#include <cstdint>

class VulkanRenderer {
public:
    struct PostProcessSettings {
        bool post_process_enabled = true;
        float bloom_threshold = 1.05F;
        float bloom_intensity = 0.42F;
        float bloom_soft_knee = 0.35F;
        float tonemap_exposure = 1.15F;
    };

    struct PerFramePushConstants {
        glm::mat4 view_proj{1.0F};
        glm::vec4 light_dir{0.0F, 1.0F, 0.0F, 0.0F};
        glm::vec4 light_color{1.0F, 1.0F, 1.0F, 1.0F};
    };

    struct PostProcessPushConstants {
        glm::vec4 bloom_a{1.05F, 0.42F, 0.35F, 1.15F};
        glm::vec4 bloom_b{0.0F, 0.0F, 0.0F, 0.0F};
    };

    void SetPostProcessEnabled(bool enabled) {
        post_process_.post_process_enabled = enabled;
    }

    [[nodiscard]] bool IsPostProcessEnabled() const {
        return post_process_.post_process_enabled;
    }

    [[nodiscard]] const PostProcessSettings& GetPostProcessSettings() const {
        return post_process_;
    }

private:
    void CreatePostProcessRenderPass();
    void CreatePostProcessPipeline();
    void CreateBloomTargets();
    void RecordPostProcessPass(std::uint32_t image_index, const Camera& camera);
    void DestroyPostProcessResources();

    PostProcessSettings post_process_{};
    VkRenderPass post_process_render_pass_ = VK_NULL_HANDLE;
    VkPipelineLayout post_process_pipeline_layout_ = VK_NULL_HANDLE;
    VkPipeline post_process_pipeline_ = VK_NULL_HANDLE;
    VkImage bloom_extract_image_ = VK_NULL_HANDLE;
    VkDeviceMemory bloom_extract_memory_ = VK_NULL_HANDLE;
    VkImageView bloom_extract_view_ = VK_NULL_HANDLE;
    VkImage bloom_blur_image_ = VK_NULL_HANDLE;
    VkDeviceMemory bloom_blur_memory_ = VK_NULL_HANDLE;
    VkImageView bloom_blur_view_ = VK_NULL_HANDLE;
};
