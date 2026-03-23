#include "VulkanRenderer.h"

void VulkanRenderer::CreatePostProcessRenderPass() {
    if (!post_process_.post_process_enabled) {
        return;
    }
}

void VulkanRenderer::CreatePostProcessPipeline() {
    if (!post_process_.post_process_enabled) {
        return;
    }
}

void VulkanRenderer::CreateBloomTargets() {
    if (!post_process_.post_process_enabled) {
        return;
    }
}

void VulkanRenderer::RecordPostProcessPass(std::uint32_t image_index, const Camera& camera) {
    (void)image_index;
    (void)camera;
    if (!post_process_.post_process_enabled) {
        return;
    }
}

void VulkanRenderer::DestroyPostProcessResources() {
    post_process_pipeline_ = VK_NULL_HANDLE;
    post_process_pipeline_layout_ = VK_NULL_HANDLE;
    post_process_render_pass_ = VK_NULL_HANDLE;
    bloom_extract_view_ = VK_NULL_HANDLE;
    bloom_extract_image_ = VK_NULL_HANDLE;
    bloom_extract_memory_ = VK_NULL_HANDLE;
    bloom_blur_view_ = VK_NULL_HANDLE;
    bloom_blur_image_ = VK_NULL_HANDLE;
    bloom_blur_memory_ = VK_NULL_HANDLE;
}
