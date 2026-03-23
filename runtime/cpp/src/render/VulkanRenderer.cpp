#include "VulkanRenderer.h"

#include "../utils/Logger.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <limits>
#include <set>
#include <stdexcept>
#include <string>

#include <glm/gtc/matrix_transform.hpp>

namespace {
constexpr std::uint32_t kWindowWidth = 1280;
constexpr std::uint32_t kWindowHeight = 720;
constexpr int kMaxFramesInFlight = 2;
constexpr std::uint32_t kQuadVertexCount = 6;

const std::vector<const char*> kValidationLayers = {"VK_LAYER_KHRONOS_validation"};
const std::vector<const char*> kRequiredDeviceExtensions = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};

#ifdef NDEBUG
constexpr bool kEnableValidationLayers = false;
#else
constexpr bool kEnableValidationLayers = true;
#endif

VkResult CreateDebugUtilsMessengerEXT(
    VkInstance instance,
    const VkDebugUtilsMessengerCreateInfoEXT* create_info,
    const VkAllocationCallbacks* allocator,
    VkDebugUtilsMessengerEXT* debug_messenger) {
    const auto function = reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(
        vkGetInstanceProcAddr(instance, "vkCreateDebugUtilsMessengerEXT"));
    if (function == nullptr) {
        return VK_ERROR_EXTENSION_NOT_PRESENT;
    }
    return function(instance, create_info, allocator, debug_messenger);
}

void DestroyDebugUtilsMessengerEXT(
    VkInstance instance,
    VkDebugUtilsMessengerEXT debug_messenger,
    const VkAllocationCallbacks* allocator) {
    const auto function = reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(
        vkGetInstanceProcAddr(instance, "vkDestroyDebugUtilsMessengerEXT"));
    if (function != nullptr) {
        function(instance, debug_messenger, allocator);
    }
}

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

    CreateInstance();
    SetupDebugMessenger();
    CreateSurface();
    PickPhysicalDevice();
    CreateLogicalDevice();
    CreateSwapChain();
    CreateImageViews();
    CreateRenderPass();
    CreateGraphicsPipeline();
    CreateFramebuffers();
    CreateCommandPool();
    CreateCommandBuffers();
    CreateSyncObjects();
}

void VulkanRenderer::Shutdown() {
    if (device_ != VK_NULL_HANDLE) {
        vkDeviceWaitIdle(device_);
    }

    GF_LOG_INFO("Vulkan shutdown: cleanup swapchain resources");
    CleanupSwapChain();

    for (std::size_t i = 0; i < image_available_semaphores_.size(); ++i) {
        if (render_finished_semaphores_[i] != VK_NULL_HANDLE) {
            vkDestroySemaphore(device_, render_finished_semaphores_[i], nullptr);
            render_finished_semaphores_[i] = VK_NULL_HANDLE;
        }
        if (image_available_semaphores_[i] != VK_NULL_HANDLE) {
            vkDestroySemaphore(device_, image_available_semaphores_[i], nullptr);
            image_available_semaphores_[i] = VK_NULL_HANDLE;
        }
        if (in_flight_fences_[i] != VK_NULL_HANDLE) {
            vkDestroyFence(device_, in_flight_fences_[i], nullptr);
            in_flight_fences_[i] = VK_NULL_HANDLE;
        }
    }
    in_flight_fences_.clear();
    render_finished_semaphores_.clear();
    image_available_semaphores_.clear();

    if (command_pool_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Vulkan shutdown: destroy command pool");
        vkDestroyCommandPool(device_, command_pool_, nullptr);
        command_pool_ = VK_NULL_HANDLE;
    }

    if (device_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Vulkan shutdown: destroy logical device");
        vkDestroyDevice(device_, nullptr);
        device_ = VK_NULL_HANDLE;
    }

    if (surface_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Vulkan shutdown: destroy surface");
        vkDestroySurfaceKHR(instance_, surface_, nullptr);
        surface_ = VK_NULL_HANDLE;
    }

    if (kEnableValidationLayers && debug_messenger_ != VK_NULL_HANDLE) {
        DestroyDebugUtilsMessengerEXT(instance_, debug_messenger_, nullptr);
        debug_messenger_ = VK_NULL_HANDLE;
    }

    if (instance_ != VK_NULL_HANDLE) {
        GF_LOG_INFO("Vulkan shutdown: destroy instance");
        vkDestroyInstance(instance_, nullptr);
        instance_ = VK_NULL_HANDLE;
    }

    if (window_ != nullptr) {
        glfwDestroyWindow(window_);
        window_ = nullptr;
    }

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

void VulkanRenderer::RenderFrame(const Scene& scene, const Camera& camera) {
    DrawFrame(scene, camera);
}

void VulkanRenderer::DrawFPSOverlay(float fps) {
    static int frame_counter = 0;
    ++frame_counter;
    if ((frame_counter % 60) == 0) {
        GF_LOG_INFO("FPS overlay hook fps=" + std::to_string(fps));
    }
}

void VulkanRenderer::DrawText(const std::string& text, float x, float y) {
    static std::string last_text;
    if (text != last_text) {
        GF_LOG_INFO("DrawText stub: '" + text + "' @ (" + std::to_string(x) + ", " + std::to_string(y) + ")");
        last_text = text;
    }
}

void VulkanRenderer::FramebufferResizeCallback(GLFWwindow* window, int width, int height) {
    auto* renderer = static_cast<VulkanRenderer*>(glfwGetWindowUserPointer(window));
    if (renderer != nullptr) {
        renderer->framebuffer_resized_ = true;
        GF_LOG_INFO("Window resize detected: " + std::to_string(width) + "x" + std::to_string(height));
    }
}

VKAPI_ATTR VkBool32 VKAPI_CALL VulkanRenderer::DebugCallback(
    VkDebugUtilsMessageSeverityFlagBitsEXT message_severity,
    VkDebugUtilsMessageTypeFlagsEXT,
    const VkDebugUtilsMessengerCallbackDataEXT* callback_data,
    void*) {
    const std::string message = callback_data != nullptr && callback_data->pMessage != nullptr ? callback_data->pMessage : "(no message)";
    if ((message_severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0U) {
        GF_LOG_ERROR("Vulkan validation: " + message);
    } else if ((message_severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT) != 0U) {
        GF_LOG_WARN("Vulkan validation: " + message);
    } else {
        GF_LOG_INFO("Vulkan validation: " + message);
    }
    return VK_FALSE;
}

void VulkanRenderer::CreateInstance() {
    if (kEnableValidationLayers && !ValidationLayersSupported()) {
        throw std::runtime_error("Validation layers requested, but not available.");
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
    if (kEnableValidationLayers) {
        extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
    }

    VkDebugUtilsMessengerCreateInfoEXT debug_create_info{};
    debug_create_info.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
    debug_create_info.messageSeverity =
        VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
    debug_create_info.messageType =
        VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT |
        VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
    debug_create_info.pfnUserCallback = DebugCallback;

    VkInstanceCreateInfo create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    create_info.pApplicationInfo = &application_info;
    create_info.enabledExtensionCount = static_cast<std::uint32_t>(extensions.size());
    create_info.ppEnabledExtensionNames = extensions.data();

    if (kEnableValidationLayers) {
        create_info.enabledLayerCount = static_cast<std::uint32_t>(kValidationLayers.size());
        create_info.ppEnabledLayerNames = kValidationLayers.data();
        create_info.pNext = &debug_create_info;
    }

    VK_CHECK(vkCreateInstance(&create_info, nullptr, &instance_));
}

void VulkanRenderer::SetupDebugMessenger() {
    if (!kEnableValidationLayers) {
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
    vkEnumerateDeviceExtensionProperties(device, nullptr, &extension_count, nullptr);

    std::vector<VkExtensionProperties> available_extensions(extension_count);
    vkEnumerateDeviceExtensionProperties(device, nullptr, &extension_count, available_extensions.data());

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
    vkEnumeratePhysicalDevices(instance_, &device_count, nullptr);
    if (device_count == 0) {
        throw std::runtime_error("Failed to find GPUs with Vulkan support.");
    }

    std::vector<VkPhysicalDevice> devices(device_count);
    vkEnumeratePhysicalDevices(instance_, &device_count, devices.data());

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

    VkPhysicalDeviceFeatures device_features{};

    VkDeviceCreateInfo create_info{};
    create_info.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    create_info.queueCreateInfoCount = static_cast<std::uint32_t>(queue_create_infos.size());
    create_info.pQueueCreateInfos = queue_create_infos.data();
    create_info.pEnabledFeatures = &device_features;
    create_info.enabledExtensionCount = static_cast<std::uint32_t>(kRequiredDeviceExtensions.size());
    create_info.ppEnabledExtensionNames = kRequiredDeviceExtensions.data();

    if (kEnableValidationLayers) {
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
    create_info.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

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

    vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, nullptr);
    swap_chain_images_.resize(image_count);
    vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, swap_chain_images_.data());

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
    color_attachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

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

    const VkShaderModule vert_shader_module = CreateShaderModule(device_, vert_shader_code);
    const VkShaderModule frag_shader_module = CreateShaderModule(device_, frag_shader_code);

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
    color_blend_attachment.blendEnable = VK_FALSE;

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

    VkPipelineLayoutCreateInfo pipeline_layout_info{};
    pipeline_layout_info.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pipeline_layout_info.pushConstantRangeCount = 2;
    pipeline_layout_info.pPushConstantRanges = push_constant_ranges;

    VK_CHECK(vkCreatePipelineLayout(device_, &pipeline_layout_info, nullptr, &pipeline_layout_));

    VkGraphicsPipelineCreateInfo pipeline_info{};
    pipeline_info.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipeline_info.stageCount = 2;
    pipeline_info.pStages = shader_stages;
    pipeline_info.pVertexInputState = &vertex_input_info;
    pipeline_info.pInputAssemblyState = &input_assembly;
    pipeline_info.pViewportState = &viewport_state;
    pipeline_info.pRasterizationState = &rasterizer;
    pipeline_info.pMultisampleState = &multisampling;
    pipeline_info.pColorBlendState = &color_blending;
    pipeline_info.layout = pipeline_layout_;
    pipeline_info.renderPass = render_pass_;
    pipeline_info.subpass = 0;

    VK_CHECK(vkCreateGraphicsPipelines(device_, VK_NULL_HANDLE, 1, &pipeline_info, nullptr, &graphics_pipeline_));

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

    VkRenderPassBeginInfo render_pass_begin_info{};
    render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    render_pass_begin_info.renderPass = render_pass_;
    render_pass_begin_info.framebuffer = swap_chain_framebuffers_[image_index];
    render_pass_begin_info.renderArea.offset = {0, 0};
    render_pass_begin_info.renderArea.extent = swap_chain_extent_;

    VkClearValue clear_color = {{{0.05F, 0.06F, 0.10F, 1.0F}}};
    render_pass_begin_info.clearValueCount = 1;
    render_pass_begin_info.pClearValues = &clear_color;

    vkCmdBeginRenderPass(command_buffers_[image_index], &render_pass_begin_info, VK_SUBPASS_CONTENTS_INLINE);
    vkCmdBindPipeline(command_buffers_[image_index], VK_PIPELINE_BIND_POINT_GRAPHICS, graphics_pipeline_);

    const glm::mat4 view_proj = camera.GetProjectionMatrix() * camera.GetViewMatrix();
    PerFramePushConstants per_frame_push{};
    per_frame_push.view_proj = view_proj;
    per_frame_push.light_dir = glm::vec4(glm::normalize(glm::vec3(0.8F, 0.45F, 0.2F)), 0.0F);
    per_frame_push.light_color = glm::vec4(1.0F, 0.98F, 0.92F, 1.0F);

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

        vkCmdPushConstants(
            command_buffers_[image_index],
            pipeline_layout_,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            sizeof(PerFramePushConstants),
            sizeof(PerDrawPushConstants),
            &per_draw_push);

        vkCmdDraw(command_buffers_[image_index], kQuadVertexCount, 1, 0, 0);
    }

    DrawText("FPS Overlay Stub", 8.0F, 8.0F);

    vkCmdEndRenderPass(command_buffers_[image_index]);
    VK_CHECK(vkEndCommandBuffer(command_buffers_[image_index]));
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
    VkPipelineStageFlags wait_stages[] = {VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT};
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

    if (pipeline_layout_ != VK_NULL_HANDLE) {
        vkDestroyPipelineLayout(device_, pipeline_layout_, nullptr);
        pipeline_layout_ = VK_NULL_HANDLE;
    }

    if (render_pass_ != VK_NULL_HANDLE) {
        vkDestroyRenderPass(device_, render_pass_, nullptr);
        render_pass_ = VK_NULL_HANDLE;
    }

    for (VkImageView image_view : swap_chain_image_views_) {
        vkDestroyImageView(device_, image_view, nullptr);
    }
    swap_chain_image_views_.clear();

    if (swap_chain_ != VK_NULL_HANDLE) {
        vkDestroySwapchainKHR(device_, swap_chain_, nullptr);
        swap_chain_ = VK_NULL_HANDLE;
    }
    swap_chain_images_.clear();
}

void VulkanRenderer::RecreateSwapChain() {
    int width = 0;
    int height = 0;
    glfwGetFramebufferSize(window_, &width, &height);
    while (width == 0 || height == 0) {
        glfwGetFramebufferSize(window_, &width, &height);
        glfwWaitEvents();
    }

    vkDeviceWaitIdle(device_);

    GF_LOG_INFO("Vulkan shutdown: cleanup swapchain resources");
    CleanupSwapChain();
    CreateSwapChain();
    CreateImageViews();
    CreateRenderPass();
    CreateGraphicsPipeline();
    CreateFramebuffers();
    CreateCommandBuffers();
}

bool VulkanRenderer::ValidationLayersSupported() const {
    std::uint32_t layer_count = 0;
    vkEnumerateInstanceLayerProperties(&layer_count, nullptr);

    std::vector<VkLayerProperties> available_layers(layer_count);
    vkEnumerateInstanceLayerProperties(&layer_count, available_layers.data());

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
