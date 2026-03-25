#pragma once

#include "Scene.h"
#include "SpriteBatch.h"
#include "TilemapChunk.h"
#include "src/core/Camera.h"

#include <GLFW/glfw3.h>
#include <glm/mat4x4.hpp>
#include <glm/vec4.hpp>
#include <vulkan/vulkan.h>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

class VulkanRenderer {
public:
    struct PerFramePushConstants {
        glm::mat4 view_proj{1.0F};
        glm::vec4 light_dir{0.0F, 1.0F, 0.0F, 0.0F};
        glm::vec4 light_color{1.0F, 1.0F, 1.0F, 1.0F};
    };

    struct PerDrawPushConstants {
        glm::mat4 model{1.0F};
        glm::vec4 color{1.0F, 1.0F, 1.0F, 1.0F};
    };
    struct BloomExtractPushConstants {
        glm::vec4 threshold{1.0F, 0.0F, 0.0F, 0.0F};
    };
    struct CombineTonemapPushConstants {
        glm::vec4 params{0.15F, 1.0F, 0.0F, 0.0F};
    };
    struct GaussianBlurPushConstants {
        glm::vec4 direction{1.0F, 0.0F, 0.0F, 0.0F};
    };

    VulkanRenderer() = default;
    ~VulkanRenderer() = default;

    void Init();
    void Shutdown();
    void RenderFrame(const Scene& scene, const Camera& camera);
    void DrawFPSOverlay(float fps, const std::string& day_night_text, const std::string& status_text, bool enabled);

    [[nodiscard]] bool ShouldClose() const;
    [[nodiscard]] bool IsKeyPressed(int key) const;
    [[nodiscard]] GLFWwindow* GetWindow() const;
    [[nodiscard]] float GetAspectRatio() const;
    void PollEvents() const;
    void SetWindowTitle(const std::string& title) const;

private:
    struct QueueFamilyIndices {
        std::optional<std::uint32_t> graphics_family;
        std::optional<std::uint32_t> present_family;

        [[nodiscard]] bool IsComplete() const {
            return graphics_family.has_value() && present_family.has_value();
        }
    };

    struct SwapChainSupportDetails {
        VkSurfaceCapabilitiesKHR capabilities{};
        std::vector<VkSurfaceFormatKHR> formats;
        std::vector<VkPresentModeKHR> present_modes;
    };

    void CreateInstance();
    void CreateDebugMessenger();
    void CreateSurface();
    void PickPhysicalDevice();
    void CreateLogicalDevice();
    void CreateSwapChain();
    void CreateImageViews();
    void CreateRenderPass();
    void CreateGraphicsPipeline();
    void CreateFramebuffers();
    void CreateOffscreenResources();
    void DestroyOffscreenResources();
    void CreateCommandPool();
    void CreateCommandBuffers();
    void CreateSyncObjects();
    void CleanupSwapChain();
    void RecreateSwapChain();
    void DrawFrame(const Scene& scene, const Camera& camera);
    void RecordCommandBuffer(std::uint32_t image_index, const Scene& scene, const Camera& camera);
    void Render2DLayer(std::uint32_t image_index, const Scene& scene);
    [[nodiscard]] std::uint32_t FindMemoryType(std::uint32_t type_filter, VkMemoryPropertyFlags properties) const;
    [[nodiscard]] VkFormat FindSupportedDepthFormat() const;

    [[nodiscard]] QueueFamilyIndices FindQueueFamilies(VkPhysicalDevice device) const;
    [[nodiscard]] bool IsDeviceSuitable(VkPhysicalDevice device) const;
    [[nodiscard]] bool CheckDeviceExtensionSupport(VkPhysicalDevice device) const;
    [[nodiscard]] SwapChainSupportDetails QuerySwapChainSupport(VkPhysicalDevice device) const;
    [[nodiscard]] VkSurfaceFormatKHR ChooseSwapSurfaceFormat(const std::vector<VkSurfaceFormatKHR>& available_formats) const;
    [[nodiscard]] VkPresentModeKHR ChooseSwapPresentMode(const std::vector<VkPresentModeKHR>& available_present_modes) const;
    [[nodiscard]] VkExtent2D ChooseSwapExtent(const VkSurfaceCapabilitiesKHR& capabilities) const;
    [[nodiscard]] bool ValidationLayersSupported() const;
    [[nodiscard]] bool DebugUtilsExtensionSupported() const;
    static VKAPI_ATTR VkBool32 VKAPI_CALL DebugCallback(
        VkDebugUtilsMessageSeverityFlagBitsEXT message_severity,
        VkDebugUtilsMessageTypeFlagsEXT message_type,
        const VkDebugUtilsMessengerCallbackDataEXT* callback_data,
        void* user_data);
    static VkResult CreateDebugUtilsMessengerEXT(
        VkInstance instance,
        const VkDebugUtilsMessengerCreateInfoEXT* create_info,
        const VkAllocationCallbacks* allocator,
        VkDebugUtilsMessengerEXT* debug_messenger);
    static void DestroyDebugUtilsMessengerEXT(
        VkInstance instance,
        VkDebugUtilsMessengerEXT debug_messenger,
        const VkAllocationCallbacks* allocator);

    static void FramebufferResizeCallback(GLFWwindow* window, int width, int height);

    GLFWwindow* window_ = nullptr;

    VkInstance instance_ = VK_NULL_HANDLE;
    VkDebugUtilsMessengerEXT debug_messenger_ = VK_NULL_HANDLE;
    VkSurfaceKHR surface_ = VK_NULL_HANDLE;
    VkPhysicalDevice physical_device_ = VK_NULL_HANDLE;
    VkDevice device_ = VK_NULL_HANDLE;
    VkQueue graphics_queue_ = VK_NULL_HANDLE;
    VkQueue present_queue_ = VK_NULL_HANDLE;

    VkSwapchainKHR swap_chain_ = VK_NULL_HANDLE;
    std::vector<VkImage> swap_chain_images_;
    VkFormat swap_chain_image_format_ = VK_FORMAT_UNDEFINED;
    VkExtent2D swap_chain_extent_{};
    std::vector<VkImageView> swap_chain_image_views_;
    std::vector<VkFramebuffer> swap_chain_framebuffers_;

    VkRenderPass render_pass_ = VK_NULL_HANDLE;
    VkPipelineLayout pipeline_layout_ = VK_NULL_HANDLE;
    VkPipeline graphics_pipeline_ = VK_NULL_HANDLE;
    VkRenderPass bloom_extract_render_pass_ = VK_NULL_HANDLE;
    VkRenderPass blur_render_pass_ = VK_NULL_HANDLE;
    VkRenderPass combine_render_pass_ = VK_NULL_HANDLE;
    VkPipeline bloom_extract_pipeline_ = VK_NULL_HANDLE;
    VkPipeline blur_pipeline_ = VK_NULL_HANDLE;
    VkPipeline combine_pipeline_ = VK_NULL_HANDLE;
    VkImage bloom_extract_image_ = VK_NULL_HANDLE;
    VkDeviceMemory bloom_extract_image_memory_ = VK_NULL_HANDLE;
    VkImageView bloom_extract_image_view_ = VK_NULL_HANDLE;
    VkFramebuffer bloom_extract_framebuffer_ = VK_NULL_HANDLE;
    VkImage blur_ping_image_ = VK_NULL_HANDLE;
    VkDeviceMemory blur_ping_image_memory_ = VK_NULL_HANDLE;
    VkImageView blur_ping_image_view_ = VK_NULL_HANDLE;
    VkFramebuffer blur_ping_framebuffer_ = VK_NULL_HANDLE;
    VkImage combine_image_ = VK_NULL_HANDLE;
    VkDeviceMemory combine_image_memory_ = VK_NULL_HANDLE;
    VkImageView combine_image_view_ = VK_NULL_HANDLE;
    VkFramebuffer combine_framebuffer_ = VK_NULL_HANDLE;
    VkSampler offscreen_color_sampler_ = VK_NULL_HANDLE;
    VkDescriptorSetLayout post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
    VkDescriptorSetLayout combine_descriptor_set_layout_ = VK_NULL_HANDLE;
    VkDescriptorPool post_process_descriptor_pool_ = VK_NULL_HANDLE;
    VkDescriptorPool combine_descriptor_pool_ = VK_NULL_HANDLE;
    VkDescriptorSet post_process_descriptor_set_ = VK_NULL_HANDLE;
    VkDescriptorSet combine_descriptor_set_ = VK_NULL_HANDLE;
    VkImage offscreen_color_image_ = VK_NULL_HANDLE;
    VkDeviceMemory offscreen_color_image_memory_ = VK_NULL_HANDLE;
    VkImageView offscreen_color_image_view_ = VK_NULL_HANDLE;
    VkImage offscreen_depth_image_ = VK_NULL_HANDLE;
    VkDeviceMemory offscreen_depth_image_memory_ = VK_NULL_HANDLE;
    VkImageView offscreen_depth_image_view_ = VK_NULL_HANDLE;
    VkFramebuffer offscreen_framebuffer_ = VK_NULL_HANDLE;
    VkFormat offscreen_depth_format_ = VK_FORMAT_UNDEFINED;
    bool post_process_enabled_ = true;
    SpriteBatch sprite_batch_{};
    TilemapChunk tilemap_chunk_{};

    VkCommandPool command_pool_ = VK_NULL_HANDLE;
    std::vector<VkCommandBuffer> command_buffers_;

    std::vector<VkSemaphore> image_available_semaphores_;
    std::vector<VkSemaphore> render_finished_semaphores_;
    std::vector<VkFence> in_flight_fences_;

    std::size_t current_frame_ = 0;
    bool framebuffer_resized_ = false;
    bool enable_validation_layers_ = false;
    std::string last_overlay_text_{};
};
