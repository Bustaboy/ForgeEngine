#include "VulkanRenderer.h"

#include "Logger.h"
#include "TextureLoader.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <set>
#include <stdexcept>
#include <string>

#include <glm/gtc/matrix_transform.hpp>
#include <glm/common.hpp>

namespace {
constexpr std::uint32_t kWindowWidth = 1280;
constexpr std::uint32_t kWindowHeight = 720;
constexpr int kMaxFramesInFlight = 2;
constexpr std::uint32_t kQuadVertexCount = 6;
constexpr std::uint32_t kMaxBindlessTextures = 512;

const std::vector<const char*> kValidationLayers = {"VK_LAYER_KHRONOS_validation"};
const std::vector<const char*> kRequiredDeviceExtensions = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};
constexpr const char* kDebugUtilsExtensionName = VK_EXT_DEBUG_UTILS_EXTENSION_NAME;

#ifdef NDEBUG
constexpr bool kEnableValidationLayers = false;
#else
constexpr bool kEnableValidationLayers = true;
#endif

std::vector<char> ReadBinaryFile(const std::vector<std::filesystem::path>& candidates) {
    for (const std::filesystem::path& path : candidates) {
        if (!std::filesystem::exists(path)) {
            continue;
        }

        std::ifstream file(path, std::ios::ate | std::ios::binary);
        if (!file.is_open()) {
            continue;
        }

        const std::streamsize file_size = file.tellg();
        if (file_size <= 0) {
            continue;
        }

        std::vector<char> buffer(static_cast<std::size_t>(file_size));
        file.seekg(0);
        file.read(buffer.data(), file_size);
        return buffer;
    }

    throw std::runtime_error("Failed to load shader binary.");
}

VkShaderModule CreateShaderModule(VkDevice device, const std::vector<char>& code) {
    VkShaderModuleCreateInfo create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    create_info.codeSize = code.size();
    create_info.pCode = reinterpret_cast<const std::uint32_t*>(code.data());

    VkShaderModule shader_module = VK_NULL_HANDLE;
    VK_CHECK(vkCreateShaderModule(device, &create_info, nullptr, &shader_module));
    return shader_module;
}
}  // namespace

void VulkanRenderer::Init() {
    if (glfwInit() == GLFW_FALSE) {
        throw std::runtime_error("GLFW initialization failed.");
    }

    glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
    glfwWindowHint(GLFW_RESIZABLE, GLFW_TRUE);

    window_ = glfwCreateWindow(
        static_cast<int>(kWindowWidth),
        static_cast<int>(kWindowHeight),
        "ForgeEngine Runtime (Vulkan-first)",
        nullptr,
        nullptr);
    if (window_ == nullptr) {
        throw std::runtime_error("Failed to create GLFW window.");
    }

    glfwSetWindowUserPointer(window_, this);
    glfwSetFramebufferSizeCallback(window_, FramebufferResizeCallback);

    try {
        CreateInstance();
        CreateDebugMessenger();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateGraphicsPipeline();
        CreateFramebuffers();
        CreateOffscreenResources();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();
        sprite_batch_.LoadApprovedAssets("Assets/Approved");
    } catch (...) {
        Shutdown();
        throw;
    }
}

void VulkanRenderer::Shutdown() {
    if (device_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Waiting for device idle...");
        VK_CHECK(vkDeviceWaitIdle(device_));
    }

    DestroyBindlessTextures();

    GF_LOG_INFO("Destroying swapchain resources...");
    CleanupSwapChain();

    GF_LOG_INFO("Destroying synchronization objects...");
    for (std::size_t i = 0; i < image_available_semaphores_.size(); ++i) {
        vkDestroySemaphore(device_, image_available_semaphores_[i], nullptr);
        vkDestroySemaphore(device_, render_finished_semaphores_[i], nullptr);
        vkDestroyFence(device_, in_flight_fences_[i], nullptr);
    }

    if (command_pool_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Destroying command pool...");
        vkDestroyCommandPool(device_, command_pool_, nullptr);
        command_pool_ = VK_NULL_HANDLE;
    }

    if (device_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Destroying logical device...");
        vkDestroyDevice(device_, nullptr);
        device_ = VK_NULL_HANDLE;
    }

    if (surface_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Destroying surface...");
        vkDestroySurfaceKHR(instance_, surface_, nullptr);
        surface_ = VK_NULL_HANDLE;
    }

    if (debug_messenger_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Destroying debug messenger...");
        DestroyDebugUtilsMessengerEXT(instance_, debug_messenger_, nullptr);
        debug_messenger_ = VK_NULL_HANDLE;
    }

    if (instance_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Destroying Vulkan instance...");
        vkDestroyInstance(instance_, nullptr);
        instance_ = VK_NULL_HANDLE;
    }

    if (window_ != nullptr) {
        GF_LOG_INFO("Destroying GLFW window...");
        glfwDestroyWindow(window_);
        window_ = nullptr;
    }

    GF_LOG_INFO("Terminating GLFW...");
    glfwTerminate();
}

bool VulkanRenderer::ShouldClose() const {
    return window_ == nullptr || glfwWindowShouldClose(window_) != 0;
}

bool VulkanRenderer::IsKeyPressed(int key) const {
    if (window_ == nullptr) {
        return false;
    }

    return glfwGetKey(window_, key) == GLFW_PRESS;
}

GLFWwindow* VulkanRenderer::GetWindow() const {
    return window_;
}

float VulkanRenderer::GetAspectRatio() const {
    if (swap_chain_extent_.height == 0) {
        return 16.0F / 9.0F;
    }

    return static_cast<float>(swap_chain_extent_.width) / static_cast<float>(swap_chain_extent_.height);
}

void VulkanRenderer::PollEvents() const {
    glfwPollEvents();
}

void VulkanRenderer::SetWindowTitle(const std::string& title) const {
    if (window_ != nullptr) {
        glfwSetWindowTitle(window_, title.c_str());
    }
}

void VulkanRenderer::DrawFPSOverlay(
    float fps,
    const std::string& day_night_text,
    const std::string& status_text,
    bool enabled) {
    if (!enabled) {
        return;
    }

    const std::string overlay_text =
        "FPS: " + std::to_string(static_cast<int>(std::round(fps))) + " | " + day_night_text + " | " + status_text;
    if (overlay_text == last_overlay_text_) {
        return;
    }

    last_overlay_text_ = overlay_text;
    GF_LOG_INFO("[Overlay] " + overlay_text);
}

void VulkanRenderer::RenderFrame(const Scene& scene, const Camera& camera) {
    DrawFrame(scene, camera);
}

void VulkanRenderer::FramebufferResizeCallback(GLFWwindow* window, int width, int height) {
    auto* renderer = static_cast<VulkanRenderer*>(glfwGetWindowUserPointer(window));
    if (renderer != nullptr) {
        renderer->framebuffer_resized_ = true;
        GF_LOG_INFO("Window resize detected: " + std::to_string(width) + "x" + std::to_string(height));
    }
}

void VulkanRenderer::CreateInstance() {
    enable_validation_layers_ = false;
    if (kEnableValidationLayers) {
        if (ValidationLayersSupported() && DebugUtilsExtensionSupported()) {
            enable_validation_layers_ = true;
        } else {
            GF_LOG_WARN("Validation layers or debug utils extension unavailable; continuing without validation layers.");
        }
    }

    VkApplicationInfo application_info{};
    application_info.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    application_info.pApplicationName = "ForgeEngineRuntime";
    application_info.applicationVersion = VK_MAKE_VERSION(0, 1, 0);
    application_info.pEngineName = "ForgeEngine";
    application_info.engineVersion = VK_MAKE_VERSION(0, 1, 0);
    application_info.apiVersion = VK_API_VERSION_1_0;

    std::uint32_t extension_count = 0;
    const char** glfw_extensions = glfwGetRequiredInstanceExtensions(&extension_count);
    if (glfw_extensions == nullptr || extension_count == 0) {
        throw std::runtime_error("GLFW did not return required Vulkan extensions.");
    }

    std::vector<const char*> extensions(glfw_extensions, glfw_extensions + extension_count);
    if (enable_validation_layers_) {
        extensions.push_back(kDebugUtilsExtensionName);
    }

    VkInstanceCreateInfo create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    create_info.pApplicationInfo = &application_info;
    create_info.enabledExtensionCount = static_cast<std::uint32_t>(extensions.size());
    create_info.ppEnabledExtensionNames = extensions.data();

    if (enable_validation_layers_) {
        create_info.enabledLayerCount = static_cast<std::uint32_t>(kValidationLayers.size());
        create_info.ppEnabledLayerNames = kValidationLayers.data();
    }

    VK_CHECK(vkCreateInstance(&create_info, nullptr, &instance_));
}

void VulkanRenderer::CreateDebugMessenger() {
    if (!enable_validation_layers_) {
        return;
    }

    VkDebugUtilsMessengerCreateInfoEXT create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
    create_info.messageSeverity =
        VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
    create_info.messageType =
        VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
    create_info.pfnUserCallback = DebugCallback;
    create_info.pUserData = nullptr;

    VK_CHECK(CreateDebugUtilsMessengerEXT(instance_, &create_info, nullptr, &debug_messenger_));
}

void VulkanRenderer::CreateSurface() {
    VK_CHECK(glfwCreateWindowSurface(instance_, window_, nullptr, &surface_));
}

VulkanRenderer::QueueFamilyIndices VulkanRenderer::FindQueueFamilies(VkPhysicalDevice device) const {
    QueueFamilyIndices indices;

    std::uint32_t queue_family_count = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queue_family_count, nullptr);

    std::vector<VkQueueFamilyProperties> queue_families(queue_family_count);
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queue_family_count, queue_families.data());

    for (std::uint32_t i = 0; i < queue_family_count; ++i) {
        if ((queue_families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0U) {
            indices.graphics_family = i;
        }

        VkBool32 present_support = VK_FALSE;
        vkGetPhysicalDeviceSurfaceSupportKHR(device, i, surface_, &present_support);
        if (present_support == VK_TRUE) {
            indices.present_family = i;
        }

        if (indices.IsComplete()) {
            break;
        }
    }

    return indices;
}

bool VulkanRenderer::CheckDeviceExtensionSupport(VkPhysicalDevice device) const {
    std::uint32_t extension_count = 0;
    VK_CHECK(vkEnumerateDeviceExtensionProperties(device, nullptr, &extension_count, nullptr));

    std::vector<VkExtensionProperties> available_extensions(extension_count);
    VK_CHECK(vkEnumerateDeviceExtensionProperties(device, nullptr, &extension_count, available_extensions.data()));

    std::set<std::string> required_extensions(kRequiredDeviceExtensions.begin(), kRequiredDeviceExtensions.end());
    for (const auto& extension : available_extensions) {
        required_extensions.erase(extension.extensionName);
    }

    return required_extensions.empty();
}

VulkanRenderer::SwapChainSupportDetails VulkanRenderer::QuerySwapChainSupport(VkPhysicalDevice device) const {
    SwapChainSupportDetails details;

    vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device, surface_, &details.capabilities);

    std::uint32_t format_count = 0;
    vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface_, &format_count, nullptr);
    if (format_count > 0) {
        details.formats.resize(format_count);
        vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface_, &format_count, details.formats.data());
    }

    std::uint32_t present_mode_count = 0;
    vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface_, &present_mode_count, nullptr);
    if (present_mode_count > 0) {
        details.present_modes.resize(present_mode_count);
        vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface_, &present_mode_count, details.present_modes.data());
    }

    return details;
}

bool VulkanRenderer::IsDeviceSuitable(VkPhysicalDevice device) const {
    const QueueFamilyIndices indices = FindQueueFamilies(device);
    const bool extensions_supported = CheckDeviceExtensionSupport(device);

    bool swap_chain_adequate = false;
    if (extensions_supported) {
        const SwapChainSupportDetails swap_chain_support = QuerySwapChainSupport(device);
        swap_chain_adequate = !swap_chain_support.formats.empty() && !swap_chain_support.present_modes.empty();
    }

    return indices.IsComplete() && extensions_supported && swap_chain_adequate;
}

void VulkanRenderer::PickPhysicalDevice() {
    std::uint32_t device_count = 0;
    VK_CHECK(vkEnumeratePhysicalDevices(instance_, &device_count, nullptr));
    if (device_count == 0) {
        throw std::runtime_error("Failed to find GPUs with Vulkan support.");
    }

    std::vector<VkPhysicalDevice> devices(device_count);
    VK_CHECK(vkEnumeratePhysicalDevices(instance_, &device_count, devices.data()));

    for (const auto& device : devices) {
        if (IsDeviceSuitable(device)) {
            physical_device_ = device;
            break;
        }
    }

    if (physical_device_ == VK_NULL_HANDLE) {
        throw std::runtime_error("Failed to find a suitable GPU.");
    }
}

void VulkanRenderer::CreateLogicalDevice() {
    const QueueFamilyIndices indices = FindQueueFamilies(physical_device_);

    std::vector<VkDeviceQueueCreateInfo> queue_create_infos;
    std::set<std::uint32_t> unique_queue_families = {
        indices.graphics_family.value(),
        indices.present_family.value(),
    };

    const float queue_priority = 1.0F;
    for (std::uint32_t queue_family : unique_queue_families) {
        VkDeviceQueueCreateInfo queue_create_info{};
        queue_create_info.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
        queue_create_info.queueFamilyIndex = queue_family;
        queue_create_info.queueCount = 1;
        queue_create_info.pQueuePriorities = &queue_priority;
        queue_create_infos.push_back(queue_create_info);
    }

    VkPhysicalDeviceFeatures available_features{};
    vkGetPhysicalDeviceFeatures(physical_device_, &available_features);
    VkPhysicalDeviceFeatures device_features{};
    device_features.shaderSampledImageArrayDynamicIndexing = available_features.shaderSampledImageArrayDynamicIndexing;

    VkDeviceCreateInfo create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    create_info.queueCreateInfoCount = static_cast<std::uint32_t>(queue_create_infos.size());
    create_info.pQueueCreateInfos = queue_create_infos.data();
    create_info.pEnabledFeatures = &device_features;
    create_info.enabledExtensionCount = static_cast<std::uint32_t>(kRequiredDeviceExtensions.size());
    create_info.ppEnabledExtensionNames = kRequiredDeviceExtensions.data();

    if (enable_validation_layers_) {
        create_info.enabledLayerCount = static_cast<std::uint32_t>(kValidationLayers.size());
        create_info.ppEnabledLayerNames = kValidationLayers.data();
    }

    VK_CHECK(vkCreateDevice(physical_device_, &create_info, nullptr, &device_));

    vkGetDeviceQueue(device_, indices.graphics_family.value(), 0, &graphics_queue_);
    vkGetDeviceQueue(device_, indices.present_family.value(), 0, &present_queue_);
}

VkSurfaceFormatKHR VulkanRenderer::ChooseSwapSurfaceFormat(const std::vector<VkSurfaceFormatKHR>& available_formats) const {
    for (const auto& available_format : available_formats) {
        if (available_format.format == VK_FORMAT_B8G8R8A8_SRGB &&
            available_format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR) {
            return available_format;
        }
    }

    return available_formats[0];
}

VkPresentModeKHR VulkanRenderer::ChooseSwapPresentMode(const std::vector<VkPresentModeKHR>& available_present_modes) const {
    for (const auto& available_present_mode : available_present_modes) {
        if (available_present_mode == VK_PRESENT_MODE_MAILBOX_KHR) {
            return available_present_mode;
        }
    }

    return VK_PRESENT_MODE_FIFO_KHR;
}

VkExtent2D VulkanRenderer::ChooseSwapExtent(const VkSurfaceCapabilitiesKHR& capabilities) const {
    if (capabilities.currentExtent.width != std::numeric_limits<std::uint32_t>::max()) {
        return capabilities.currentExtent;
    }

    int width = 0;
    int height = 0;
    glfwGetFramebufferSize(window_, &width, &height);

    VkExtent2D actual_extent = {
        static_cast<std::uint32_t>(width),
        static_cast<std::uint32_t>(height),
    };

    actual_extent.width = std::clamp(
        actual_extent.width,
        capabilities.minImageExtent.width,
        capabilities.maxImageExtent.width);
    actual_extent.height = std::clamp(
        actual_extent.height,
        capabilities.minImageExtent.height,
        capabilities.maxImageExtent.height);

    return actual_extent;
}

void VulkanRenderer::CreateSwapChain() {
    const SwapChainSupportDetails swap_chain_support = QuerySwapChainSupport(physical_device_);

    const VkSurfaceFormatKHR surface_format = ChooseSwapSurfaceFormat(swap_chain_support.formats);
    const VkPresentModeKHR present_mode = ChooseSwapPresentMode(swap_chain_support.present_modes);
    const VkExtent2D extent = ChooseSwapExtent(swap_chain_support.capabilities);

    std::uint32_t image_count = swap_chain_support.capabilities.minImageCount + 1;
    if (swap_chain_support.capabilities.maxImageCount > 0 &&
        image_count > swap_chain_support.capabilities.maxImageCount) {
        image_count = swap_chain_support.capabilities.maxImageCount;
    }

    VkSwapchainCreateInfoKHR create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
    create_info.surface = surface_;
    create_info.minImageCount = image_count;
    create_info.imageFormat = surface_format.format;
    create_info.imageColorSpace = surface_format.colorSpace;
    create_info.imageExtent = extent;
    create_info.imageArrayLayers = 1;
    create_info.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;

    const QueueFamilyIndices indices = FindQueueFamilies(physical_device_);
    const std::uint32_t queue_family_indices[] = {
        indices.graphics_family.value(),
        indices.present_family.value(),
    };

    if (indices.graphics_family != indices.present_family) {
        create_info.imageSharingMode = VK_SHARING_MODE_CONCURRENT;
        create_info.queueFamilyIndexCount = 2;
        create_info.pQueueFamilyIndices = queue_family_indices;
    } else {
        create_info.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
    }

    create_info.preTransform = swap_chain_support.capabilities.currentTransform;
    create_info.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
    create_info.presentMode = present_mode;
    create_info.clipped = VK_TRUE;

    VK_CHECK(vkCreateSwapchainKHR(device_, &create_info, nullptr, &swap_chain_));

    VK_CHECK(vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, nullptr));
    swap_chain_images_.resize(image_count);
    VK_CHECK(vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, swap_chain_images_.data()));

    swap_chain_image_format_ = surface_format.format;
    swap_chain_extent_ = extent;
}

void VulkanRenderer::CreateImageViews() {
    swap_chain_image_views_.resize(swap_chain_images_.size());

    for (std::size_t i = 0; i < swap_chain_images_.size(); ++i) {
        VkImageViewCreateInfo create_info{};
        create_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        create_info.image = swap_chain_images_[i];
        create_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
        create_info.format = swap_chain_image_format_;
        create_info.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
        create_info.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
        create_info.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
        create_info.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
        create_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        create_info.subresourceRange.baseMipLevel = 0;
        create_info.subresourceRange.levelCount = 1;
        create_info.subresourceRange.baseArrayLayer = 0;
        create_info.subresourceRange.layerCount = 1;

        VK_CHECK(vkCreateImageView(device_, &create_info, nullptr, &swap_chain_image_views_[i]));
    }
}

void VulkanRenderer::CreateRenderPass() {
    VkAttachmentDescription color_attachment{};
    color_attachment.format = swap_chain_image_format_;
    color_attachment.samples = VK_SAMPLE_COUNT_1_BIT;
    color_attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    color_attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    color_attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    color_attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    color_attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    color_attachment.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference color_attachment_ref{};
    color_attachment_ref.attachment = 0;
    color_attachment_ref.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &color_attachment_ref;

    VkSubpassDependency dependency{};
    dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    dependency.dstSubpass = 0;
    dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.srcAccessMask = 0;
    dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    VkRenderPassCreateInfo render_pass_info{};
    render_pass_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    render_pass_info.attachmentCount = 1;
    render_pass_info.pAttachments = &color_attachment;
    render_pass_info.subpassCount = 1;
    render_pass_info.pSubpasses = &subpass;
    render_pass_info.dependencyCount = 1;
    render_pass_info.pDependencies = &dependency;

    VK_CHECK(vkCreateRenderPass(device_, &render_pass_info, nullptr, &render_pass_));

    VkAttachmentDescription bloom_color_attachment{};
    bloom_color_attachment.format = swap_chain_image_format_;
    bloom_color_attachment.samples = VK_SAMPLE_COUNT_1_BIT;
    bloom_color_attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    bloom_color_attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    bloom_color_attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    bloom_color_attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    bloom_color_attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    bloom_color_attachment.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference bloom_color_attachment_ref{};
    bloom_color_attachment_ref.attachment = 0;
    bloom_color_attachment_ref.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription bloom_subpass{};
    bloom_subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    bloom_subpass.colorAttachmentCount = 1;
    bloom_subpass.pColorAttachments = &bloom_color_attachment_ref;

    VkSubpassDependency bloom_dependency{};
    bloom_dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    bloom_dependency.dstSubpass = 0;
    bloom_dependency.srcStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
    bloom_dependency.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
    bloom_dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    bloom_dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    VkRenderPassCreateInfo bloom_render_pass_info{};
    bloom_render_pass_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    bloom_render_pass_info.attachmentCount = 1;
    bloom_render_pass_info.pAttachments = &bloom_color_attachment;
    bloom_render_pass_info.subpassCount = 1;
    bloom_render_pass_info.pSubpasses = &bloom_subpass;
    bloom_render_pass_info.dependencyCount = 1;
    bloom_render_pass_info.pDependencies = &bloom_dependency;

    VK_CHECK(vkCreateRenderPass(device_, &bloom_render_pass_info, nullptr, &bloom_extract_render_pass_));
    VK_CHECK(vkCreateRenderPass(device_, &bloom_render_pass_info, nullptr, &blur_render_pass_));

    VkAttachmentDescription combine_color_attachment{};
    combine_color_attachment.format = swap_chain_image_format_;
    combine_color_attachment.samples = VK_SAMPLE_COUNT_1_BIT;
    combine_color_attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    combine_color_attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    combine_color_attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    combine_color_attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    combine_color_attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    combine_color_attachment.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference combine_color_attachment_ref{};
    combine_color_attachment_ref.attachment = 0;
    combine_color_attachment_ref.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription combine_subpass{};
    combine_subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    combine_subpass.colorAttachmentCount = 1;
    combine_subpass.pColorAttachments = &combine_color_attachment_ref;

    VkSubpassDependency combine_dependency{};
    combine_dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    combine_dependency.dstSubpass = 0;
    combine_dependency.srcStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
    combine_dependency.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
    combine_dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    combine_dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    VkRenderPassCreateInfo combine_render_pass_info{};
    combine_render_pass_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    combine_render_pass_info.attachmentCount = 1;
    combine_render_pass_info.pAttachments = &combine_color_attachment;
    combine_render_pass_info.subpassCount = 1;
    combine_render_pass_info.pSubpasses = &combine_subpass;
    combine_render_pass_info.dependencyCount = 1;
    combine_render_pass_info.pDependencies = &combine_dependency;

    VK_CHECK(vkCreateRenderPass(device_, &combine_render_pass_info, nullptr, &combine_render_pass_));
}

void VulkanRenderer::CreateGraphicsPipeline() {
    const std::filesystem::path cwd = std::filesystem::current_path();
    const std::vector<char> vert_shader_code = ReadBinaryFile({
        cwd / "shaders/vertex.vert.spv",
        cwd / "../shaders/vertex.vert.spv",
        cwd / "../../shaders/vertex.vert.spv",
    });
    const std::vector<char> frag_shader_code = ReadBinaryFile({
        cwd / "shaders/fragment.frag.spv",
        cwd / "../shaders/fragment.frag.spv",
        cwd / "../../shaders/fragment.frag.spv",
    });
    const std::vector<char> bloom_extract_frag_shader_code = ReadBinaryFile({
        cwd / "shaders/bloom_extract.frag.spv",
        cwd / "../shaders/bloom_extract.frag.spv",
        cwd / "../../shaders/bloom_extract.frag.spv",
        cwd / "shaders/fragment.frag.spv",
        cwd / "../shaders/fragment.frag.spv",
        cwd / "../../shaders/fragment.frag.spv",
    });
    const std::vector<char> combine_tonemap_frag_shader_code = ReadBinaryFile({
        cwd / "shaders/combine_tonemap.frag.spv",
        cwd / "../shaders/combine_tonemap.frag.spv",
        cwd / "../../shaders/combine_tonemap.frag.spv",
        cwd / "shaders/fragment.frag.spv",
        cwd / "../shaders/fragment.frag.spv",
        cwd / "../../shaders/fragment.frag.spv",
    });
    const std::vector<char> gaussian_blur_frag_shader_code = ReadBinaryFile({
        cwd / "shaders/gaussian_blur.frag.spv",
        cwd / "../shaders/gaussian_blur.frag.spv",
        cwd / "../../shaders/gaussian_blur.frag.spv",
        cwd / "shaders/bloom_extract.frag.spv",
        cwd / "../shaders/bloom_extract.frag.spv",
        cwd / "../../shaders/bloom_extract.frag.spv",
        cwd / "shaders/fragment.frag.spv",
        cwd / "../shaders/fragment.frag.spv",
        cwd / "../../shaders/fragment.frag.spv",
    });

    const VkShaderModule vert_shader_module = CreateShaderModule(device_, vert_shader_code);
    const VkShaderModule frag_shader_module = CreateShaderModule(device_, frag_shader_code);
    const VkShaderModule bloom_extract_frag_shader_module = CreateShaderModule(device_, bloom_extract_frag_shader_code);
    const VkShaderModule combine_tonemap_frag_shader_module = CreateShaderModule(device_, combine_tonemap_frag_shader_code);
    const VkShaderModule gaussian_blur_frag_shader_module = CreateShaderModule(device_, gaussian_blur_frag_shader_code);

    VkPipelineShaderStageCreateInfo vert_shader_stage_info{};
    vert_shader_stage_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vert_shader_stage_info.stage = VK_SHADER_STAGE_VERTEX_BIT;
    vert_shader_stage_info.module = vert_shader_module;
    vert_shader_stage_info.pName = "main";

    VkPipelineShaderStageCreateInfo frag_shader_stage_info{};
    frag_shader_stage_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    frag_shader_stage_info.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    frag_shader_stage_info.module = frag_shader_module;
    frag_shader_stage_info.pName = "main";

    VkPipelineShaderStageCreateInfo shader_stages[] = {vert_shader_stage_info, frag_shader_stage_info};

    VkPipelineVertexInputStateCreateInfo vertex_input_info{};
    vertex_input_info.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertex_input_info.vertexBindingDescriptionCount = 0;
    vertex_input_info.pVertexBindingDescriptions = nullptr;
    vertex_input_info.vertexAttributeDescriptionCount = 0;
    vertex_input_info.pVertexAttributeDescriptions = nullptr;

    VkPipelineInputAssemblyStateCreateInfo input_assembly{};
    input_assembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    input_assembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    input_assembly.primitiveRestartEnable = VK_FALSE;

    VkViewport viewport{};
    viewport.x = 0.0F;
    viewport.y = 0.0F;
    viewport.width = static_cast<float>(swap_chain_extent_.width);
    viewport.height = static_cast<float>(swap_chain_extent_.height);
    viewport.minDepth = 0.0F;
    viewport.maxDepth = 1.0F;

    VkRect2D scissor{};
    scissor.offset = {0, 0};
    scissor.extent = swap_chain_extent_;

    VkPipelineViewportStateCreateInfo viewport_state{};
    viewport_state.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewport_state.viewportCount = 1;
    viewport_state.pViewports = &viewport;
    viewport_state.scissorCount = 1;
    viewport_state.pScissors = &scissor;

    VkPipelineRasterizationStateCreateInfo rasterizer{};
    rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rasterizer.depthClampEnable = VK_FALSE;
    rasterizer.rasterizerDiscardEnable = VK_FALSE;
    rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
    rasterizer.lineWidth = 1.0F;
    rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
    rasterizer.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rasterizer.depthBiasEnable = VK_FALSE;

    VkPipelineMultisampleStateCreateInfo multisampling{};
    multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.sampleShadingEnable = VK_FALSE;
    multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineColorBlendAttachmentState color_blend_attachment{};
    color_blend_attachment.colorWriteMask =
        VK_COLOR_COMPONENT_R_BIT |
        VK_COLOR_COMPONENT_G_BIT |
        VK_COLOR_COMPONENT_B_BIT |
        VK_COLOR_COMPONENT_A_BIT;
    color_blend_attachment.blendEnable = VK_TRUE;
    color_blend_attachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
    color_blend_attachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    color_blend_attachment.colorBlendOp = VK_BLEND_OP_ADD;
    color_blend_attachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
    color_blend_attachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    color_blend_attachment.alphaBlendOp = VK_BLEND_OP_ADD;

    VkPipelineColorBlendStateCreateInfo color_blending{};
    color_blending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    color_blending.logicOpEnable = VK_FALSE;
    color_blending.attachmentCount = 1;
    color_blending.pAttachments = &color_blend_attachment;

    VkPushConstantRange push_constant_ranges[2]{};
    push_constant_ranges[0].stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
    push_constant_ranges[0].offset = 0;
    push_constant_ranges[0].size = sizeof(PerFramePushConstants);
    push_constant_ranges[1].stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
    push_constant_ranges[1].offset = sizeof(PerFramePushConstants);
    push_constant_ranges[1].size = sizeof(PerDrawPushConstants);
    VkPushConstantRange post_process_push_constant_range{};
    post_process_push_constant_range.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    post_process_push_constant_range.offset = sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants);
    post_process_push_constant_range.size = sizeof(BloomExtractPushConstants) + sizeof(CombineTonemapPushConstants);
    std::array<VkPushConstantRange, 3> all_push_constant_ranges = {
        push_constant_ranges[0],
        push_constant_ranges[1],
        post_process_push_constant_range,
    };

    VkDescriptorSetLayoutBinding post_process_sampler_binding{};
    post_process_sampler_binding.binding = 0;
    post_process_sampler_binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    post_process_sampler_binding.descriptorCount = 1;
    post_process_sampler_binding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    post_process_sampler_binding.pImmutableSamplers = nullptr;
    VkDescriptorSetLayoutCreateInfo post_process_descriptor_set_layout_info{};
    post_process_descriptor_set_layout_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    post_process_descriptor_set_layout_info.bindingCount = 1;
    post_process_descriptor_set_layout_info.pBindings = &post_process_sampler_binding;
    VK_CHECK(vkCreateDescriptorSetLayout(
        device_,
        &post_process_descriptor_set_layout_info,
        nullptr,
        &post_process_descriptor_set_layout_));
    VkDescriptorSetLayoutBinding combine_scene_sampler_binding{};
    combine_scene_sampler_binding.binding = 0;
    combine_scene_sampler_binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_scene_sampler_binding.descriptorCount = 1;
    combine_scene_sampler_binding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    combine_scene_sampler_binding.pImmutableSamplers = nullptr;
    VkDescriptorSetLayoutBinding combine_bloom_sampler_binding{};
    combine_bloom_sampler_binding.binding = 1;
    combine_bloom_sampler_binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_bloom_sampler_binding.descriptorCount = 1;
    combine_bloom_sampler_binding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    combine_bloom_sampler_binding.pImmutableSamplers = nullptr;
    std::array<VkDescriptorSetLayoutBinding, 2> combine_sampler_bindings = {
        combine_scene_sampler_binding,
        combine_bloom_sampler_binding,
    };
    VkDescriptorSetLayoutCreateInfo combine_descriptor_set_layout_info{};
    combine_descriptor_set_layout_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    combine_descriptor_set_layout_info.bindingCount = static_cast<std::uint32_t>(combine_sampler_bindings.size());
    combine_descriptor_set_layout_info.pBindings = combine_sampler_bindings.data();
    const VkResult combine_descriptor_set_layout_result = vkCreateDescriptorSetLayout(
        device_,
        &combine_descriptor_set_layout_info,
        nullptr,
        &combine_descriptor_set_layout_);
    if (combine_descriptor_set_layout_result != VK_SUCCESS) {
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(combine_descriptor_set_layout_result);
    }
    VkDescriptorSetLayoutBinding bindless_sampler_binding{};
    bindless_sampler_binding.binding = 0;
    bindless_sampler_binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindless_sampler_binding.descriptorCount = kMaxBindlessTextures;
    bindless_sampler_binding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindless_sampler_binding.pImmutableSamplers = nullptr;
    VkDescriptorSetLayoutCreateInfo bindless_descriptor_set_layout_info{};
    bindless_descriptor_set_layout_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    bindless_descriptor_set_layout_info.bindingCount = 1;
    bindless_descriptor_set_layout_info.pBindings = &bindless_sampler_binding;
    const VkResult bindless_descriptor_set_layout_result = vkCreateDescriptorSetLayout(
        device_,
        &bindless_descriptor_set_layout_info,
        nullptr,
        &bindless_texture_descriptor_set_layout_);
    if (bindless_descriptor_set_layout_result != VK_SUCCESS) {
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(bindless_descriptor_set_layout_result);
    }

    VkPipelineLayoutCreateInfo pipeline_layout_info{};
    pipeline_layout_info.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pipeline_layout_info.setLayoutCount = 0;
    pipeline_layout_info.pSetLayouts = nullptr;
    pipeline_layout_info.pushConstantRangeCount = 2;
    pipeline_layout_info.pPushConstantRanges = push_constant_ranges;
    VkDescriptorSetLayout pipeline_descriptor_set_layouts[] = {
        post_process_descriptor_set_layout_,
        combine_descriptor_set_layout_,
        bindless_texture_descriptor_set_layout_,
    };
    pipeline_layout_info.setLayoutCount = 3;
    pipeline_layout_info.pSetLayouts = pipeline_descriptor_set_layouts;
    pipeline_layout_info.pushConstantRangeCount = static_cast<std::uint32_t>(all_push_constant_ranges.size());
    pipeline_layout_info.pPushConstantRanges = all_push_constant_ranges.data();

    const VkResult pipeline_layout_result = vkCreatePipelineLayout(device_, &pipeline_layout_info, nullptr, &pipeline_layout_);
    if (pipeline_layout_result != VK_SUCCESS) {
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(pipeline_layout_result);
    }

    VkGraphicsPipelineCreateInfo pipeline_info{};
    pipeline_info.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipeline_info.stageCount = 2;
    pipeline_info.pStages = shader_stages;
    pipeline_info.pVertexInputState = &vertex_input_info;
    pipeline_info.pInputAssemblyState = &input_assembly;
    pipeline_info.pViewportState = &viewport_state;
    pipeline_info.pRasterizationState = &rasterizer;
    pipeline_info.pMultisampleState = &multisampling;
    pipeline_info.pDepthStencilState = nullptr;
    pipeline_info.pColorBlendState = &color_blending;
    pipeline_info.layout = pipeline_layout_;
    pipeline_info.renderPass = render_pass_;
    pipeline_info.subpass = 0;
    pipeline_info.basePipelineHandle = VK_NULL_HANDLE;

    const VkResult graphics_pipeline_result =
        vkCreateGraphicsPipelines(device_, VK_NULL_HANDLE, 1, &pipeline_info, nullptr, &graphics_pipeline_);
    if (graphics_pipeline_result != VK_SUCCESS) {
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(graphics_pipeline_result);
    }

    VkPipelineShaderStageCreateInfo bloom_extract_frag_shader_stage_info{};
    bloom_extract_frag_shader_stage_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    bloom_extract_frag_shader_stage_info.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    bloom_extract_frag_shader_stage_info.module = bloom_extract_frag_shader_module;
    bloom_extract_frag_shader_stage_info.pName = "main";
    VkPipelineShaderStageCreateInfo bloom_extract_shader_stages[] = {
        vert_shader_stage_info,
        bloom_extract_frag_shader_stage_info};
    VkGraphicsPipelineCreateInfo bloom_extract_pipeline_info = pipeline_info;
    bloom_extract_pipeline_info.pStages = bloom_extract_shader_stages;
    bloom_extract_pipeline_info.renderPass = bloom_extract_render_pass_;
    const VkResult bloom_extract_pipeline_result = vkCreateGraphicsPipelines(
        device_,
        VK_NULL_HANDLE,
        1,
        &bloom_extract_pipeline_info,
        nullptr,
        &bloom_extract_pipeline_);
    if (bloom_extract_pipeline_result != VK_SUCCESS) {
        vkDestroyPipeline(device_, graphics_pipeline_, nullptr);
        graphics_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(bloom_extract_pipeline_result);
    }
    VkPipelineShaderStageCreateInfo gaussian_blur_frag_shader_stage_info{};
    gaussian_blur_frag_shader_stage_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    gaussian_blur_frag_shader_stage_info.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    gaussian_blur_frag_shader_stage_info.module = gaussian_blur_frag_shader_module;
    gaussian_blur_frag_shader_stage_info.pName = "main";
    VkPipelineShaderStageCreateInfo gaussian_blur_shader_stages[] = {
        vert_shader_stage_info,
        gaussian_blur_frag_shader_stage_info};
    VkGraphicsPipelineCreateInfo gaussian_blur_pipeline_info = pipeline_info;
    gaussian_blur_pipeline_info.pStages = gaussian_blur_shader_stages;
    gaussian_blur_pipeline_info.renderPass = blur_render_pass_;
    const VkResult gaussian_blur_pipeline_result = vkCreateGraphicsPipelines(
        device_,
        VK_NULL_HANDLE,
        1,
        &gaussian_blur_pipeline_info,
        nullptr,
        &blur_pipeline_);
    if (gaussian_blur_pipeline_result != VK_SUCCESS) {
        vkDestroyPipeline(device_, bloom_extract_pipeline_, nullptr);
        bloom_extract_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipeline(device_, graphics_pipeline_, nullptr);
        graphics_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(gaussian_blur_pipeline_result);
    }
    VkPipelineShaderStageCreateInfo combine_tonemap_frag_shader_stage_info{};
    combine_tonemap_frag_shader_stage_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    combine_tonemap_frag_shader_stage_info.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    combine_tonemap_frag_shader_stage_info.module = combine_tonemap_frag_shader_module;
    combine_tonemap_frag_shader_stage_info.pName = "main";
    VkPipelineShaderStageCreateInfo combine_tonemap_shader_stages[] = {
        vert_shader_stage_info,
        combine_tonemap_frag_shader_stage_info};
    VkGraphicsPipelineCreateInfo combine_tonemap_pipeline_info = pipeline_info;
    combine_tonemap_pipeline_info.pStages = combine_tonemap_shader_stages;
    combine_tonemap_pipeline_info.renderPass = combine_render_pass_;
    const VkResult combine_pipeline_result = vkCreateGraphicsPipelines(
        device_,
        VK_NULL_HANDLE,
        1,
        &combine_tonemap_pipeline_info,
        nullptr,
        &combine_pipeline_);
    if (combine_pipeline_result != VK_SUCCESS) {
        vkDestroyPipeline(device_, blur_pipeline_, nullptr);
        blur_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipeline(device_, bloom_extract_pipeline_, nullptr);
        bloom_extract_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipeline(device_, graphics_pipeline_, nullptr);
        graphics_pipeline_ = VK_NULL_HANDLE;
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
        vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, frag_shader_module, nullptr);
        vkDestroyShaderModule(device_, vert_shader_module, nullptr);
        VK_CHECK(combine_pipeline_result);
    }

    vkDestroyShaderModule(device_, gaussian_blur_frag_shader_module, nullptr);
    vkDestroyShaderModule(device_, combine_tonemap_frag_shader_module, nullptr);
    vkDestroyShaderModule(device_, bloom_extract_frag_shader_module, nullptr);
    vkDestroyShaderModule(device_, frag_shader_module, nullptr);
    vkDestroyShaderModule(device_, vert_shader_module, nullptr);
}

void VulkanRenderer::CreateFramebuffers() {
    swap_chain_framebuffers_.resize(swap_chain_image_views_.size());

    for (std::size_t i = 0; i < swap_chain_image_views_.size(); ++i) {
        VkImageView attachments[] = {swap_chain_image_views_[i]};

        VkFramebufferCreateInfo framebuffer_info{};
        framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        framebuffer_info.renderPass = render_pass_;
        framebuffer_info.attachmentCount = 1;
        framebuffer_info.pAttachments = attachments;
        framebuffer_info.width = swap_chain_extent_.width;
        framebuffer_info.height = swap_chain_extent_.height;
        framebuffer_info.layers = 1;

        VK_CHECK(vkCreateFramebuffer(device_, &framebuffer_info, nullptr, &swap_chain_framebuffers_[i]));
    }
}

void VulkanRenderer::CreateOffscreenResources() {
    VkImageCreateInfo color_image_info{};
    color_image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    color_image_info.imageType = VK_IMAGE_TYPE_2D;
    color_image_info.extent.width = swap_chain_extent_.width;
    color_image_info.extent.height = swap_chain_extent_.height;
    color_image_info.extent.depth = 1;
    color_image_info.mipLevels = 1;
    color_image_info.arrayLayers = 1;
    color_image_info.format = swap_chain_image_format_;
    color_image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
    color_image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    color_image_info.usage =
        VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT |
        VK_IMAGE_USAGE_TRANSFER_SRC_BIT |
        VK_IMAGE_USAGE_SAMPLED_BIT;
    color_image_info.samples = VK_SAMPLE_COUNT_1_BIT;
    color_image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateImage(device_, &color_image_info, nullptr, &offscreen_color_image_));

    VkMemoryRequirements color_memory_requirements{};
    vkGetImageMemoryRequirements(device_, offscreen_color_image_, &color_memory_requirements);

    VkMemoryAllocateInfo color_allocate_info{};
    color_allocate_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    color_allocate_info.allocationSize = color_memory_requirements.size;
    color_allocate_info.memoryTypeIndex = FindMemoryType(
        color_memory_requirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    VK_CHECK(vkAllocateMemory(device_, &color_allocate_info, nullptr, &offscreen_color_image_memory_));
    VK_CHECK(vkBindImageMemory(device_, offscreen_color_image_, offscreen_color_image_memory_, 0));

    VkImageViewCreateInfo color_image_view_info{};
    color_image_view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    color_image_view_info.image = offscreen_color_image_;
    color_image_view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
    color_image_view_info.format = swap_chain_image_format_;
    color_image_view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    color_image_view_info.subresourceRange.baseMipLevel = 0;
    color_image_view_info.subresourceRange.levelCount = 1;
    color_image_view_info.subresourceRange.baseArrayLayer = 0;
    color_image_view_info.subresourceRange.layerCount = 1;
    VK_CHECK(vkCreateImageView(device_, &color_image_view_info, nullptr, &offscreen_color_image_view_));

    offscreen_depth_format_ = FindSupportedDepthFormat();

    VkImageCreateInfo depth_image_info{};
    depth_image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    depth_image_info.imageType = VK_IMAGE_TYPE_2D;
    depth_image_info.extent.width = swap_chain_extent_.width;
    depth_image_info.extent.height = swap_chain_extent_.height;
    depth_image_info.extent.depth = 1;
    depth_image_info.mipLevels = 1;
    depth_image_info.arrayLayers = 1;
    depth_image_info.format = offscreen_depth_format_;
    depth_image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
    depth_image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    depth_image_info.usage = VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
    depth_image_info.samples = VK_SAMPLE_COUNT_1_BIT;
    depth_image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateImage(device_, &depth_image_info, nullptr, &offscreen_depth_image_));

    VkMemoryRequirements depth_memory_requirements{};
    vkGetImageMemoryRequirements(device_, offscreen_depth_image_, &depth_memory_requirements);

    VkMemoryAllocateInfo depth_allocate_info{};
    depth_allocate_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    depth_allocate_info.allocationSize = depth_memory_requirements.size;
    depth_allocate_info.memoryTypeIndex = FindMemoryType(
        depth_memory_requirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    VK_CHECK(vkAllocateMemory(device_, &depth_allocate_info, nullptr, &offscreen_depth_image_memory_));
    VK_CHECK(vkBindImageMemory(device_, offscreen_depth_image_, offscreen_depth_image_memory_, 0));

    VkImageViewCreateInfo depth_image_view_info{};
    depth_image_view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    depth_image_view_info.image = offscreen_depth_image_;
    depth_image_view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
    depth_image_view_info.format = offscreen_depth_format_;
    depth_image_view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    depth_image_view_info.subresourceRange.baseMipLevel = 0;
    depth_image_view_info.subresourceRange.levelCount = 1;
    depth_image_view_info.subresourceRange.baseArrayLayer = 0;
    depth_image_view_info.subresourceRange.layerCount = 1;
    VK_CHECK(vkCreateImageView(device_, &depth_image_view_info, nullptr, &offscreen_depth_image_view_));

    VkImageView attachments[] = {offscreen_color_image_view_};
    VkFramebufferCreateInfo framebuffer_info{};
    framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    framebuffer_info.renderPass = render_pass_;
    framebuffer_info.attachmentCount = 1;
    framebuffer_info.pAttachments = attachments;
    framebuffer_info.width = swap_chain_extent_.width;
    framebuffer_info.height = swap_chain_extent_.height;
    framebuffer_info.layers = 1;
    VK_CHECK(vkCreateFramebuffer(device_, &framebuffer_info, nullptr, &offscreen_framebuffer_));

    VkImageCreateInfo bloom_extract_image_info{};
    bloom_extract_image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    bloom_extract_image_info.imageType = VK_IMAGE_TYPE_2D;
    bloom_extract_image_info.extent.width = swap_chain_extent_.width;
    bloom_extract_image_info.extent.height = swap_chain_extent_.height;
    bloom_extract_image_info.extent.depth = 1;
    bloom_extract_image_info.mipLevels = 1;
    bloom_extract_image_info.arrayLayers = 1;
    bloom_extract_image_info.format = swap_chain_image_format_;
    bloom_extract_image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
    bloom_extract_image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    bloom_extract_image_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    bloom_extract_image_info.samples = VK_SAMPLE_COUNT_1_BIT;
    bloom_extract_image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateImage(device_, &bloom_extract_image_info, nullptr, &bloom_extract_image_));

    VkMemoryRequirements bloom_extract_memory_requirements{};
    vkGetImageMemoryRequirements(device_, bloom_extract_image_, &bloom_extract_memory_requirements);
    VkMemoryAllocateInfo bloom_extract_allocate_info{};
    bloom_extract_allocate_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    bloom_extract_allocate_info.allocationSize = bloom_extract_memory_requirements.size;
    bloom_extract_allocate_info.memoryTypeIndex = FindMemoryType(
        bloom_extract_memory_requirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    VK_CHECK(vkAllocateMemory(device_, &bloom_extract_allocate_info, nullptr, &bloom_extract_image_memory_));
    VK_CHECK(vkBindImageMemory(device_, bloom_extract_image_, bloom_extract_image_memory_, 0));

    VkImageViewCreateInfo bloom_extract_image_view_info{};
    bloom_extract_image_view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    bloom_extract_image_view_info.image = bloom_extract_image_;
    bloom_extract_image_view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
    bloom_extract_image_view_info.format = swap_chain_image_format_;
    bloom_extract_image_view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    bloom_extract_image_view_info.subresourceRange.baseMipLevel = 0;
    bloom_extract_image_view_info.subresourceRange.levelCount = 1;
    bloom_extract_image_view_info.subresourceRange.baseArrayLayer = 0;
    bloom_extract_image_view_info.subresourceRange.layerCount = 1;
    VK_CHECK(vkCreateImageView(device_, &bloom_extract_image_view_info, nullptr, &bloom_extract_image_view_));

    VkImageView bloom_extract_attachments[] = {bloom_extract_image_view_};
    VkFramebufferCreateInfo bloom_extract_framebuffer_info{};
    bloom_extract_framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    bloom_extract_framebuffer_info.renderPass = bloom_extract_render_pass_;
    bloom_extract_framebuffer_info.attachmentCount = 1;
    bloom_extract_framebuffer_info.pAttachments = bloom_extract_attachments;
    bloom_extract_framebuffer_info.width = swap_chain_extent_.width;
    bloom_extract_framebuffer_info.height = swap_chain_extent_.height;
    bloom_extract_framebuffer_info.layers = 1;
    VK_CHECK(vkCreateFramebuffer(device_, &bloom_extract_framebuffer_info, nullptr, &bloom_extract_framebuffer_));

    VkImageCreateInfo blur_ping_image_info{};
    blur_ping_image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    blur_ping_image_info.imageType = VK_IMAGE_TYPE_2D;
    blur_ping_image_info.extent.width = swap_chain_extent_.width;
    blur_ping_image_info.extent.height = swap_chain_extent_.height;
    blur_ping_image_info.extent.depth = 1;
    blur_ping_image_info.mipLevels = 1;
    blur_ping_image_info.arrayLayers = 1;
    blur_ping_image_info.format = swap_chain_image_format_;
    blur_ping_image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
    blur_ping_image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    blur_ping_image_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    blur_ping_image_info.samples = VK_SAMPLE_COUNT_1_BIT;
    blur_ping_image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateImage(device_, &blur_ping_image_info, nullptr, &blur_ping_image_));
    VkMemoryRequirements blur_ping_memory_requirements{};
    vkGetImageMemoryRequirements(device_, blur_ping_image_, &blur_ping_memory_requirements);
    VkMemoryAllocateInfo blur_ping_allocate_info{};
    blur_ping_allocate_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    blur_ping_allocate_info.allocationSize = blur_ping_memory_requirements.size;
    blur_ping_allocate_info.memoryTypeIndex = FindMemoryType(
        blur_ping_memory_requirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    VK_CHECK(vkAllocateMemory(device_, &blur_ping_allocate_info, nullptr, &blur_ping_image_memory_));
    VK_CHECK(vkBindImageMemory(device_, blur_ping_image_, blur_ping_image_memory_, 0));
    VkImageViewCreateInfo blur_ping_image_view_info{};
    blur_ping_image_view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    blur_ping_image_view_info.image = blur_ping_image_;
    blur_ping_image_view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
    blur_ping_image_view_info.format = swap_chain_image_format_;
    blur_ping_image_view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    blur_ping_image_view_info.subresourceRange.baseMipLevel = 0;
    blur_ping_image_view_info.subresourceRange.levelCount = 1;
    blur_ping_image_view_info.subresourceRange.baseArrayLayer = 0;
    blur_ping_image_view_info.subresourceRange.layerCount = 1;
    VK_CHECK(vkCreateImageView(device_, &blur_ping_image_view_info, nullptr, &blur_ping_image_view_));
    VkImageView blur_ping_attachments[] = {blur_ping_image_view_};
    VkFramebufferCreateInfo blur_ping_framebuffer_info{};
    blur_ping_framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    blur_ping_framebuffer_info.renderPass = blur_render_pass_;
    blur_ping_framebuffer_info.attachmentCount = 1;
    blur_ping_framebuffer_info.pAttachments = blur_ping_attachments;
    blur_ping_framebuffer_info.width = swap_chain_extent_.width;
    blur_ping_framebuffer_info.height = swap_chain_extent_.height;
    blur_ping_framebuffer_info.layers = 1;
    VK_CHECK(vkCreateFramebuffer(device_, &blur_ping_framebuffer_info, nullptr, &blur_ping_framebuffer_));

    VkImageCreateInfo combine_image_info{};
    combine_image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    combine_image_info.imageType = VK_IMAGE_TYPE_2D;
    combine_image_info.extent.width = swap_chain_extent_.width;
    combine_image_info.extent.height = swap_chain_extent_.height;
    combine_image_info.extent.depth = 1;
    combine_image_info.mipLevels = 1;
    combine_image_info.arrayLayers = 1;
    combine_image_info.format = swap_chain_image_format_;
    combine_image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
    combine_image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    combine_image_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    combine_image_info.samples = VK_SAMPLE_COUNT_1_BIT;
    combine_image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateImage(device_, &combine_image_info, nullptr, &combine_image_));
    VkMemoryRequirements combine_memory_requirements{};
    vkGetImageMemoryRequirements(device_, combine_image_, &combine_memory_requirements);
    VkMemoryAllocateInfo combine_allocate_info{};
    combine_allocate_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    combine_allocate_info.allocationSize = combine_memory_requirements.size;
    combine_allocate_info.memoryTypeIndex = FindMemoryType(
        combine_memory_requirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    VK_CHECK(vkAllocateMemory(device_, &combine_allocate_info, nullptr, &combine_image_memory_));
    VK_CHECK(vkBindImageMemory(device_, combine_image_, combine_image_memory_, 0));
    VkImageViewCreateInfo combine_image_view_info{};
    combine_image_view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    combine_image_view_info.image = combine_image_;
    combine_image_view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
    combine_image_view_info.format = swap_chain_image_format_;
    combine_image_view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    combine_image_view_info.subresourceRange.baseMipLevel = 0;
    combine_image_view_info.subresourceRange.levelCount = 1;
    combine_image_view_info.subresourceRange.baseArrayLayer = 0;
    combine_image_view_info.subresourceRange.layerCount = 1;
    VK_CHECK(vkCreateImageView(device_, &combine_image_view_info, nullptr, &combine_image_view_));
    VkImageView combine_attachments[] = {combine_image_view_};
    VkFramebufferCreateInfo combine_framebuffer_info{};
    combine_framebuffer_info.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    combine_framebuffer_info.renderPass = combine_render_pass_;
    combine_framebuffer_info.attachmentCount = 1;
    combine_framebuffer_info.pAttachments = combine_attachments;
    combine_framebuffer_info.width = swap_chain_extent_.width;
    combine_framebuffer_info.height = swap_chain_extent_.height;
    combine_framebuffer_info.layers = 1;
    VK_CHECK(vkCreateFramebuffer(device_, &combine_framebuffer_info, nullptr, &combine_framebuffer_));

    VkSamplerCreateInfo offscreen_color_sampler_info{};
    offscreen_color_sampler_info.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    offscreen_color_sampler_info.magFilter = VK_FILTER_LINEAR;
    offscreen_color_sampler_info.minFilter = VK_FILTER_LINEAR;
    offscreen_color_sampler_info.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
    offscreen_color_sampler_info.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    offscreen_color_sampler_info.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    offscreen_color_sampler_info.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    offscreen_color_sampler_info.mipLodBias = 0.0F;
    offscreen_color_sampler_info.anisotropyEnable = VK_FALSE;
    offscreen_color_sampler_info.maxAnisotropy = 1.0F;
    offscreen_color_sampler_info.compareEnable = VK_FALSE;
    offscreen_color_sampler_info.compareOp = VK_COMPARE_OP_ALWAYS;
    offscreen_color_sampler_info.minLod = 0.0F;
    offscreen_color_sampler_info.maxLod = 0.0F;
    offscreen_color_sampler_info.borderColor = VK_BORDER_COLOR_INT_OPAQUE_BLACK;
    offscreen_color_sampler_info.unnormalizedCoordinates = VK_FALSE;
    VK_CHECK(vkCreateSampler(device_, &offscreen_color_sampler_info, nullptr, &offscreen_color_sampler_));

    VkDescriptorPoolSize post_process_descriptor_pool_size{};
    post_process_descriptor_pool_size.type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    post_process_descriptor_pool_size.descriptorCount = 1;
    VkDescriptorPoolCreateInfo post_process_descriptor_pool_info{};
    post_process_descriptor_pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    post_process_descriptor_pool_info.maxSets = 1;
    post_process_descriptor_pool_info.poolSizeCount = 1;
    post_process_descriptor_pool_info.pPoolSizes = &post_process_descriptor_pool_size;
    VK_CHECK(vkCreateDescriptorPool(device_, &post_process_descriptor_pool_info, nullptr, &post_process_descriptor_pool_));

    VkDescriptorSetAllocateInfo post_process_descriptor_set_allocate_info{};
    post_process_descriptor_set_allocate_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    post_process_descriptor_set_allocate_info.descriptorPool = post_process_descriptor_pool_;
    post_process_descriptor_set_allocate_info.descriptorSetCount = 1;
    post_process_descriptor_set_allocate_info.pSetLayouts = &post_process_descriptor_set_layout_;
    VK_CHECK(vkAllocateDescriptorSets(device_, &post_process_descriptor_set_allocate_info, &post_process_descriptor_set_));

    VkDescriptorImageInfo offscreen_color_descriptor_image_info{};
    offscreen_color_descriptor_image_info.sampler = offscreen_color_sampler_;
    offscreen_color_descriptor_image_info.imageView = offscreen_color_image_view_;
    offscreen_color_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    VkWriteDescriptorSet post_process_descriptor_write{};
    post_process_descriptor_write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    post_process_descriptor_write.dstSet = post_process_descriptor_set_;
    post_process_descriptor_write.dstBinding = 0;
    post_process_descriptor_write.dstArrayElement = 0;
    post_process_descriptor_write.descriptorCount = 1;
    post_process_descriptor_write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    post_process_descriptor_write.pImageInfo = &offscreen_color_descriptor_image_info;
    vkUpdateDescriptorSets(device_, 1, &post_process_descriptor_write, 0, nullptr);

    std::array<VkDescriptorPoolSize, 2> combine_descriptor_pool_sizes{};
    combine_descriptor_pool_sizes[0].type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_descriptor_pool_sizes[0].descriptorCount = 1;
    combine_descriptor_pool_sizes[1].type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_descriptor_pool_sizes[1].descriptorCount = 1;
    VkDescriptorPoolCreateInfo combine_descriptor_pool_info{};
    combine_descriptor_pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    combine_descriptor_pool_info.maxSets = 1;
    combine_descriptor_pool_info.poolSizeCount = static_cast<std::uint32_t>(combine_descriptor_pool_sizes.size());
    combine_descriptor_pool_info.pPoolSizes = combine_descriptor_pool_sizes.data();
    VK_CHECK(vkCreateDescriptorPool(device_, &combine_descriptor_pool_info, nullptr, &combine_descriptor_pool_));
    VkDescriptorSetAllocateInfo combine_descriptor_set_allocate_info{};
    combine_descriptor_set_allocate_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    combine_descriptor_set_allocate_info.descriptorPool = combine_descriptor_pool_;
    combine_descriptor_set_allocate_info.descriptorSetCount = 1;
    combine_descriptor_set_allocate_info.pSetLayouts = &combine_descriptor_set_layout_;
    VK_CHECK(vkAllocateDescriptorSets(device_, &combine_descriptor_set_allocate_info, &combine_descriptor_set_));
    VkDescriptorImageInfo combine_scene_descriptor_image_info{};
    combine_scene_descriptor_image_info.sampler = offscreen_color_sampler_;
    combine_scene_descriptor_image_info.imageView = offscreen_color_image_view_;
    combine_scene_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    VkDescriptorImageInfo combine_bloom_descriptor_image_info{};
    combine_bloom_descriptor_image_info.sampler = offscreen_color_sampler_;
    combine_bloom_descriptor_image_info.imageView = bloom_extract_image_view_;
    combine_bloom_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    std::array<VkWriteDescriptorSet, 2> combine_descriptor_writes{};
    combine_descriptor_writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    combine_descriptor_writes[0].dstSet = combine_descriptor_set_;
    combine_descriptor_writes[0].dstBinding = 0;
    combine_descriptor_writes[0].dstArrayElement = 0;
    combine_descriptor_writes[0].descriptorCount = 1;
    combine_descriptor_writes[0].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_descriptor_writes[0].pImageInfo = &combine_scene_descriptor_image_info;
    combine_descriptor_writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    combine_descriptor_writes[1].dstSet = combine_descriptor_set_;
    combine_descriptor_writes[1].dstBinding = 1;
    combine_descriptor_writes[1].dstArrayElement = 0;
    combine_descriptor_writes[1].descriptorCount = 1;
    combine_descriptor_writes[1].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    combine_descriptor_writes[1].pImageInfo = &combine_bloom_descriptor_image_info;
    vkUpdateDescriptorSets(
        device_,
        static_cast<std::uint32_t>(combine_descriptor_writes.size()),
        combine_descriptor_writes.data(),
        0,
        nullptr);
}

void VulkanRenderer::DestroyOffscreenResources() {
    if (combine_descriptor_pool_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorPool(device_, combine_descriptor_pool_, nullptr);
        combine_descriptor_pool_ = VK_NULL_HANDLE;
        combine_descriptor_set_ = VK_NULL_HANDLE;
    }
    if (post_process_descriptor_pool_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorPool(device_, post_process_descriptor_pool_, nullptr);
        post_process_descriptor_pool_ = VK_NULL_HANDLE;
        post_process_descriptor_set_ = VK_NULL_HANDLE;
    }
    if (offscreen_color_sampler_ != VK_NULL_HANDLE) {
        vkDestroySampler(device_, offscreen_color_sampler_, nullptr);
        offscreen_color_sampler_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_framebuffer_ != VK_NULL_HANDLE) {
        vkDestroyFramebuffer(device_, bloom_extract_framebuffer_, nullptr);
        bloom_extract_framebuffer_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_image_view_ != VK_NULL_HANDLE) {
        vkDestroyImageView(device_, bloom_extract_image_view_, nullptr);
        bloom_extract_image_view_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_image_ != VK_NULL_HANDLE) {
        vkDestroyImage(device_, bloom_extract_image_, nullptr);
        bloom_extract_image_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_image_memory_ != VK_NULL_HANDLE) {
        vkFreeMemory(device_, bloom_extract_image_memory_, nullptr);
        bloom_extract_image_memory_ = VK_NULL_HANDLE;
    }
    if (blur_ping_framebuffer_ != VK_NULL_HANDLE) {
        vkDestroyFramebuffer(device_, blur_ping_framebuffer_, nullptr);
        blur_ping_framebuffer_ = VK_NULL_HANDLE;
    }
    if (blur_ping_image_view_ != VK_NULL_HANDLE) {
        vkDestroyImageView(device_, blur_ping_image_view_, nullptr);
        blur_ping_image_view_ = VK_NULL_HANDLE;
    }
    if (blur_ping_image_ != VK_NULL_HANDLE) {
        vkDestroyImage(device_, blur_ping_image_, nullptr);
        blur_ping_image_ = VK_NULL_HANDLE;
    }
    if (blur_ping_image_memory_ != VK_NULL_HANDLE) {
        vkFreeMemory(device_, blur_ping_image_memory_, nullptr);
        blur_ping_image_memory_ = VK_NULL_HANDLE;
    }
    if (combine_framebuffer_ != VK_NULL_HANDLE) {
        vkDestroyFramebuffer(device_, combine_framebuffer_, nullptr);
        combine_framebuffer_ = VK_NULL_HANDLE;
    }
    if (combine_image_view_ != VK_NULL_HANDLE) {
        vkDestroyImageView(device_, combine_image_view_, nullptr);
        combine_image_view_ = VK_NULL_HANDLE;
    }
    if (combine_image_ != VK_NULL_HANDLE) {
        vkDestroyImage(device_, combine_image_, nullptr);
        combine_image_ = VK_NULL_HANDLE;
    }
    if (combine_image_memory_ != VK_NULL_HANDLE) {
        vkFreeMemory(device_, combine_image_memory_, nullptr);
        combine_image_memory_ = VK_NULL_HANDLE;
    }
    if (offscreen_framebuffer_ != VK_NULL_HANDLE) {
        vkDestroyFramebuffer(device_, offscreen_framebuffer_, nullptr);
        offscreen_framebuffer_ = VK_NULL_HANDLE;
    }
    if (offscreen_depth_image_view_ != VK_NULL_HANDLE) {
        vkDestroyImageView(device_, offscreen_depth_image_view_, nullptr);
        offscreen_depth_image_view_ = VK_NULL_HANDLE;
    }
    if (offscreen_depth_image_ != VK_NULL_HANDLE) {
        vkDestroyImage(device_, offscreen_depth_image_, nullptr);
        offscreen_depth_image_ = VK_NULL_HANDLE;
    }
    if (offscreen_depth_image_memory_ != VK_NULL_HANDLE) {
        vkFreeMemory(device_, offscreen_depth_image_memory_, nullptr);
        offscreen_depth_image_memory_ = VK_NULL_HANDLE;
    }
    if (offscreen_color_image_view_ != VK_NULL_HANDLE) {
        vkDestroyImageView(device_, offscreen_color_image_view_, nullptr);
        offscreen_color_image_view_ = VK_NULL_HANDLE;
    }
    if (offscreen_color_image_ != VK_NULL_HANDLE) {
        vkDestroyImage(device_, offscreen_color_image_, nullptr);
        offscreen_color_image_ = VK_NULL_HANDLE;
    }
    if (offscreen_color_image_memory_ != VK_NULL_HANDLE) {
        vkFreeMemory(device_, offscreen_color_image_memory_, nullptr);
        offscreen_color_image_memory_ = VK_NULL_HANDLE;
    }
    offscreen_depth_format_ = VK_FORMAT_UNDEFINED;
}

void VulkanRenderer::CreateCommandPool() {
    const QueueFamilyIndices queue_family_indices = FindQueueFamilies(physical_device_);

    VkCommandPoolCreateInfo pool_info{};
    pool_info.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    pool_info.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    pool_info.queueFamilyIndex = queue_family_indices.graphics_family.value();

    VK_CHECK(vkCreateCommandPool(device_, &pool_info, nullptr, &command_pool_));
}

void VulkanRenderer::CreateCommandBuffers() {
    command_buffers_.resize(swap_chain_framebuffers_.size());

    VkCommandBufferAllocateInfo alloc_info{};
    alloc_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    alloc_info.commandPool = command_pool_;
    alloc_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    alloc_info.commandBufferCount = static_cast<std::uint32_t>(command_buffers_.size());

    VK_CHECK(vkAllocateCommandBuffers(device_, &alloc_info, command_buffers_.data()));
}

void VulkanRenderer::CreateSyncObjects() {
    image_available_semaphores_.resize(kMaxFramesInFlight);
    render_finished_semaphores_.resize(kMaxFramesInFlight);
    in_flight_fences_.resize(kMaxFramesInFlight);

    VkSemaphoreCreateInfo semaphore_info{};
    semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

    VkFenceCreateInfo fence_info{};
    fence_info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fence_info.flags = VK_FENCE_CREATE_SIGNALED_BIT;

    for (int i = 0; i < kMaxFramesInFlight; ++i) {
        VK_CHECK(vkCreateSemaphore(device_, &semaphore_info, nullptr, &image_available_semaphores_[i]));
        VK_CHECK(vkCreateSemaphore(device_, &semaphore_info, nullptr, &render_finished_semaphores_[i]));
        VK_CHECK(vkCreateFence(device_, &fence_info, nullptr, &in_flight_fences_[i]));
    }
}

void VulkanRenderer::RecordCommandBuffer(std::uint32_t image_index, const Scene& scene, const Camera& camera) {
    VK_CHECK(vkResetCommandBuffer(command_buffers_[image_index], 0));

    VkCommandBufferBeginInfo begin_info{};
    begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    VK_CHECK(vkBeginCommandBuffer(command_buffers_[image_index], &begin_info));

    post_process_enabled_ = scene.post_processing.enabled;

    if (post_process_enabled_) {
        VkImageMemoryBarrier offscreen_color_to_attachment{};
        offscreen_color_to_attachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_color_to_attachment.srcAccessMask = 0;
        offscreen_color_to_attachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        offscreen_color_to_attachment.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        offscreen_color_to_attachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        offscreen_color_to_attachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_attachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_attachment.image = offscreen_color_image_;
        offscreen_color_to_attachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_color_to_attachment.subresourceRange.baseMipLevel = 0;
        offscreen_color_to_attachment.subresourceRange.levelCount = 1;
        offscreen_color_to_attachment.subresourceRange.baseArrayLayer = 0;
        offscreen_color_to_attachment.subresourceRange.layerCount = 1;

        VkImageMemoryBarrier offscreen_depth_to_attachment{};
        offscreen_depth_to_attachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_depth_to_attachment.srcAccessMask = 0;
        offscreen_depth_to_attachment.dstAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        offscreen_depth_to_attachment.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        offscreen_depth_to_attachment.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
        offscreen_depth_to_attachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_depth_to_attachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_depth_to_attachment.image = offscreen_depth_image_;
        offscreen_depth_to_attachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
        offscreen_depth_to_attachment.subresourceRange.baseMipLevel = 0;
        offscreen_depth_to_attachment.subresourceRange.levelCount = 1;
        offscreen_depth_to_attachment.subresourceRange.baseArrayLayer = 0;
        offscreen_depth_to_attachment.subresourceRange.layerCount = 1;

        std::array<VkImageMemoryBarrier, 2> pre_render_barriers = {
            offscreen_color_to_attachment,
            offscreen_depth_to_attachment,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(pre_render_barriers.size()),
            pre_render_barriers.data());
    }

    VkRenderPassBeginInfo render_pass_begin_info{};
    render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    render_pass_begin_info.renderPass = render_pass_;
    render_pass_begin_info.framebuffer = post_process_enabled_ ? offscreen_framebuffer_ : swap_chain_framebuffers_[image_index];
    render_pass_begin_info.renderArea.offset = {0, 0};
    render_pass_begin_info.renderArea.extent = swap_chain_extent_;

    const glm::vec3 normalized_sky = glm::clamp(scene.directional_light.color, glm::vec3(0.0F), glm::vec3(1.0F));
    VkClearValue clear_color = {{{normalized_sky.r, normalized_sky.g, normalized_sky.b, 1.0F}}};
    render_pass_begin_info.clearValueCount = 1;
    render_pass_begin_info.pClearValues = &clear_color;

    vkCmdBeginRenderPass(command_buffers_[image_index], &render_pass_begin_info, VK_SUBPASS_CONTENTS_INLINE);
    vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, graphics_pipeline_);
    if (bindless_texture_descriptor_set_ != VK_NULL_HANDLE) {
        vkCmdBindDescriptorSets(
            command_buffers_[image_index],
            VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline_layout_,
            2,
            1,
            &bindless_texture_descriptor_set_,
            0,
            nullptr);
    }

    const glm::mat4 view_proj = camera.GetProjectionMatrix() * camera.GetViewMatrix();
    PerFramePushConstants per_frame_push{};
    per_frame_push.view_proj = view_proj;
    per_frame_push.light_dir = glm::vec4(glm::normalize(scene.directional_light.direction), 0.0F);
    const glm::vec3 light_color = scene.directional_light.color * scene.directional_light.intensity;
    per_frame_push.light_color = glm::vec4(light_color, 1.0F);

    vkCmdPushConstants(
        command_buffers_[image_index],
        pipeline_layout_,
        VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
        0,
        sizeof(PerFramePushConstants),
        &per_frame_push);

    if (scene.render_2d.render_mode == "3D") {
        Render3DLayer(image_index, scene, camera);
    }
    if (scene.build_ghost_preview.has_value()) {
        const Entity& ghost = scene.build_ghost_preview.value();
        glm::mat4 model(1.0F);
        model = glm::translate(model, ghost.transform.pos);
        model = glm::rotate(model, ghost.transform.rot.x, glm::vec3(1.0F, 0.0F, 0.0F));
        model = glm::rotate(model, ghost.transform.rot.y, glm::vec3(0.0F, 1.0F, 0.0F));
        model = glm::rotate(model, ghost.transform.rot.z, glm::vec3(0.0F, 0.0F, 1.0F));
        model = glm::scale(model, ghost.transform.scale);

        PerDrawPushConstants per_draw_push{};
        per_draw_push.model = model;
        per_draw_push.color = ghost.renderable.color;

        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &per_draw_push);
        TexturePushConstants texture_push{};
        texture_push.texture_index = 0;
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(TexturePushConstants),
            &texture_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
    }
    if (scene.render_2d.render_mode != "3D") {
        for (const Entity& entity : scene.entities) {
            glm::mat4 model(1.0F);
            model = glm::translate(model, entity.transform.pos);
            model = glm::rotate(model, entity.transform.rot.x, glm::vec3(1.0F, 0.0F, 0.0F));
            model = glm::rotate(model, entity.transform.rot.y, glm::vec3(0.0F, 1.0F, 0.0F));
            model = glm::rotate(model, entity.transform.rot.z, glm::vec3(0.0F, 0.0F, 1.0F));
            model = glm::scale(model, entity.transform.scale);

            PerDrawPushConstants per_draw_push{};
            per_draw_push.model = model;
            per_draw_push.color = entity.renderable.color;

            vkCmdPushConstants(
                command_buffers_[image_index],
                pipeline_layout_,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                sizeof(PerFramePushConstants),
                sizeof(PerDrawPushConstants),
                &per_draw_push);
            TexturePushConstants texture_push{};
            texture_push.texture_index = 0;
            vkCmdPushConstants(
                command_buffers_[image_index],
                pipeline_layout_,
                VK_SHADER_STAGE_FRAGMENT_BIT,
                sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
                sizeof(TexturePushConstants),
                &texture_push);
            vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        }
        Render2DLayer(image_index, scene);
    }

    vkCmdEndRenderPass(command_buffers_[image_index]);

    if (post_process_enabled_) {
        VkImageMemoryBarrier offscreen_color_to_shader_read_for_bloom{};
        offscreen_color_to_shader_read_for_bloom.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_color_to_shader_read_for_bloom.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        offscreen_color_to_shader_read_for_bloom.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        offscreen_color_to_shader_read_for_bloom.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        offscreen_color_to_shader_read_for_bloom.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        offscreen_color_to_shader_read_for_bloom.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_shader_read_for_bloom.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_shader_read_for_bloom.image = offscreen_color_image_;
        offscreen_color_to_shader_read_for_bloom.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_color_to_shader_read_for_bloom.subresourceRange.baseMipLevel = 0;
        offscreen_color_to_shader_read_for_bloom.subresourceRange.levelCount = 1;
        offscreen_color_to_shader_read_for_bloom.subresourceRange.baseArrayLayer = 0;
        offscreen_color_to_shader_read_for_bloom.subresourceRange.layerCount = 1;

        VkImageMemoryBarrier bloom_extract_to_attachment{};
        bloom_extract_to_attachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        bloom_extract_to_attachment.srcAccessMask = 0;
        bloom_extract_to_attachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        bloom_extract_to_attachment.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        bloom_extract_to_attachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        bloom_extract_to_attachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_attachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_attachment.image = bloom_extract_image_;
        bloom_extract_to_attachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        bloom_extract_to_attachment.subresourceRange.baseMipLevel = 0;
        bloom_extract_to_attachment.subresourceRange.levelCount = 1;
        bloom_extract_to_attachment.subresourceRange.baseArrayLayer = 0;
        bloom_extract_to_attachment.subresourceRange.layerCount = 1;
        std::array<VkImageMemoryBarrier, 2> bloom_pre_pass_barriers = {
            offscreen_color_to_shader_read_for_bloom,
            bloom_extract_to_attachment,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(bloom_pre_pass_barriers.size()),
            bloom_pre_pass_barriers.data());

        VkRenderPassBeginInfo bloom_extract_render_pass_begin_info{};
        bloom_extract_render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        bloom_extract_render_pass_begin_info.renderPass = bloom_extract_render_pass_;
        bloom_extract_render_pass_begin_info.framebuffer = bloom_extract_framebuffer_;
        bloom_extract_render_pass_begin_info.renderArea.offset = {0, 0};
        bloom_extract_render_pass_begin_info.renderArea.extent = swap_chain_extent_;
        VkClearValue bloom_extract_clear_color = {{{0.0F, 0.0F, 0.0F, 1.0F}}};
        bloom_extract_render_pass_begin_info.clearValueCount = 1;
        bloom_extract_render_pass_begin_info.pClearValues = &bloom_extract_clear_color;
        vkCmdBeginRenderPass(
            command_buffers_[image_index],
            &bloom_extract_render_pass_begin_info,
            VK_SUBPASS_CONTENTS_INLINE);
        vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, bloom_extract_pipeline_);
        vkCmdBindDescriptorSets(
            command_buffers_[image_index],
            VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline_layout_,
            0,
            1,
            &post_process_descriptor_set_,
            0,
            nullptr);
        BloomExtractPushConstants bloom_extract_push_constants{};
        const float bloom_threshold = scene.post_processing.bloom_enabled ? 1.0F : 16.0F;
        bloom_extract_push_constants.threshold = glm::vec4(bloom_threshold, scene.post_processing.outline_enabled ? scene.post_processing.outline_strength : 0.0F, 0.0F, 0.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(BloomExtractPushConstants),
            &bloom_extract_push_constants);
        PerFramePushConstants bloom_per_frame_push{};
        bloom_per_frame_push.view_proj = glm::mat4(1.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(PerFramePushConstants),
            &bloom_per_frame_push);
        PerDrawPushConstants bloom_per_draw_push{};
        bloom_per_draw_push.model = glm::scale(glm::mat4(1.0F), glm::vec3(2.0F, 2.0F, 1.0F));
        bloom_per_draw_push.color = glm::vec4(1.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &bloom_per_draw_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        vkCmdEndRenderPass(command_buffers_[image_index]);

        VkImageMemoryBarrier bloom_extract_to_shader_read{};
        bloom_extract_to_shader_read.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        bloom_extract_to_shader_read.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        bloom_extract_to_shader_read.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        bloom_extract_to_shader_read.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        bloom_extract_to_shader_read.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        bloom_extract_to_shader_read.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_shader_read.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_shader_read.image = bloom_extract_image_;
        bloom_extract_to_shader_read.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        bloom_extract_to_shader_read.subresourceRange.baseMipLevel = 0;
        bloom_extract_to_shader_read.subresourceRange.levelCount = 1;
        bloom_extract_to_shader_read.subresourceRange.baseArrayLayer = 0;
        bloom_extract_to_shader_read.subresourceRange.layerCount = 1;
        VkImageMemoryBarrier blur_ping_to_attachment{};
        blur_ping_to_attachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        blur_ping_to_attachment.srcAccessMask = 0;
        blur_ping_to_attachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        blur_ping_to_attachment.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        blur_ping_to_attachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        blur_ping_to_attachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        blur_ping_to_attachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        blur_ping_to_attachment.image = blur_ping_image_;
        blur_ping_to_attachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        blur_ping_to_attachment.subresourceRange.baseMipLevel = 0;
        blur_ping_to_attachment.subresourceRange.levelCount = 1;
        blur_ping_to_attachment.subresourceRange.baseArrayLayer = 0;
        blur_ping_to_attachment.subresourceRange.layerCount = 1;
        std::array<VkImageMemoryBarrier, 2> blur_horizontal_pre_barriers = {
            bloom_extract_to_shader_read,
            blur_ping_to_attachment,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(blur_horizontal_pre_barriers.size()),
            blur_horizontal_pre_barriers.data());
        VkDescriptorImageInfo blur_horizontal_descriptor_image_info{};
        blur_horizontal_descriptor_image_info.sampler = offscreen_color_sampler_;
        blur_horizontal_descriptor_image_info.imageView = bloom_extract_image_view_;
        blur_horizontal_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkWriteDescriptorSet blur_horizontal_descriptor_write{};
        blur_horizontal_descriptor_write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        blur_horizontal_descriptor_write.dstSet = post_process_descriptor_set_;
        blur_horizontal_descriptor_write.dstBinding = 0;
        blur_horizontal_descriptor_write.dstArrayElement = 0;
        blur_horizontal_descriptor_write.descriptorCount = 1;
        blur_horizontal_descriptor_write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        blur_horizontal_descriptor_write.pImageInfo = &blur_horizontal_descriptor_image_info;
        vkUpdateDescriptorSets(device_, 1, &blur_horizontal_descriptor_write, 0, nullptr);
        VkRenderPassBeginInfo blur_horizontal_render_pass_begin_info{};
        blur_horizontal_render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        blur_horizontal_render_pass_begin_info.renderPass = blur_render_pass_;
        blur_horizontal_render_pass_begin_info.framebuffer = blur_ping_framebuffer_;
        blur_horizontal_render_pass_begin_info.renderArea.offset = {0, 0};
        blur_horizontal_render_pass_begin_info.renderArea.extent = swap_chain_extent_;
        VkClearValue blur_horizontal_clear_color = {{{0.0F, 0.0F, 0.0F, 1.0F}}};
        blur_horizontal_render_pass_begin_info.clearValueCount = 1;
        blur_horizontal_render_pass_begin_info.pClearValues = &blur_horizontal_clear_color;
        vkCmdBeginRenderPass(
            command_buffers_[image_index],
            &blur_horizontal_render_pass_begin_info,
            VK_SUBPASS_CONTENTS_INLINE);
        vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, blur_pipeline_);
        vkCmdBindDescriptorSets(
            command_buffers_[image_index],
            VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline_layout_,
            0,
            1,
            &post_process_descriptor_set_,
            0,
            nullptr);
        GaussianBlurPushConstants blur_horizontal_push_constants{};
        blur_horizontal_push_constants.direction = glm::vec4(1.0F / static_cast<float>(swap_chain_extent_.width), 0.0F, 0.0F, 0.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(GaussianBlurPushConstants),
            &blur_horizontal_push_constants);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(PerFramePushConstants),
            &bloom_per_frame_push);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &bloom_per_draw_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        vkCmdEndRenderPass(command_buffers_[image_index]);
        VkImageMemoryBarrier blur_ping_to_shader_read{};
        blur_ping_to_shader_read.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        blur_ping_to_shader_read.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        blur_ping_to_shader_read.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        blur_ping_to_shader_read.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        blur_ping_to_shader_read.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        blur_ping_to_shader_read.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        blur_ping_to_shader_read.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        blur_ping_to_shader_read.image = blur_ping_image_;
        blur_ping_to_shader_read.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        blur_ping_to_shader_read.subresourceRange.baseMipLevel = 0;
        blur_ping_to_shader_read.subresourceRange.levelCount = 1;
        blur_ping_to_shader_read.subresourceRange.baseArrayLayer = 0;
        blur_ping_to_shader_read.subresourceRange.layerCount = 1;
        VkImageMemoryBarrier bloom_extract_to_attachment_for_vertical{};
        bloom_extract_to_attachment_for_vertical.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        bloom_extract_to_attachment_for_vertical.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
        bloom_extract_to_attachment_for_vertical.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        bloom_extract_to_attachment_for_vertical.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        bloom_extract_to_attachment_for_vertical.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        bloom_extract_to_attachment_for_vertical.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_attachment_for_vertical.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_extract_to_attachment_for_vertical.image = bloom_extract_image_;
        bloom_extract_to_attachment_for_vertical.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        bloom_extract_to_attachment_for_vertical.subresourceRange.baseMipLevel = 0;
        bloom_extract_to_attachment_for_vertical.subresourceRange.levelCount = 1;
        bloom_extract_to_attachment_for_vertical.subresourceRange.baseArrayLayer = 0;
        bloom_extract_to_attachment_for_vertical.subresourceRange.layerCount = 1;
        std::array<VkImageMemoryBarrier, 2> blur_vertical_pre_barriers = {
            blur_ping_to_shader_read,
            bloom_extract_to_attachment_for_vertical,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(blur_vertical_pre_barriers.size()),
            blur_vertical_pre_barriers.data());
        VkDescriptorImageInfo blur_vertical_descriptor_image_info{};
        blur_vertical_descriptor_image_info.sampler = offscreen_color_sampler_;
        blur_vertical_descriptor_image_info.imageView = blur_ping_image_view_;
        blur_vertical_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkWriteDescriptorSet blur_vertical_descriptor_write{};
        blur_vertical_descriptor_write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        blur_vertical_descriptor_write.dstSet = post_process_descriptor_set_;
        blur_vertical_descriptor_write.dstBinding = 0;
        blur_vertical_descriptor_write.dstArrayElement = 0;
        blur_vertical_descriptor_write.descriptorCount = 1;
        blur_vertical_descriptor_write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        blur_vertical_descriptor_write.pImageInfo = &blur_vertical_descriptor_image_info;
        vkUpdateDescriptorSets(device_, 1, &blur_vertical_descriptor_write, 0, nullptr);
        VkRenderPassBeginInfo blur_vertical_render_pass_begin_info{};
        blur_vertical_render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        blur_vertical_render_pass_begin_info.renderPass = blur_render_pass_;
        blur_vertical_render_pass_begin_info.framebuffer = bloom_extract_framebuffer_;
        blur_vertical_render_pass_begin_info.renderArea.offset = {0, 0};
        blur_vertical_render_pass_begin_info.renderArea.extent = swap_chain_extent_;
        VkClearValue blur_vertical_clear_color = {{{0.0F, 0.0F, 0.0F, 1.0F}}};
        blur_vertical_render_pass_begin_info.clearValueCount = 1;
        blur_vertical_render_pass_begin_info.pClearValues = &blur_vertical_clear_color;
        vkCmdBeginRenderPass(
            command_buffers_[image_index],
            &blur_vertical_render_pass_begin_info,
            VK_SUBPASS_CONTENTS_INLINE);
        vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, blur_pipeline_);
        vkCmdBindDescriptorSets(
            command_buffers_[image_index],
            VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline_layout_,
            0,
            1,
            &post_process_descriptor_set_,
            0,
            nullptr);
        GaussianBlurPushConstants blur_vertical_push_constants{};
        blur_vertical_push_constants.direction = glm::vec4(0.0F, 1.0F / static_cast<float>(swap_chain_extent_.height), 0.0F, 0.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(GaussianBlurPushConstants),
            &blur_vertical_push_constants);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(PerFramePushConstants),
            &bloom_per_frame_push);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &bloom_per_draw_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        vkCmdEndRenderPass(command_buffers_[image_index]);
        VkImageMemoryBarrier bloom_blur_to_shader_read{};
        bloom_blur_to_shader_read.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        bloom_blur_to_shader_read.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        bloom_blur_to_shader_read.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        bloom_blur_to_shader_read.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        bloom_blur_to_shader_read.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        bloom_blur_to_shader_read.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_blur_to_shader_read.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bloom_blur_to_shader_read.image = bloom_extract_image_;
        bloom_blur_to_shader_read.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        bloom_blur_to_shader_read.subresourceRange.baseMipLevel = 0;
        bloom_blur_to_shader_read.subresourceRange.levelCount = 1;
        bloom_blur_to_shader_read.subresourceRange.baseArrayLayer = 0;
        bloom_blur_to_shader_read.subresourceRange.layerCount = 1;
        VkDescriptorImageInfo reset_post_process_descriptor_image_info{};
        reset_post_process_descriptor_image_info.sampler = offscreen_color_sampler_;
        reset_post_process_descriptor_image_info.imageView = offscreen_color_image_view_;
        reset_post_process_descriptor_image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkWriteDescriptorSet reset_post_process_descriptor_write{};
        reset_post_process_descriptor_write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        reset_post_process_descriptor_write.dstSet = post_process_descriptor_set_;
        reset_post_process_descriptor_write.dstBinding = 0;
        reset_post_process_descriptor_write.dstArrayElement = 0;
        reset_post_process_descriptor_write.descriptorCount = 1;
        reset_post_process_descriptor_write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        reset_post_process_descriptor_write.pImageInfo = &reset_post_process_descriptor_image_info;
        vkUpdateDescriptorSets(device_, 1, &reset_post_process_descriptor_write, 0, nullptr);
        VkImageMemoryBarrier combine_to_attachment{};
        combine_to_attachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        combine_to_attachment.srcAccessMask = 0;
        combine_to_attachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        combine_to_attachment.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        combine_to_attachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        combine_to_attachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        combine_to_attachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        combine_to_attachment.image = combine_image_;
        combine_to_attachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        combine_to_attachment.subresourceRange.baseMipLevel = 0;
        combine_to_attachment.subresourceRange.levelCount = 1;
        combine_to_attachment.subresourceRange.baseArrayLayer = 0;
        combine_to_attachment.subresourceRange.layerCount = 1;
        std::array<VkImageMemoryBarrier, 2> combine_pre_pass_barriers = {
            bloom_blur_to_shader_read,
            combine_to_attachment,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(combine_pre_pass_barriers.size()),
            combine_pre_pass_barriers.data());
        VkRenderPassBeginInfo combine_render_pass_begin_info{};
        combine_render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        combine_render_pass_begin_info.renderPass = combine_render_pass_;
        combine_render_pass_begin_info.framebuffer = combine_framebuffer_;
        combine_render_pass_begin_info.renderArea.offset = {0, 0};
        combine_render_pass_begin_info.renderArea.extent = swap_chain_extent_;
        VkClearValue combine_clear_color = {{{0.0F, 0.0F, 0.0F, 1.0F}}};
        combine_render_pass_begin_info.clearValueCount = 1;
        combine_render_pass_begin_info.pClearValues = &combine_clear_color;
        vkCmdBeginRenderPass(
            command_buffers_[image_index],
            &combine_render_pass_begin_info,
            VK_SUBPASS_CONTENTS_INLINE);
        vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, combine_pipeline_);
        vkCmdBindDescriptorSets(
            command_buffers_[image_index],
            VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline_layout_,
            1,
            1,
            &combine_descriptor_set_,
            0,
            nullptr);
        CombineTonemapPushConstants combine_push_constants{};
        const float bloom_strength = scene.post_processing.bloom_enabled ? scene.post_processing.bloom_strength : 0.0F;
        const float vignette_strength = scene.post_processing.vignette_enabled ? scene.post_processing.vignette_strength : 0.0F;
        const float saturation = scene.post_processing.color_grading_enabled ? scene.post_processing.color_grade_saturation : 1.0F;
        const float exposure = scene.post_processing.color_grading_enabled ? scene.post_processing.color_grade_contrast : 1.0F;
        combine_push_constants.params = glm::vec4(bloom_strength, exposure, vignette_strength, saturation);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants) + sizeof(BloomExtractPushConstants),
            sizeof(CombineTonemapPushConstants),
            &combine_push_constants);
        PerFramePushConstants combine_per_frame_push{};
        combine_per_frame_push.view_proj = glm::mat4(1.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(PerFramePushConstants),
            &combine_per_frame_push);
        PerDrawPushConstants combine_per_draw_push{};
        combine_per_draw_push.model = glm::scale(glm::mat4(1.0F), glm::vec3(2.0F, 2.0F, 1.0F));
        combine_per_draw_push.color = glm::vec4(1.0F);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &combine_per_draw_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        vkCmdEndRenderPass(command_buffers_[image_index]);

        VkImageMemoryBarrier combine_to_transfer_src{};
        combine_to_transfer_src.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        combine_to_transfer_src.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        combine_to_transfer_src.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
        combine_to_transfer_src.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        combine_to_transfer_src.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        combine_to_transfer_src.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        combine_to_transfer_src.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        combine_to_transfer_src.image = combine_image_;
        combine_to_transfer_src.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        combine_to_transfer_src.subresourceRange.baseMipLevel = 0;
        combine_to_transfer_src.subresourceRange.levelCount = 1;
        combine_to_transfer_src.subresourceRange.baseArrayLayer = 0;
        combine_to_transfer_src.subresourceRange.layerCount = 1;
        VkImageMemoryBarrier offscreen_to_transfer_dst_for_combine{};
        offscreen_to_transfer_dst_for_combine.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_to_transfer_dst_for_combine.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
        offscreen_to_transfer_dst_for_combine.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        offscreen_to_transfer_dst_for_combine.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        offscreen_to_transfer_dst_for_combine.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        offscreen_to_transfer_dst_for_combine.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_to_transfer_dst_for_combine.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_to_transfer_dst_for_combine.image = offscreen_color_image_;
        offscreen_to_transfer_dst_for_combine.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_to_transfer_dst_for_combine.subresourceRange.baseMipLevel = 0;
        offscreen_to_transfer_dst_for_combine.subresourceRange.levelCount = 1;
        offscreen_to_transfer_dst_for_combine.subresourceRange.baseArrayLayer = 0;
        offscreen_to_transfer_dst_for_combine.subresourceRange.layerCount = 1;
        std::array<VkImageMemoryBarrier, 2> combine_copy_pre_barriers = {
            combine_to_transfer_src,
            offscreen_to_transfer_dst_for_combine,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
            VK_PIPELINE_STAGE_TRANSFER_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(combine_copy_pre_barriers.size()),
            combine_copy_pre_barriers.data());
        VkImageBlit combine_to_offscreen_blit{};
        combine_to_offscreen_blit.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        combine_to_offscreen_blit.srcSubresource.mipLevel = 0;
        combine_to_offscreen_blit.srcSubresource.baseArrayLayer = 0;
        combine_to_offscreen_blit.srcSubresource.layerCount = 1;
        combine_to_offscreen_blit.srcOffsets[0] = {0, 0, 0};
        combine_to_offscreen_blit.srcOffsets[1] = {
            static_cast<std::int32_t>(swap_chain_extent_.width),
            static_cast<std::int32_t>(swap_chain_extent_.height),
            1};
        combine_to_offscreen_blit.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        combine_to_offscreen_blit.dstSubresource.mipLevel = 0;
        combine_to_offscreen_blit.dstSubresource.baseArrayLayer = 0;
        combine_to_offscreen_blit.dstSubresource.layerCount = 1;
        combine_to_offscreen_blit.dstOffsets[0] = {0, 0, 0};
        combine_to_offscreen_blit.dstOffsets[1] = {
            static_cast<std::int32_t>(swap_chain_extent_.width),
            static_cast<std::int32_t>(swap_chain_extent_.height),
            1};
        vkCmdBlitImage(
            command_buffers_[image_index],
            combine_image_,
            VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            offscreen_color_image_,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            1,
            &combine_to_offscreen_blit,
            VK_FILTER_LINEAR);
        VkImageMemoryBarrier offscreen_back_to_shader_read_after_combine{};
        offscreen_back_to_shader_read_after_combine.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_back_to_shader_read_after_combine.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        offscreen_back_to_shader_read_after_combine.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        offscreen_back_to_shader_read_after_combine.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        offscreen_back_to_shader_read_after_combine.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        offscreen_back_to_shader_read_after_combine.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_back_to_shader_read_after_combine.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_back_to_shader_read_after_combine.image = offscreen_color_image_;
        offscreen_back_to_shader_read_after_combine.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_back_to_shader_read_after_combine.subresourceRange.baseMipLevel = 0;
        offscreen_back_to_shader_read_after_combine.subresourceRange.levelCount = 1;
        offscreen_back_to_shader_read_after_combine.subresourceRange.baseArrayLayer = 0;
        offscreen_back_to_shader_read_after_combine.subresourceRange.layerCount = 1;
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_TRANSFER_BIT,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            1,
            &offscreen_back_to_shader_read_after_combine);

        VkImageMemoryBarrier offscreen_color_to_transfer_src{};
        offscreen_color_to_transfer_src.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_color_to_transfer_src.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        offscreen_color_to_transfer_src.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
        offscreen_color_to_transfer_src.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        offscreen_color_to_transfer_src.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        offscreen_color_to_transfer_src.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_transfer_src.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_transfer_src.image = offscreen_color_image_;
        offscreen_color_to_transfer_src.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_color_to_transfer_src.subresourceRange.baseMipLevel = 0;
        offscreen_color_to_transfer_src.subresourceRange.levelCount = 1;
        offscreen_color_to_transfer_src.subresourceRange.baseArrayLayer = 0;
        offscreen_color_to_transfer_src.subresourceRange.layerCount = 1;
        offscreen_color_to_transfer_src.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
        offscreen_color_to_transfer_src.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        VkImageMemoryBarrier swapchain_to_transfer_dst{};
        swapchain_to_transfer_dst.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        swapchain_to_transfer_dst.srcAccessMask = 0;
        swapchain_to_transfer_dst.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        swapchain_to_transfer_dst.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        swapchain_to_transfer_dst.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        swapchain_to_transfer_dst.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_transfer_dst.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_transfer_dst.image = swap_chain_images_[image_index];
        swapchain_to_transfer_dst.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        swapchain_to_transfer_dst.subresourceRange.baseMipLevel = 0;
        swapchain_to_transfer_dst.subresourceRange.levelCount = 1;
        swapchain_to_transfer_dst.subresourceRange.baseArrayLayer = 0;
        swapchain_to_transfer_dst.subresourceRange.layerCount = 1;

        std::array<VkImageMemoryBarrier, 2> pre_blit_barriers = {
            offscreen_color_to_transfer_src,
            swapchain_to_transfer_dst,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_PIPELINE_STAGE_TRANSFER_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(pre_blit_barriers.size()),
            pre_blit_barriers.data());

        VkImageBlit blit_region{};
        blit_region.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        blit_region.srcSubresource.mipLevel = 0;
        blit_region.srcSubresource.baseArrayLayer = 0;
        blit_region.srcSubresource.layerCount = 1;
        blit_region.srcOffsets[0] = {0, 0, 0};
        blit_region.srcOffsets[1] = {
            static_cast<std::int32_t>(swap_chain_extent_.width),
            static_cast<std::int32_t>(swap_chain_extent_.height),
            1};
        blit_region.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        blit_region.dstSubresource.mipLevel = 0;
        blit_region.dstSubresource.baseArrayLayer = 0;
        blit_region.dstSubresource.layerCount = 1;
        blit_region.dstOffsets[0] = {0, 0, 0};
        blit_region.dstOffsets[1] = {
            static_cast<std::int32_t>(swap_chain_extent_.width),
            static_cast<std::int32_t>(swap_chain_extent_.height),
            1};

        vkCmdBlitImage(
            command_buffers_[image_index],
            offscreen_color_image_,
            VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            swap_chain_images_[image_index],
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            1,
            &blit_region,
            VK_FILTER_LINEAR);

        VkImageMemoryBarrier swapchain_to_present{};
        swapchain_to_present.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        swapchain_to_present.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        swapchain_to_present.dstAccessMask = 0;
        swapchain_to_present.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        swapchain_to_present.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        swapchain_to_present.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_present.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_present.image = swap_chain_images_[image_index];
        swapchain_to_present.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        swapchain_to_present.subresourceRange.baseMipLevel = 0;
        swapchain_to_present.subresourceRange.levelCount = 1;
        swapchain_to_present.subresourceRange.baseArrayLayer = 0;
        swapchain_to_present.subresourceRange.layerCount = 1;

        VkImageMemoryBarrier offscreen_color_to_shader_read{};
        offscreen_color_to_shader_read.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        offscreen_color_to_shader_read.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
        offscreen_color_to_shader_read.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        offscreen_color_to_shader_read.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        offscreen_color_to_shader_read.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        offscreen_color_to_shader_read.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_shader_read.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        offscreen_color_to_shader_read.image = offscreen_color_image_;
        offscreen_color_to_shader_read.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        offscreen_color_to_shader_read.subresourceRange.baseMipLevel = 0;
        offscreen_color_to_shader_read.subresourceRange.levelCount = 1;
        offscreen_color_to_shader_read.subresourceRange.baseArrayLayer = 0;
        offscreen_color_to_shader_read.subresourceRange.layerCount = 1;

        std::array<VkImageMemoryBarrier, 2> post_blit_barriers = {
            swapchain_to_present,
            offscreen_color_to_shader_read,
        };
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_TRANSFER_BIT,
            VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            static_cast<std::uint32_t>(post_blit_barriers.size()),
            post_blit_barriers.data());
    } else {
        VkImageMemoryBarrier swapchain_to_present{};
        swapchain_to_present.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        swapchain_to_present.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        swapchain_to_present.dstAccessMask = 0;
        swapchain_to_present.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        swapchain_to_present.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        swapchain_to_present.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_present.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        swapchain_to_present.image = swap_chain_images_[image_index];
        swapchain_to_present.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        swapchain_to_present.subresourceRange.baseMipLevel = 0;
        swapchain_to_present.subresourceRange.levelCount = 1;
        swapchain_to_present.subresourceRange.baseArrayLayer = 0;
        swapchain_to_present.subresourceRange.layerCount = 1;
        vkCmdPipelineBarrier(
            command_buffers_[image_index],
            VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
            0,
            0,
            nullptr,
            0,
            nullptr,
            1,
            &swapchain_to_present);
    }

    VK_CHECK(vkEndCommandBuffer(command_buffers_[image_index]));
}

void VulkanRenderer::Render2DLayer(std::uint32_t image_index, const Scene& scene) {
    if (!scene.render_2d.enabled) {
        return;
    }

    auto tile_sprites = tilemap_chunk_.ExpandVisibleTiles(scene);
    if (!tile_sprites.empty()) {
        Scene merged_scene = scene;
        merged_scene.render_2d.sprites.insert(
            merged_scene.render_2d.sprites.end(),
            tile_sprites.begin(),
            tile_sprites.end());
        SyncBindlessTexturesForScene(merged_scene);
        const SpriteBatch::BuildResult build = sprite_batch_.Build(merged_scene);
        const glm::vec2 viewport = scene.render_2d.camera.viewport_world_size;
        const glm::mat4 ortho = glm::ortho(
            -viewport.x * 0.5F,
            viewport.x * 0.5F,
            -viewport.y * 0.5F,
            viewport.y * 0.5F,
            -10.0F,
            10.0F);

        PerFramePushConstants per_frame_push{};
        per_frame_push.view_proj = ortho;
        per_frame_push.light_dir = glm::vec4(0.0F, 0.0F, -1.0F, 0.0F);
        per_frame_push.light_color = glm::vec4(1.0F);

        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(PerFramePushConstants),
            &per_frame_push);

        for (const SpriteBatch::DrawPacket& packet : build.draws) {
            PerDrawPushConstants per_draw_push{};
            per_draw_push.model = packet.model;
            per_draw_push.color = packet.tint;
            vkCmdPushConstants(
                command_buffers_[image_index],
                pipeline_layout_,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                sizeof(PerFramePushConstants),
                sizeof(PerDrawPushConstants),
                &per_draw_push);
            TexturePushConstants texture_push{};
            texture_push.texture_index = packet.texture_index;
            vkCmdPushConstants(
                command_buffers_[image_index],
                pipeline_layout_,
                VK_SHADER_STAGE_FRAGMENT_BIT,
                sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
                sizeof(TexturePushConstants),
                &texture_push);
            vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
        }
        return;
    }

    SyncBindlessTexturesForScene(scene);
    const SpriteBatch::BuildResult build = sprite_batch_.Build(scene);
    const glm::vec2 viewport = scene.render_2d.camera.viewport_world_size;
    const glm::mat4 ortho = glm::ortho(
        -viewport.x * 0.5F,
        viewport.x * 0.5F,
        -viewport.y * 0.5F,
        viewport.y * 0.5F,
        -10.0F,
        10.0F);

    PerFramePushConstants per_frame_push{};
    per_frame_push.view_proj = ortho;
    per_frame_push.light_dir = glm::vec4(0.0F, 0.0F, -1.0F, 0.0F);
    per_frame_push.light_color = glm::vec4(1.0F);

    vkCmdPushConstants(
        command_buffers_[image_index],
        pipeline_layout_,
        VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
        0,
        sizeof(PerFramePushConstants),
        &per_frame_push);

    for (const SpriteBatch::DrawPacket& packet : build.draws) {
        PerDrawPushConstants per_draw_push{};
        per_draw_push.model = packet.model;
        per_draw_push.color = packet.tint;
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &per_draw_push);
        TexturePushConstants texture_push{};
        texture_push.texture_index = packet.texture_index;
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(TexturePushConstants),
            &texture_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
    }
}

void VulkanRenderer::Render3DLayer(std::uint32_t image_index, const Scene& scene, const Camera& camera) {
    const glm::mat4 view_proj = camera.GetProjectionMatrix() * camera.GetViewMatrix();
    PerFramePushConstants per_frame_push{};
    per_frame_push.view_proj = view_proj;
    per_frame_push.light_dir = glm::vec4(glm::normalize(scene.directional_light.direction), 0.0F);
    const glm::vec3 light_color = scene.directional_light.color * scene.directional_light.intensity;
    per_frame_push.light_color = glm::vec4(light_color, 1.0F);

    vkCmdPushConstants(
        command_buffers_[image_index],
        pipeline_layout_,
        VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
        0,
        sizeof(PerFramePushConstants),
        &per_frame_push);

    for (const Entity& entity : scene.entities) {
        glm::mat4 model(1.0F);
        model = glm::translate(model, entity.transform.pos);
        model = glm::rotate(model, entity.transform.rot.x, glm::vec3(1.0F, 0.0F, 0.0F));
        model = glm::rotate(model, entity.transform.rot.y, glm::vec3(0.0F, 1.0F, 0.0F));
        model = glm::rotate(model, entity.transform.rot.z, glm::vec3(0.0F, 0.0F, 1.0F));
        model = glm::scale(model, entity.transform.scale);

        PerDrawPushConstants per_draw_push{};
        per_draw_push.model = model;
        per_draw_push.color = entity.renderable.color;
        TexturePushConstants texture_push{};
        texture_push.texture_index = 0;
        texture_push.padding0 = 1;  // toon flag in shader

        if (entity.mesh.IsValid()) {
            auto [it, inserted] = mesh_cache_.try_emplace(entity.mesh.source);
            if (inserted) {
                it->second.loaded = MeshLoader::LoadSimpleGltf(
                    entity.mesh.source,
                    entity.mesh.primitive_index,
                    it->second.mesh,
                    it->second.error);
                if (!it->second.loaded) {
                    GF_LOG_WARN("Mesh load failed: " + entity.mesh.source + " (" + it->second.error + ")");
                }
            }
            if (it->second.loaded) {
                const glm::vec3 mesh_size = glm::max(it->second.mesh.bounds_max - it->second.mesh.bounds_min, glm::vec3(0.01F));
                const float approx_scale = std::max({mesh_size.x, mesh_size.y, mesh_size.z});
                per_draw_push.model = glm::scale(per_draw_push.model, glm::vec3(approx_scale, approx_scale, approx_scale));
            }
        }

        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &per_draw_push);
        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants) + sizeof(PerDrawPushConstants),
            sizeof(TexturePushConstants),
            &texture_push);
        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
    }
}

void VulkanRenderer::CreateBuffer(
    VkDeviceSize size,
    VkBufferUsageFlags usage,
    VkMemoryPropertyFlags properties,
    VkBuffer& buffer,
    VkDeviceMemory& buffer_memory) const {
    VkBufferCreateInfo buffer_info{};
    buffer_info.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    buffer_info.size = size;
    buffer_info.usage = usage;
    buffer_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VK_CHECK(vkCreateBuffer(device_, &buffer_info, nullptr, &buffer));

    VkMemoryRequirements mem_requirements{};
    vkGetBufferMemoryRequirements(device_, buffer, &mem_requirements);

    VkMemoryAllocateInfo alloc_info{};
    alloc_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc_info.allocationSize = mem_requirements.size;
    alloc_info.memoryTypeIndex = FindMemoryType(mem_requirements.memoryTypeBits, properties);
    VK_CHECK(vkAllocateMemory(device_, &alloc_info, nullptr, &buffer_memory));
    VK_CHECK(vkBindBufferMemory(device_, buffer, buffer_memory, 0));
}

void VulkanRenderer::TransitionImageLayout(
    VkImage image,
    VkFormat /*format*/,
    VkImageLayout old_layout,
    VkImageLayout new_layout) const {
    VkCommandBufferAllocateInfo alloc_info{};
    alloc_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    alloc_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    alloc_info.commandPool = command_pool_;
    alloc_info.commandBufferCount = 1;

    VkCommandBuffer command_buffer = VK_NULL_HANDLE;
    VK_CHECK(vkAllocateCommandBuffers(device_, &alloc_info, &command_buffer));

    VkCommandBufferBeginInfo begin_info{};
    begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin_info.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    VK_CHECK(vkBeginCommandBuffer(command_buffer, &begin_info));

    VkImageMemoryBarrier barrier{};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    barrier.oldLayout = old_layout;
    barrier.newLayout = new_layout;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = image;
    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    barrier.subresourceRange.baseMipLevel = 0;
    barrier.subresourceRange.levelCount = 1;
    barrier.subresourceRange.baseArrayLayer = 0;
    barrier.subresourceRange.layerCount = 1;

    VkPipelineStageFlags source_stage = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
    VkPipelineStageFlags destination_stage = VK_PIPELINE_STAGE_TRANSFER_BIT;
    if (old_layout == VK_IMAGE_LAYOUT_UNDEFINED && new_layout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL) {
        barrier.srcAccessMask = 0;
        barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    } else if (
        old_layout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL &&
        new_layout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL) {
        barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        source_stage = VK_PIPELINE_STAGE_TRANSFER_BIT;
        destination_stage = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
    } else {
        throw std::runtime_error("Unsupported texture layout transition.");
    }

    vkCmdPipelineBarrier(
        command_buffer,
        source_stage,
        destination_stage,
        0,
        0,
        nullptr,
        0,
        nullptr,
        1,
        &barrier);
    VK_CHECK(vkEndCommandBuffer(command_buffer));

    VkSubmitInfo submit_info{};
    submit_info.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit_info.commandBufferCount = 1;
    submit_info.pCommandBuffers = &command_buffer;
    VK_CHECK(vkQueueSubmit(graphics_queue_, 1, &submit_info, VK_NULL_HANDLE));
    VK_CHECK(vkQueueWaitIdle(graphics_queue_));

    vkFreeCommandBuffers(device_, command_pool_, 1, &command_buffer);
}

void VulkanRenderer::CopyBufferToImage(VkBuffer buffer, VkImage image, std::uint32_t width, std::uint32_t height) const {
    VkCommandBufferAllocateInfo alloc_info{};
    alloc_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    alloc_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    alloc_info.commandPool = command_pool_;
    alloc_info.commandBufferCount = 1;

    VkCommandBuffer command_buffer = VK_NULL_HANDLE;
    VK_CHECK(vkAllocateCommandBuffers(device_, &alloc_info, &command_buffer));

    VkCommandBufferBeginInfo begin_info{};
    begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin_info.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    VK_CHECK(vkBeginCommandBuffer(command_buffer, &begin_info));

    VkBufferImageCopy region{};
    region.bufferOffset = 0;
    region.bufferRowLength = 0;
    region.bufferImageHeight = 0;
    region.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    region.imageSubresource.mipLevel = 0;
    region.imageSubresource.baseArrayLayer = 0;
    region.imageSubresource.layerCount = 1;
    region.imageOffset = {0, 0, 0};
    region.imageExtent = {width, height, 1};

    vkCmdCopyBufferToImage(command_buffer, buffer, image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
    VK_CHECK(vkEndCommandBuffer(command_buffer));

    VkSubmitInfo submit_info{};
    submit_info.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit_info.commandBufferCount = 1;
    submit_info.pCommandBuffers = &command_buffer;
    VK_CHECK(vkQueueSubmit(graphics_queue_, 1, &submit_info, VK_NULL_HANDLE));
    VK_CHECK(vkQueueWaitIdle(graphics_queue_));

    vkFreeCommandBuffers(device_, command_pool_, 1, &command_buffer);
}

void VulkanRenderer::DestroyBindlessTextures() {
    if (device_ == VK_NULL_HANDLE) {
        return;
    }

    for (auto& texture : bindless_textures_) {
        if (texture.sampler != VK_NULL_HANDLE) {
            vkDestroySampler(device_, texture.sampler, nullptr);
            texture.sampler = VK_NULL_HANDLE;
        }
        if (texture.image_view != VK_NULL_HANDLE) {
            vkDestroyImageView(device_, texture.image_view, nullptr);
            texture.image_view = VK_NULL_HANDLE;
        }
        if (texture.image != VK_NULL_HANDLE) {
            vkDestroyImage(device_, texture.image, nullptr);
            texture.image = VK_NULL_HANDLE;
        }
        if (texture.image_memory != VK_NULL_HANDLE) {
            vkFreeMemory(device_, texture.image_memory, nullptr);
            texture.image_memory = VK_NULL_HANDLE;
        }
    }
    bindless_textures_.clear();

    if (bindless_texture_descriptor_pool_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorPool(device_, bindless_texture_descriptor_pool_, nullptr);
        bindless_texture_descriptor_pool_ = VK_NULL_HANDLE;
    }
    bindless_texture_descriptor_set_ = VK_NULL_HANDLE;
    last_uploaded_bindless_texture_count_ = 0;
}

void VulkanRenderer::UploadBindlessTextures() {
    if (bindless_texture_descriptor_set_layout_ == VK_NULL_HANDLE) {
        return;
    }

    const std::vector<std::string>& texture_paths = sprite_batch_.BindlessTexturePaths();
    if (texture_paths.empty()) {
        return;
    }

    bindless_textures_.reserve(texture_paths.size());
    for (std::size_t i = 0; i < texture_paths.size(); ++i) {
        TextureLoader::LoadedImage loaded{};
        if (i == 0 || texture_paths[i].empty()) {
            loaded.width = 1;
            loaded.height = 1;
            loaded.rgba8 = {255, 255, 255, 255};
        } else {
            loaded = TextureLoader::LoadImageRGBA8(texture_paths[i]);
        }

        VkBuffer staging_buffer = VK_NULL_HANDLE;
        VkDeviceMemory staging_buffer_memory = VK_NULL_HANDLE;
        const VkDeviceSize image_size = static_cast<VkDeviceSize>(loaded.rgba8.size());
        CreateBuffer(
            image_size,
            VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            staging_buffer,
            staging_buffer_memory);

        void* mapped = nullptr;
        VK_CHECK(vkMapMemory(device_, staging_buffer_memory, 0, image_size, 0, &mapped));
        std::memcpy(mapped, loaded.rgba8.data(), loaded.rgba8.size());
        vkUnmapMemory(device_, staging_buffer_memory);

        BindlessTextureGpuResource texture{};
        VkImageCreateInfo image_info{};
        image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        image_info.imageType = VK_IMAGE_TYPE_2D;
        image_info.extent.width = static_cast<std::uint32_t>(loaded.width);
        image_info.extent.height = static_cast<std::uint32_t>(loaded.height);
        image_info.extent.depth = 1;
        image_info.mipLevels = 1;
        image_info.arrayLayers = 1;
        image_info.format = VK_FORMAT_R8G8B8A8_UNORM;
        image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
        image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        image_info.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
        image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        image_info.samples = VK_SAMPLE_COUNT_1_BIT;
        VK_CHECK(vkCreateImage(device_, &image_info, nullptr, &texture.image));

        VkMemoryRequirements mem_requirements{};
        vkGetImageMemoryRequirements(device_, texture.image, &mem_requirements);
        VkMemoryAllocateInfo alloc_info{};
        alloc_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        alloc_info.allocationSize = mem_requirements.size;
        alloc_info.memoryTypeIndex = FindMemoryType(mem_requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        VK_CHECK(vkAllocateMemory(device_, &alloc_info, nullptr, &texture.image_memory));
        VK_CHECK(vkBindImageMemory(device_, texture.image, texture.image_memory, 0));

        TransitionImageLayout(
            texture.image,
            VK_FORMAT_R8G8B8A8_UNORM,
            VK_IMAGE_LAYOUT_UNDEFINED,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
        CopyBufferToImage(
            staging_buffer,
            texture.image,
            static_cast<std::uint32_t>(loaded.width),
            static_cast<std::uint32_t>(loaded.height));
        TransitionImageLayout(
            texture.image,
            VK_FORMAT_R8G8B8A8_UNORM,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);

        vkDestroyBuffer(device_, staging_buffer, nullptr);
        vkFreeMemory(device_, staging_buffer_memory, nullptr);

        VkImageViewCreateInfo view_info{};
        view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        view_info.image = texture.image;
        view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
        view_info.format = VK_FORMAT_R8G8B8A8_UNORM;
        view_info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        view_info.subresourceRange.baseMipLevel = 0;
        view_info.subresourceRange.levelCount = 1;
        view_info.subresourceRange.baseArrayLayer = 0;
        view_info.subresourceRange.layerCount = 1;
        VK_CHECK(vkCreateImageView(device_, &view_info, nullptr, &texture.image_view));

        VkSamplerCreateInfo sampler_info{};
        sampler_info.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        sampler_info.magFilter = VK_FILTER_LINEAR;
        sampler_info.minFilter = VK_FILTER_LINEAR;
        sampler_info.addressModeU = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        sampler_info.addressModeV = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        sampler_info.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        sampler_info.anisotropyEnable = VK_FALSE;
        sampler_info.maxAnisotropy = 1.0F;
        sampler_info.borderColor = VK_BORDER_COLOR_INT_OPAQUE_BLACK;
        sampler_info.unnormalizedCoordinates = VK_FALSE;
        sampler_info.compareEnable = VK_FALSE;
        sampler_info.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
        sampler_info.minLod = 0.0F;
        sampler_info.maxLod = 0.0F;
        VK_CHECK(vkCreateSampler(device_, &sampler_info, nullptr, &texture.sampler));

        bindless_textures_.push_back(texture);
    }

    VkDescriptorPoolSize descriptor_pool_size{};
    descriptor_pool_size.type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    descriptor_pool_size.descriptorCount = kMaxBindlessTextures;
    VkDescriptorPoolCreateInfo descriptor_pool_info{};
    descriptor_pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    descriptor_pool_info.maxSets = 1;
    descriptor_pool_info.poolSizeCount = 1;
    descriptor_pool_info.pPoolSizes = &descriptor_pool_size;
    VK_CHECK(vkCreateDescriptorPool(device_, &descriptor_pool_info, nullptr, &bindless_texture_descriptor_pool_));

    VkDescriptorSetAllocateInfo allocate_info{};
    allocate_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    allocate_info.descriptorPool = bindless_texture_descriptor_pool_;
    allocate_info.descriptorSetCount = 1;
    allocate_info.pSetLayouts = &bindless_texture_descriptor_set_layout_;
    VK_CHECK(vkAllocateDescriptorSets(device_, &allocate_info, &bindless_texture_descriptor_set_));

    std::vector<VkDescriptorImageInfo> descriptor_images{};
    descriptor_images.reserve(bindless_textures_.size());
    for (const auto& texture : bindless_textures_) {
        VkDescriptorImageInfo image_info{};
        image_info.sampler = texture.sampler;
        image_info.imageView = texture.image_view;
        image_info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        descriptor_images.push_back(image_info);
    }
    VkWriteDescriptorSet descriptor_write{};
    descriptor_write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    descriptor_write.dstSet = bindless_texture_descriptor_set_;
    descriptor_write.dstBinding = 0;
    descriptor_write.dstArrayElement = 0;
    descriptor_write.descriptorCount = static_cast<std::uint32_t>(descriptor_images.size());
    descriptor_write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    descriptor_write.pImageInfo = descriptor_images.data();
    vkUpdateDescriptorSets(device_, 1, &descriptor_write, 0, nullptr);
    last_uploaded_bindless_texture_count_ = bindless_textures_.size();
}

void VulkanRenderer::SyncBindlessTexturesForScene(const Scene& scene) {
    if (!scene.render_2d.enabled) {
        return;
    }

    const bool changed = sprite_batch_.RebuildTextureIndexForScene(scene);
    if (!changed && bindless_texture_descriptor_set_ != VK_NULL_HANDLE) {
        return;
    }

    DestroyBindlessTextures();
    UploadBindlessTextures();
}

void VulkanRenderer::DrawFrame(const Scene& scene, const Camera& camera) {
    VK_CHECK(vkWaitForFences(device_, 1, &in_flight_fences_[current_frame_], VK_TRUE, UINT64_MAX));

    std::uint32_t image_index = 0;
    VkResult result = vkAcquireNextImageKHR(
        device_,
        swap_chain_,
        UINT64_MAX,
        image_available_semaphores_[current_frame_],
        VK_NULL_HANDLE,
        &image_index);

    if (result == VK_ERROR_OUT_OF_DATE_KHR) {
        RecreateSwapChain();
        return;
    }

    if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR) {
        VK_CHECK(result);
    }

    VK_CHECK(vkResetFences(device_, 1, &in_flight_fences_[current_frame_]));

    RecordCommandBuffer(image_index, scene, camera);

    VkSubmitInfo submit_info{};
    submit_info.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;

    VkSemaphore wait_semaphores[] = {image_available_semaphores_[current_frame_]};
    VkPipelineStageFlags wait_stages[] = {
        VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT};
    submit_info.waitSemaphoreCount = 1;
    submit_info.pWaitSemaphores = wait_semaphores;
    submit_info.pWaitDstStageMask = wait_stages;
    submit_info.commandBufferCount = 1;
    submit_info.pCommandBuffers = &command_buffers_[image_index];

    VkSemaphore signal_semaphores[] = {render_finished_semaphores_[current_frame_]};
    submit_info.signalSemaphoreCount = 1;
    submit_info.pSignalSemaphores = signal_semaphores;

    VK_CHECK(vkQueueSubmit(graphics_queue_, 1, &submit_info, in_flight_fences_[current_frame_]));

    VkPresentInfoKHR present_info{};
    present_info.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    present_info.waitSemaphoreCount = 1;
    present_info.pWaitSemaphores = signal_semaphores;

    VkSwapchainKHR swap_chains[] = {swap_chain_};
    present_info.swapchainCount = 1;
    present_info.pSwapchains = swap_chains;
    present_info.pImageIndices = &image_index;

    result = vkQueuePresentKHR(present_queue_, &present_info);

    if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR || framebuffer_resized_) {
        framebuffer_resized_ = false;
        RecreateSwapChain();
    } else if (result != VK_SUCCESS) {
        VK_CHECK(result);
    }

    current_frame_ = (current_frame_ + 1) % kMaxFramesInFlight;
}

void VulkanRenderer::CleanupSwapChain() {
    DestroyOffscreenResources();

    for (VkFramebuffer framebuffer : swap_chain_framebuffers_) {
        vkDestroyFramebuffer(device_, framebuffer, nullptr);
    }
    swap_chain_framebuffers_.clear();

    if (!command_buffers_.empty()) {
        vkFreeCommandBuffers(device_, command_pool_, static_cast<std::uint32_t>(command_buffers_.size()), command_buffers_.data());
        command_buffers_.clear();
    }

    if (graphics_pipeline_ != VK_NULL_HANDLE) {
        vkDestroyPipeline(device_, graphics_pipeline_, nullptr);
        graphics_pipeline_ = VK_NULL_HANDLE;
    }
    if (combine_pipeline_ != VK_NULL_HANDLE) {
        vkDestroyPipeline(device_, combine_pipeline_, nullptr);
        combine_pipeline_ = VK_NULL_HANDLE;
    }
    if (blur_pipeline_ != VK_NULL_HANDLE) {
        vkDestroyPipeline(device_, blur_pipeline_, nullptr);
        blur_pipeline_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_pipeline_ != VK_NULL_HANDLE) {
        vkDestroyPipeline(device_, bloom_extract_pipeline_, nullptr);
        bloom_extract_pipeline_ = VK_NULL_HANDLE;
    }

    if (pipeline_layout_ != VK_NULL_HANDLE) {
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
    }
    if (post_process_descriptor_set_layout_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorSetLayout(device_, post_process_descriptor_set_layout_, nullptr);
        post_process_descriptor_set_layout_ = VK_NULL_HANDLE;
    }
    if (combine_descriptor_set_layout_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorSetLayout(device_, combine_descriptor_set_layout_, nullptr);
        combine_descriptor_set_layout_ = VK_NULL_HANDLE;
    }
    if (bindless_texture_descriptor_set_layout_ != VK_NULL_HANDLE) {
        vkDestroyDescriptorSetLayout(device_, bindless_texture_descriptor_set_layout_, nullptr);
        bindless_texture_descriptor_set_layout_ = VK_NULL_HANDLE;
    }

    if (render_pass_ != VK_NULL_HANDLE) {
        vkDestroyRenderPass(device_, render_pass_, nullptr);
        render_pass_ = VK_NULL_HANDLE;
    }
    if (bloom_extract_render_pass_ != VK_NULL_HANDLE) {
        vkDestroyRenderPass(device_, bloom_extract_render_pass_, nullptr);
        bloom_extract_render_pass_ = VK_NULL_HANDLE;
    }
    if (combine_render_pass_ != VK_NULL_HANDLE) {
        vkDestroyRenderPass(device_, combine_render_pass_, nullptr);
        combine_render_pass_ = VK_NULL_HANDLE;
    }
    if (blur_render_pass_ != VK_NULL_HANDLE) {
        vkDestroyRenderPass(device_, blur_render_pass_, nullptr);
        blur_render_pass_ = VK_NULL_HANDLE;
    }

    for (VkImageView image_view : swap_chain_image_views_) {
        vkDestroyImageView(device_, image_view, nullptr);
    }
    swap_chain_image_views_.clear();

    if (swap_chain_ != VK_NULL_HANDLE) {
        vkDestroySwapchainKHR(device_, swap_chain_, nullptr);
        swap_chain_ = VK_NULL_HANDLE;
    }
}

void VulkanRenderer::RecreateSwapChain() {
    int width = 0;
    int height = 0;
    glfwGetFramebufferSize(window_, &width, &height);
    while (width == 0 || height == 0) {
        glfwGetFramebufferSize(window_, &width, &height);
        glfwWaitEvents();
    }

    VK_CHECK(vkDeviceWaitIdle(device_));

    CleanupSwapChain();
    CreateSwapChain();
    CreateImageViews();
    CreateRenderPass();
    CreateGraphicsPipeline();
    CreateFramebuffers();
    CreateOffscreenResources();
    CreateCommandBuffers();
}

std::uint32_t VulkanRenderer::FindMemoryType(std::uint32_t type_filter, VkMemoryPropertyFlags properties) const {
    VkPhysicalDeviceMemoryProperties memory_properties{};
    vkGetPhysicalDeviceMemoryProperties(physical_device_, &memory_properties);
    for (std::uint32_t i = 0; i < memory_properties.memoryTypeCount; ++i) {
        if ((type_filter & (1U << i)) != 0U &&
            (memory_properties.memoryTypes[i].propertyFlags & properties) == properties) {
            return i;
        }
    }
    throw std::runtime_error("Failed to find suitable memory type.");
}

VkFormat VulkanRenderer::FindSupportedDepthFormat() const {
    constexpr std::array<VkFormat, 3> depth_formats = {
        VK_FORMAT_D32_SFLOAT,
        VK_FORMAT_D32_SFLOAT_S8_UINT,
        VK_FORMAT_D24_UNORM_S8_UINT,
    };
    for (VkFormat format : depth_formats) {
        VkFormatProperties properties{};
        vkGetPhysicalDeviceFormatProperties(physical_device_, format, &properties);
        if ((properties.optimalTilingFeatures & VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT) != 0U) {
            return format;
        }
    }
    throw std::runtime_error("Failed to find supported depth format.");
}

bool VulkanRenderer::ValidationLayersSupported() const {
    std::uint32_t layer_count = 0;
    VK_CHECK(vkEnumerateInstanceLayerProperties(&layer_count, nullptr));

    std::vector<VkLayerProperties> available_layers(layer_count);
    VK_CHECK(vkEnumerateInstanceLayerProperties(&layer_count, available_layers.data()));

    for (const char* layer_name : kValidationLayers) {
        bool found = false;
        for (const auto& layer_properties : available_layers) {
            if (std::string(layer_name) == layer_properties.layerName) {
                found = true;
                break;
            }
        }

        if (!found) {
            return false;
        }
    }

    return true;
}

bool VulkanRenderer::DebugUtilsExtensionSupported() const {
    std::uint32_t extension_count = 0;
    VK_CHECK(vkEnumerateInstanceExtensionProperties(nullptr, &extension_count, nullptr));
    std::vector<VkExtensionProperties> available_extensions(extension_count);
    VK_CHECK(vkEnumerateInstanceExtensionProperties(nullptr, &extension_count, available_extensions.data()));
    for (const VkExtensionProperties& extension : available_extensions) {
        if (std::string(extension.extensionName) == kDebugUtilsExtensionName) {
            return true;
        }
    }
    return false;
}

VKAPI_ATTR VkBool32 VKAPI_CALL VulkanRenderer::DebugCallback(
    VkDebugUtilsMessageSeverityFlagBitsEXT message_severity,
    VkDebugUtilsMessageTypeFlagsEXT message_type,
    const VkDebugUtilsMessengerCallbackDataEXT* callback_data,
    void* user_data) {
    (void)message_type;
    (void)user_data;
    if (callback_data == nullptr || callback_data->pMessage == nullptr) {
        return VK_FALSE;
    }
    if ((message_severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0U) {
        GF_LOG_ERROR(callback_data->pMessage);
    } else {
        GF_LOG_WARN(callback_data->pMessage);
    }
    return VK_FALSE;
}

VkResult VulkanRenderer::CreateDebugUtilsMessengerEXT(
    VkInstance instance,
    const VkDebugUtilsMessengerCreateInfoEXT* create_info,
    const VkAllocationCallbacks* allocator,
    VkDebugUtilsMessengerEXT* debug_messenger) {
    const auto function = reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(
        vkGetInstanceProcAddr(instance, "vkCreateDebugUtilsMessengerEXT"));
    if (function != nullptr) {
        return function(instance, create_info, allocator, debug_messenger);
    }
    return VK_ERROR_EXTENSION_NOT_PRESENT;
}

void VulkanRenderer::DestroyDebugUtilsMessengerEXT(
    VkInstance instance,
    VkDebugUtilsMessengerEXT debug_messenger,
    const VkAllocationCallbacks* allocator) {
    const auto function = reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(
        vkGetInstanceProcAddr(instance, "vkDestroyDebugUtilsMessengerEXT"));
    if (function != nullptr) {
        function(instance, debug_messenger, allocator);
    }
}
