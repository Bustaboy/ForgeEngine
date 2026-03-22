#include <GLFW/glfw3.h>
#include <vulkan/vulkan.h>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <cmath>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iomanip>
#include <optional>
#include <set>
#include <sstream>
#include <stdexcept>
#include <thread>
#include <utility>

namespace {

using Json = nlohmann::json;

constexpr std::uint32_t kWindowWidth = 1280;
constexpr std::uint32_t kWindowHeight = 720;
constexpr int kMaxFramesInFlight = 2;

const std::vector<const char*> kRequiredDeviceExtensions = {
    VK_KHR_SWAPCHAIN_EXTENSION_NAME,
};

enum class LogLevel { kInfo, kWarn, kError };


struct PositionComponent {
    float x = 0.0F;
    float y = 0.0F;
};

struct RenderComponent {
    float r = 1.0F;
    float g = 1.0F;
    float b = 1.0F;
    float size = 0.1F;
};

struct RuntimeEntity {
    std::string id;
    PositionComponent position;
    RenderComponent render;
    float velocity_x = 0.0F;
    float velocity_y = 0.0F;
    bool active = false;
};

constexpr int kGeneratedSceneEntityLimit = 32;
constexpr int kGeneratedEntityIdCapacity = 64;

struct GeneratedEntity {
    char id[kGeneratedEntityIdCapacity]{};
    float x = 0.0F;
    float y = 0.0F;
    float vx = 0.0F;
    float vy = 0.0F;
    float size = 0.1F;
    float r = 1.0F;
    float g = 1.0F;
    float b = 1.0F;
    int active = 0;
};

struct GeneratedSceneState {
    std::array<GeneratedEntity, kGeneratedSceneEntityLimit> entities{};
    int entity_count = 0;
    float elapsed_time = 0.0F;
};

void Log(LogLevel level, const std::string& message) {
    const auto now = std::chrono::system_clock::now();
    const auto now_time_t = std::chrono::system_clock::to_time_t(now);

    std::tm local_tm{};
#ifdef _WIN32
    localtime_s(&local_tm, &now_time_t);
#else
    localtime_r(&now_time_t, &local_tm);
#endif

    std::ostringstream stream;
    stream << std::put_time(&local_tm, "%H:%M:%S");

    const char* level_text = "INFO";
    if (level == LogLevel::kWarn) {
        level_text = "WARN";
    } else if (level == LogLevel::kError) {
        level_text = "ERROR";
    }

    std::cout << "[Runtime][" << level_text << "][" << stream.str() << "] " << message << "\n";
}

struct RuntimeLaunchOptions {
    std::optional<std::filesystem::path> manifest_path;
};

RuntimeLaunchOptions ParseRuntimeLaunchOptions(int argc, char* argv[]) {
    RuntimeLaunchOptions options;

    for (int index = 1; index < argc; ++index) {
        const std::string argument = argv[index];
        if (argument == "--manifest") {
            if (index + 1 >= argc) {
                throw std::runtime_error("Missing value for --manifest argument.");
            }

            options.manifest_path = std::filesystem::path(argv[++index]);
            continue;
        }

        Log(LogLevel::kWarn, "Ignoring unknown argument: " + argument);
    }

    return options;
}

Json ReadJsonFile(const std::filesystem::path& file_path, const std::string& label) {
    std::ifstream file_stream(file_path);
    if (!file_stream.good()) {
        throw std::runtime_error("Failed to open " + label + ": " + file_path.string());
    }

    try {
        Json root;
        file_stream >> root;
        return root;
    } catch (const std::exception& exception) {
        throw std::runtime_error(
            "Failed to parse " + label + " JSON (" + file_path.string() + "): " + exception.what());
    }
}

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

class RuntimeApp {
public:
    explicit RuntimeApp(RuntimeLaunchOptions launch_options)
        : launch_options_(std::move(launch_options)) {}

    void Run() {
        InitWindow();
        InitVulkan();
        MainLoop();
        Cleanup();
    }

private:
    RuntimeLaunchOptions launch_options_;

    GLFWwindow* window_ = nullptr;

    VkInstance instance_ = VK_NULL_HANDLE;
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

    VkCommandPool command_pool_ = VK_NULL_HANDLE;
    std::vector<VkCommandBuffer> command_buffers_;

    std::vector<VkSemaphore> image_available_semaphores_;
    std::vector<VkSemaphore> render_finished_semaphores_;
    std::vector<VkFence> in_flight_fences_;

    std::size_t current_frame_ = 0;
    bool framebuffer_resized_ = false;
    bool manifest_load_attempted_ = false;
    bool scene_loaded_from_manifest_ = false;
    bool render_loaded_scene_logged_ = false;
    std::filesystem::path generated_cpp_root_;
    std::filesystem::path generated_build_root_;
    std::filesystem::path generated_executable_path_;
    bool generated_runner_launched_ = false;
    GeneratedSceneState generated_scene_state_{};
    std::vector<RuntimeEntity> runtime_entities_;
    std::chrono::steady_clock::time_point last_update_time_ = std::chrono::steady_clock::now();

    static void FramebufferResizeCallback(GLFWwindow* window, int width, int height) {
        auto* app = static_cast<RuntimeApp*>(glfwGetWindowUserPointer(window));
        if (app != nullptr) {
            app->framebuffer_resized_ = true;
            Log(LogLevel::kInfo,
                "Window resize detected: " + std::to_string(width) + "x" + std::to_string(height));
        }
    }

    void InitWindow() {
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

        Log(LogLevel::kInfo, "Window initialized.");
    }

    void InitVulkan() {
        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateFramebuffers();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();

        Log(LogLevel::kInfo, "ForgeEngine Vulkan runtime initialized");
    }

    void MainLoop() {
        Log(LogLevel::kInfo, "Render loop started");

        bool manifest_loaded = false;
        if (!manifest_loaded) {
            ProcessManifestLoad();
            manifest_loaded = true;
        }

        std::uint32_t frame_counter = 0;
        auto fps_window_start = std::chrono::steady_clock::now();

        while (glfwWindowShouldClose(window_) == GLFW_FALSE) {
            glfwPollEvents();
            if (scene_loaded_from_manifest_) {
                RenderLoadedScene();
            }
            DrawFrame();

            ++frame_counter;
            const auto now = std::chrono::steady_clock::now();
            const double elapsed_seconds =
                std::chrono::duration_cast<std::chrono::duration<double>>(now - fps_window_start).count();

            if (elapsed_seconds >= 1.0) {
                const double fps = static_cast<double>(frame_counter) / elapsed_seconds;
                Log(LogLevel::kInfo, "FPS: " + std::to_string(static_cast<int>(fps)));
                frame_counter = 0;
                fps_window_start = now;
            }
        }

        vkDeviceWaitIdle(device_);
    }

    void ProcessManifestLoad() {
        if (manifest_load_attempted_ || !launch_options_.manifest_path.has_value()) {
            return;
        }

        manifest_load_attempted_ = true;
        const auto& manifest_path = launch_options_.manifest_path.value();
        Log(LogLevel::kInfo, "Loading integration manifest: " + manifest_path.string());

        LoadSceneFromManifest(manifest_path);
        scene_loaded_from_manifest_ = true;
        Log(LogLevel::kInfo, "Manifest load completed.");
    }

    void LoadSceneFromManifest(const std::filesystem::path& manifest_path) {
        const Json integration_manifest = ReadJsonFile(manifest_path, "integration manifest");
        const std::string schema = integration_manifest.value("schema", "");
        const auto targets = integration_manifest.value("integration_targets", std::vector<std::string>{});
        const std::string generated_at_utc = integration_manifest.value("generated_at_utc", "unknown");

        Log(LogLevel::kInfo, "Integration schema: " + schema);
        Log(LogLevel::kInfo, "Integration generated at: " + generated_at_utc);
        Log(LogLevel::kInfo, "Integration target count: " + std::to_string(targets.size()));
        for (const auto& target : targets) {
            Log(LogLevel::kInfo, "Integration target: " + target);
        }

        const std::filesystem::path prototype_root = integration_manifest.value("prototype_root", "");
        if (prototype_root.empty()) {
            throw std::runtime_error("Integration manifest is missing prototype_root.");
        }

        Log(LogLevel::kInfo, "Prototype root: " + prototype_root.string());

        const Json scene = ReadJsonFile(prototype_root / "scene" / "scene_scaffold.json", "scene scaffold");
        const Json prototype_manifest = ReadJsonFile(prototype_root / "prototype-manifest.json", "prototype manifest");
        const Json player_controller = ReadJsonFile(
            prototype_root / "scripts" / "player_controller.json",
            "player controller");

        Log(LogLevel::kInfo, "Project name: " + prototype_manifest.value("project_name", "unknown"));
        Log(LogLevel::kInfo, "Rendering mode: " + prototype_manifest.value("rendering", "unknown"));
        Log(LogLevel::kInfo, "Scope: " + prototype_manifest.value("scope", "unknown"));

        Log(LogLevel::kInfo, "Scene ID: " + scene.value("scene_id", "unknown"));
        Log(LogLevel::kInfo, "Scene world notes: " + scene.value("world_notes", ""));

        const Json spawn = scene.value("player_spawn", Json::object());
        Log(LogLevel::kInfo,
            "Player spawn: x=" + std::to_string(spawn.value("x", 0.0)) + ", y=" +
                std::to_string(spawn.value("y", 0.0)) + ", z=" + std::to_string(spawn.value("z", 0.0)));
        SpawnEntityFromManifest("player", "player_01", player_controller);

        const Json npcs = scene.value("npcs", Json::array());
        Log(LogLevel::kInfo, "NPC count from scene: " + std::to_string(npcs.size()));
        for (std::size_t i = 0; i < npcs.size(); ++i) {
            const auto& npc = npcs[i];
            SpawnEntityFromManifest("npc", npc.value("id", "npc_" + std::to_string(i + 1U)), npc);
        }

        const Json assets = scene.value("assets", Json::array());
        Log(LogLevel::kInfo, "Asset reference count from scene: " + std::to_string(assets.size()));

        generated_scene_state_ = GeneratedSceneState{};
        generated_scene_state_.entity_count = 0;
        generated_scene_state_.elapsed_time = 0.0F;

        GeneratedEntity player{};
        std::strncpy(player.id, "player_01", sizeof(player.id) - 1);
        player.x = static_cast<float>(spawn.value("x", 0.0));
        player.y = static_cast<float>(spawn.value("y", 0.0));
        player.size = 0.10F;
        player.r = 0.15F;
        player.g = 0.85F;
        player.b = 0.95F;
        player.active = 1;
        generated_scene_state_.entities[0] = player;
        generated_scene_state_.entity_count = 1;

        if (!npcs.empty()) {
            const Json first_npc = npcs[0];
            GeneratedEntity npc{};
            std::strncpy(npc.id, first_npc.value("id", "npc_01").c_str(), sizeof(npc.id) - 1);
            npc.x = static_cast<float>(first_npc.value("spawn_x", 0.35));
            npc.y = static_cast<float>(first_npc.value("spawn_y", -0.15));
            npc.size = 0.08F;
            npc.r = 0.95F;
            npc.g = 0.35F;
            npc.b = 0.30F;
            npc.active = 1;
            generated_scene_state_.entities[1] = npc;
            generated_scene_state_.entity_count = 2;
        }

        BuildAndLoadGeneratedGameplay(prototype_root);
        SyncRuntimeEntitiesFromGeneratedState();
    }

    void SpawnEntityFromManifest(const std::string& entity_type, const std::string& entity_id, const Json& payload) {
        const std::string schema = payload.value("schema", "unspecified");
        Log(LogLevel::kInfo,
            "SpawnEntityFromManifest() stub -> type=" + entity_type + ", id=" + entity_id +
                ", schema=" + schema);
    }

    void RenderLoadedScene() {
        if (render_loaded_scene_logged_) {
            return;
        }

        render_loaded_scene_logged_ = true;
        Log(LogLevel::kInfo,
            "RenderLoadedScene() stub active; Vulkan clear + present loop running while manifest data is loaded.");
    }

    void BuildAndLoadGeneratedGameplay(const std::filesystem::path& prototype_root) {
        generated_cpp_root_ = prototype_root / "generated" / "cpp";
        generated_build_root_ = prototype_root / "generated" / "build";
        if (!std::filesystem::exists(generated_cpp_root_ / "scene.cpp")) {
            Log(LogLevel::kWarn, "Generated scene.cpp not found; continuing with manifest-only stubs.");
            return;
        }
        if (!std::filesystem::exists(prototype_root / "generated" / "CMakeLists.txt")) {
            Log(LogLevel::kWarn, "Generated CMakeLists.txt not found; skipping generated build.");
            return;
        }

        const std::string configure_command =
            "cmake -S \"" + (prototype_root / "generated").string() + "\" -B \"" + generated_build_root_.string() + "\"";
        Log(LogLevel::kInfo, "Configuring generated gameplay build: " + configure_command);
        if (std::system(configure_command.c_str()) != 0) {
            throw std::runtime_error("Failed to configure generated gameplay build.");
        }

        const std::string build_command = "cmake --build \"" + generated_build_root_.string() + "\"";
        Log(LogLevel::kInfo, "Building generated gameplay executable: " + build_command);
        if (std::system(build_command.c_str()) != 0) {
            throw std::runtime_error("Failed to build generated gameplay executable.");
        }

#ifdef _WIN32
        generated_executable_path_ = generated_build_root_ / "Debug" / "generated_gameplay_runner.exe";
        if (!std::filesystem::exists(generated_executable_path_)) {
            generated_executable_path_ = generated_build_root_ / "Release" / "generated_gameplay_runner.exe";
        }
#else
        generated_executable_path_ = generated_build_root_ / "generated_gameplay_runner";
#endif

        if (!std::filesystem::exists(generated_executable_path_)) {
            throw std::runtime_error("Generated gameplay executable missing: " + generated_executable_path_.string());
        }

        if (!generated_runner_launched_) {
            generated_runner_launched_ = true;
            const std::string launch_command = "\"" + generated_executable_path_.string() + "\"";
            std::thread([launch_command]() {
                std::system(launch_command.c_str());
            }).detach();
            Log(LogLevel::kInfo, "Generated gameplay runner launched: " + generated_executable_path_.string());
        }
    }

    void SyncRuntimeEntitiesFromGeneratedState() {
        runtime_entities_.clear();
        const int clamped_count = std::max(0, std::min(generated_scene_state_.entity_count, kGeneratedSceneEntityLimit));
        runtime_entities_.reserve(static_cast<std::size_t>(clamped_count));
        for (int index = 0; index < clamped_count; ++index) {
            const GeneratedEntity& source = generated_scene_state_.entities[static_cast<std::size_t>(index)];
            RuntimeEntity entity;
            entity.id = source.id;
            entity.position.x = source.x;
            entity.position.y = source.y;
            entity.velocity_x = source.vx;
            entity.velocity_y = source.vy;
            entity.render.size = source.size;
            entity.render.r = source.r;
            entity.render.g = source.g;
            entity.render.b = source.b;
            entity.active = source.active != 0;
            runtime_entities_.push_back(entity);
        }
    }

    void UpdateGeneratedSceneState() {
        const auto now = std::chrono::steady_clock::now();
        float dt_seconds = std::chrono::duration_cast<std::chrono::duration<float>>(now - last_update_time_).count();
        last_update_time_ = now;
        if (dt_seconds <= 0.0F || dt_seconds > 0.1F) {
            dt_seconds = 1.0F / 60.0F;
        }

        generated_scene_state_.elapsed_time += dt_seconds;
        if (generated_scene_state_.entity_count > 0) {
            GeneratedEntity& player = generated_scene_state_.entities[0];
            const float wave = std::sin(generated_scene_state_.elapsed_time * 1.6F);
            player.vx = 0.42F;
            player.vy = wave * 0.35F;
            player.x += player.vx * dt_seconds;
            player.y += player.vy * dt_seconds;
            if (player.x > 0.82F) {
                player.x = -0.82F;
            }
            player.g = 0.45F + 0.40F * (wave + 1.0F) * 0.5F;
        }
        if (generated_scene_state_.entity_count > 1) {
            GeneratedEntity& npc = generated_scene_state_.entities[1];
            const float angle = generated_scene_state_.elapsed_time * 0.85F;
            npc.x = std::cos(angle) * 0.34F;
            npc.y = std::sin(angle) * 0.34F;
            npc.r = 0.60F + 0.35F * (std::sin(angle * 1.5F) + 1.0F) * 0.5F;
            npc.b = 0.45F + 0.45F * (std::cos(angle * 1.2F) + 1.0F) * 0.5F;
        }
        SyncRuntimeEntitiesFromGeneratedState();
    }

    void RecordCommandBuffer(std::uint32_t image_index) {
        if (vkResetCommandBuffer(command_buffers_[image_index], 0) != VK_SUCCESS) {
            throw std::runtime_error("Failed to reset command buffer.");
        }

        VkCommandBufferBeginInfo begin_info{};
        begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        if (vkBeginCommandBuffer(command_buffers_[image_index], &begin_info) != VK_SUCCESS) {
            throw std::runtime_error("Failed to begin recording command buffer.");
        }

        VkRenderPassBeginInfo render_pass_begin_info{};
        render_pass_begin_info.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        render_pass_begin_info.renderPass = render_pass_;
        render_pass_begin_info.framebuffer = swap_chain_framebuffers_[image_index];
        render_pass_begin_info.renderArea.offset = {0, 0};
        render_pass_begin_info.renderArea.extent = swap_chain_extent_;

        const float pulse = 0.5F + 0.5F * std::sin(generated_scene_state_.elapsed_time * 0.6F);
        VkClearValue clear_color = {{{0.05F + (0.08F * pulse), 0.06F, 0.10F + (0.10F * pulse), 1.0F}}};
        render_pass_begin_info.clearValueCount = 1;
        render_pass_begin_info.pClearValues = &clear_color;

        vkCmdBeginRenderPass(command_buffers_[image_index], &render_pass_begin_info, VK_SUBPASS_CONTENTS_INLINE);

        for (const RuntimeEntity& entity : runtime_entities_) {
            if (!entity.active) {
                continue;
            }

            const float normalized_x = std::clamp(entity.position.x, -0.95F, 0.95F);
            const float normalized_y = std::clamp(entity.position.y, -0.95F, 0.95F);
            const std::uint32_t rect_width = static_cast<std::uint32_t>(
                std::max(4.0F, entity.render.size * static_cast<float>(swap_chain_extent_.width)));
            const std::uint32_t rect_height = static_cast<std::uint32_t>(
                std::max(4.0F, entity.render.size * static_cast<float>(swap_chain_extent_.height)));

            const int center_x = static_cast<int>(((normalized_x + 1.0F) * 0.5F) * static_cast<float>(swap_chain_extent_.width));
            const int center_y = static_cast<int>(((1.0F - (normalized_y + 1.0F) * 0.5F)) * static_cast<float>(swap_chain_extent_.height));

            const std::int32_t offset_x = static_cast<std::int32_t>(std::max(0, center_x - static_cast<int>(rect_width / 2U)));
            const std::int32_t offset_y = static_cast<std::int32_t>(std::max(0, center_y - static_cast<int>(rect_height / 2U)));

            VkClearAttachment attachment{};
            attachment.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            attachment.colorAttachment = 0;
            attachment.clearValue.color.float32[0] = std::clamp(entity.render.r, 0.0F, 1.0F);
            attachment.clearValue.color.float32[1] = std::clamp(entity.render.g, 0.0F, 1.0F);
            attachment.clearValue.color.float32[2] = std::clamp(entity.render.b, 0.0F, 1.0F);
            attachment.clearValue.color.float32[3] = 1.0F;

            VkClearRect clear_rect{};
            clear_rect.baseArrayLayer = 0;
            clear_rect.layerCount = 1;
            clear_rect.rect.offset = {offset_x, offset_y};
            clear_rect.rect.extent = {rect_width, rect_height};
            vkCmdClearAttachments(command_buffers_[image_index], 1, &attachment, 1, &clear_rect);
        }

        vkCmdEndRenderPass(command_buffers_[image_index]);

        if (vkEndCommandBuffer(command_buffers_[image_index]) != VK_SUCCESS) {
            throw std::runtime_error("Failed to finalize command buffer.");
        }
    }

    void CleanupSwapChain() {
        for (auto framebuffer : swap_chain_framebuffers_) {
            vkDestroyFramebuffer(device_, framebuffer, nullptr);
        }
        swap_chain_framebuffers_.clear();

        if (!command_buffers_.empty()) {
            vkFreeCommandBuffers(
                device_,
                command_pool_,
                static_cast<std::uint32_t>(command_buffers_.size()),
                command_buffers_.data());
            command_buffers_.clear();
        }

        if (render_pass_ != VK_NULL_HANDLE) {
            vkDestroyRenderPass(device_, render_pass_, nullptr);
            render_pass_ = VK_NULL_HANDLE;
        }

        for (auto image_view : swap_chain_image_views_) {
            vkDestroyImageView(device_, image_view, nullptr);
        }
        swap_chain_image_views_.clear();

        if (swap_chain_ != VK_NULL_HANDLE) {
            vkDestroySwapchainKHR(device_, swap_chain_, nullptr);
            swap_chain_ = VK_NULL_HANDLE;
        }
    }

    void Cleanup() {
        CleanupSwapChain();

        for (std::size_t i = 0; i < image_available_semaphores_.size(); ++i) {
            vkDestroySemaphore(device_, image_available_semaphores_[i], nullptr);
            vkDestroySemaphore(device_, render_finished_semaphores_[i], nullptr);
            vkDestroyFence(device_, in_flight_fences_[i], nullptr);
        }

        if (command_pool_ != VK_NULL_HANDLE) {
            vkDestroyCommandPool(device_, command_pool_, nullptr);
        }

        if (device_ != VK_NULL_HANDLE) {
            vkDestroyDevice(device_, nullptr);
        }

        if (surface_ != VK_NULL_HANDLE) {
            vkDestroySurfaceKHR(instance_, surface_, nullptr);
        }

        if (instance_ != VK_NULL_HANDLE) {
            vkDestroyInstance(instance_, nullptr);
        }

        if (window_ != nullptr) {
            glfwDestroyWindow(window_);
        }

        glfwTerminate();
        Log(LogLevel::kInfo, "Runtime shutdown complete.");
    }

    void RecreateSwapChain() {
        int width = 0;
        int height = 0;
        glfwGetFramebufferSize(window_, &width, &height);
        while (width == 0 || height == 0) {
            glfwGetFramebufferSize(window_, &width, &height);
            glfwWaitEvents();
        }

        vkDeviceWaitIdle(device_);

        CleanupSwapChain();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateFramebuffers();
        CreateCommandBuffers();
    }

    void CreateInstance() {
        VkApplicationInfo application_info{};
        application_info.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
        application_info.pApplicationName = "ForgeEngineRuntime";
        application_info.applicationVersion = VK_MAKE_VERSION(0, 1, 0);
        application_info.pEngineName = "ForgeEngine";
        application_info.engineVersion = VK_MAKE_VERSION(0, 1, 0);
        application_info.apiVersion = VK_API_VERSION_1_0;

        std::uint32_t extension_count = 0;
        const char** extensions = glfwGetRequiredInstanceExtensions(&extension_count);
        if (extensions == nullptr || extension_count == 0) {
            throw std::runtime_error("GLFW did not return required Vulkan extensions.");
        }

        VkInstanceCreateInfo create_info{};
        create_info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
        create_info.pApplicationInfo = &application_info;
        create_info.enabledExtensionCount = extension_count;
        create_info.ppEnabledExtensionNames = extensions;
        create_info.enabledLayerCount = 0;

        if (vkCreateInstance(&create_info, nullptr, &instance_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create Vulkan instance.");
        }
    }

    void CreateSurface() {
        if (glfwCreateWindowSurface(instance_, window_, nullptr, &surface_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create Vulkan surface.");
        }
    }

    [[nodiscard]] QueueFamilyIndices FindQueueFamilies(VkPhysicalDevice device) const {
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

    [[nodiscard]] bool CheckDeviceExtensionSupport(VkPhysicalDevice device) const {
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

    [[nodiscard]] SwapChainSupportDetails QuerySwapChainSupport(VkPhysicalDevice device) const {
        SwapChainSupportDetails details;

        vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device, surface_, &details.capabilities);

        std::uint32_t format_count = 0;
        vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface_, &format_count, nullptr);
        if (format_count != 0) {
            details.formats.resize(format_count);
            vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface_, &format_count, details.formats.data());
        }

        std::uint32_t present_mode_count = 0;
        vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface_, &present_mode_count, nullptr);
        if (present_mode_count != 0) {
            details.present_modes.resize(present_mode_count);
            vkGetPhysicalDeviceSurfacePresentModesKHR(
                device,
                surface_,
                &present_mode_count,
                details.present_modes.data());
        }

        return details;
    }

    [[nodiscard]] bool IsDeviceSuitable(VkPhysicalDevice device) const {
        const QueueFamilyIndices indices = FindQueueFamilies(device);
        const bool extensions_supported = CheckDeviceExtensionSupport(device);

        bool swap_chain_adequate = false;
        if (extensions_supported) {
            const SwapChainSupportDetails swap_chain_support = QuerySwapChainSupport(device);
            swap_chain_adequate = !swap_chain_support.formats.empty() && !swap_chain_support.present_modes.empty();
        }

        return indices.IsComplete() && extensions_supported && swap_chain_adequate;
    }

    void PickPhysicalDevice() {
        std::uint32_t device_count = 0;
        vkEnumeratePhysicalDevices(instance_, &device_count, nullptr);
        if (device_count == 0) {
            throw std::runtime_error("No Vulkan-capable GPUs found.");
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
            throw std::runtime_error("No suitable Vulkan GPU found.");
        }
    }

    void CreateLogicalDevice() {
        const QueueFamilyIndices indices = FindQueueFamilies(physical_device_);

        std::set<std::uint32_t> unique_queue_families = {
            indices.graphics_family.value(),
            indices.present_family.value(),
        };

        float queue_priority = 1.0F;
        std::vector<VkDeviceQueueCreateInfo> queue_create_infos;
        queue_create_infos.reserve(unique_queue_families.size());

        for (const auto queue_family : unique_queue_families) {
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

        if (vkCreateDevice(physical_device_, &create_info, nullptr, &device_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create logical Vulkan device.");
        }

        vkGetDeviceQueue(device_, indices.graphics_family.value(), 0, &graphics_queue_);
        vkGetDeviceQueue(device_, indices.present_family.value(), 0, &present_queue_);
    }

    [[nodiscard]] VkSurfaceFormatKHR ChooseSwapSurfaceFormat(
        const std::vector<VkSurfaceFormatKHR>& available_formats) const {
        for (const auto& available_format : available_formats) {
            if (available_format.format == VK_FORMAT_B8G8R8A8_UNORM &&
                available_format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR) {
                return available_format;
            }
        }
        return available_formats[0];
    }

    [[nodiscard]] VkPresentModeKHR ChooseSwapPresentMode(
        const std::vector<VkPresentModeKHR>& available_present_modes) const {
        for (const auto& available_present_mode : available_present_modes) {
            if (available_present_mode == VK_PRESENT_MODE_MAILBOX_KHR) {
                return available_present_mode;
            }
        }
        return VK_PRESENT_MODE_FIFO_KHR;
    }

    [[nodiscard]] VkExtent2D ChooseSwapExtent(const VkSurfaceCapabilitiesKHR& capabilities) const {
        if (capabilities.currentExtent.width != UINT32_MAX) {
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

    void CreateSwapChain() {
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
        create_info.oldSwapchain = VK_NULL_HANDLE;

        if (vkCreateSwapchainKHR(device_, &create_info, nullptr, &swap_chain_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create Vulkan swap chain.");
        }

        vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, nullptr);
        swap_chain_images_.resize(image_count);
        vkGetSwapchainImagesKHR(device_, swap_chain_, &image_count, swap_chain_images_.data());

        swap_chain_image_format_ = surface_format.format;
        swap_chain_extent_ = extent;
    }

    void CreateImageViews() {
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

            if (vkCreateImageView(device_, &create_info, nullptr, &swap_chain_image_views_[i]) != VK_SUCCESS) {
                throw std::runtime_error("Failed to create swap chain image view.");
            }
        }
    }

    void CreateRenderPass() {
        VkAttachmentDescription color_attachment{};
        color_attachment.format = swap_chain_image_format_;
        color_attachment.samples = VK_SAMPLE_COUNT_1_BIT;
        color_attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
        color_attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        color_attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        color_attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        color_attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        color_attachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

        VkAttachmentReference color_attachment_reference{};
        color_attachment_reference.attachment = 0;
        color_attachment_reference.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

        VkSubpassDescription subpass{};
        subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        subpass.colorAttachmentCount = 1;
        subpass.pColorAttachments = &color_attachment_reference;

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

        if (vkCreateRenderPass(device_, &render_pass_info, nullptr, &render_pass_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create render pass.");
        }
    }

    void CreateFramebuffers() {
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

            if (vkCreateFramebuffer(device_, &framebuffer_info, nullptr, &swap_chain_framebuffers_[i]) != VK_SUCCESS) {
                throw std::runtime_error("Failed to create framebuffer.");
            }
        }
    }

    void CreateCommandPool() {
        const QueueFamilyIndices queue_family_indices = FindQueueFamilies(physical_device_);

        VkCommandPoolCreateInfo pool_info{};
        pool_info.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        pool_info.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        pool_info.queueFamilyIndex = queue_family_indices.graphics_family.value();

        if (vkCreateCommandPool(device_, &pool_info, nullptr, &command_pool_) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create command pool.");
        }
    }

    void CreateCommandBuffers() {
        command_buffers_.resize(swap_chain_framebuffers_.size());

        VkCommandBufferAllocateInfo allocate_info{};
        allocate_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        allocate_info.commandPool = command_pool_;
        allocate_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        allocate_info.commandBufferCount = static_cast<std::uint32_t>(command_buffers_.size());

        if (vkAllocateCommandBuffers(device_, &allocate_info, command_buffers_.data()) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate command buffers.");
        }
    }

    void CreateSyncObjects() {
        image_available_semaphores_.resize(kMaxFramesInFlight);
        render_finished_semaphores_.resize(kMaxFramesInFlight);
        in_flight_fences_.resize(kMaxFramesInFlight);

        VkSemaphoreCreateInfo semaphore_info{};
        semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

        VkFenceCreateInfo fence_info{};
        fence_info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        fence_info.flags = VK_FENCE_CREATE_SIGNALED_BIT;

        for (std::size_t i = 0; i < kMaxFramesInFlight; ++i) {
            if (vkCreateSemaphore(device_, &semaphore_info, nullptr, &image_available_semaphores_[i]) != VK_SUCCESS ||
                vkCreateSemaphore(device_, &semaphore_info, nullptr, &render_finished_semaphores_[i]) != VK_SUCCESS ||
                vkCreateFence(device_, &fence_info, nullptr, &in_flight_fences_[i]) != VK_SUCCESS) {
                throw std::runtime_error("Failed to create synchronization objects.");
            }
        }
    }

    void DrawFrame() {
        vkWaitForFences(device_, 1, &in_flight_fences_[current_frame_], VK_TRUE, UINT64_MAX);

        std::uint32_t image_index = 0;
        VkResult acquire_result = vkAcquireNextImageKHR(
            device_,
            swap_chain_,
            UINT64_MAX,
            image_available_semaphores_[current_frame_],
            VK_NULL_HANDLE,
            &image_index);

        if (acquire_result == VK_ERROR_OUT_OF_DATE_KHR) {
            RecreateSwapChain();
            return;
        }
        if (acquire_result != VK_SUCCESS && acquire_result != VK_SUBOPTIMAL_KHR) {
            throw std::runtime_error("Failed to acquire swap chain image.");
        }

        vkResetFences(device_, 1, &in_flight_fences_[current_frame_]);

        if (scene_loaded_from_manifest_) {
            UpdateGeneratedSceneState();
        }
        RecordCommandBuffer(image_index);

        VkSemaphore wait_semaphores[] = {image_available_semaphores_[current_frame_]};
        VkPipelineStageFlags wait_stages[] = {VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT};
        VkSemaphore signal_semaphores[] = {render_finished_semaphores_[current_frame_]};

        VkSubmitInfo submit_info{};
        submit_info.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        submit_info.waitSemaphoreCount = 1;
        submit_info.pWaitSemaphores = wait_semaphores;
        submit_info.pWaitDstStageMask = wait_stages;
        submit_info.commandBufferCount = 1;
        submit_info.pCommandBuffers = &command_buffers_[image_index];
        submit_info.signalSemaphoreCount = 1;
        submit_info.pSignalSemaphores = signal_semaphores;

        if (vkQueueSubmit(graphics_queue_, 1, &submit_info, in_flight_fences_[current_frame_]) != VK_SUCCESS) {
            throw std::runtime_error("Failed to submit draw command buffer.");
        }

        VkSwapchainKHR swap_chains[] = {swap_chain_};

        VkPresentInfoKHR present_info{};
        present_info.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
        present_info.waitSemaphoreCount = 1;
        present_info.pWaitSemaphores = signal_semaphores;
        present_info.swapchainCount = 1;
        present_info.pSwapchains = swap_chains;
        present_info.pImageIndices = &image_index;

        VkResult present_result = vkQueuePresentKHR(present_queue_, &present_info);

        if (present_result == VK_ERROR_OUT_OF_DATE_KHR || present_result == VK_SUBOPTIMAL_KHR || framebuffer_resized_) {
            framebuffer_resized_ = false;
            RecreateSwapChain();
        } else if (present_result != VK_SUCCESS) {
            throw std::runtime_error("Failed to present swap chain image.");
        }

        current_frame_ = (current_frame_ + 1) % kMaxFramesInFlight;
    }
};

}  // namespace

int main(int argc, char* argv[]) {
    try {
        RuntimeLaunchOptions launch_options = ParseRuntimeLaunchOptions(argc, argv);
        Log(LogLevel::kInfo, "Starting ForgeEngine Vulkan runtime skeleton.");
        if (launch_options.manifest_path.has_value()) {
            Log(LogLevel::kInfo, "Manifest argument detected: " + launch_options.manifest_path->string());
        } else {
            Log(LogLevel::kInfo, "No manifest provided. Runtime will run without pipeline data.");
        }
        RuntimeApp app(std::move(launch_options));
        app.Run();
        return EXIT_SUCCESS;
    } catch (const std::exception& exception) {
        Log(LogLevel::kError, exception.what());
        return EXIT_FAILURE;
    }
}
