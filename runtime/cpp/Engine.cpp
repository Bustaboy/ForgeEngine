#include "Engine.h"

#include "DialogSystem.h"
#include "DialogEvolutionSystem.h"
#include "EconomySystem.h"
#include "FactionSystem.h"
#include "InventorySystem.h"
#include "Logger.h"
#include "CoCreatorSystem.h"
#include "RelationshipSystem.h"
#include "SceneLoader.h"
#include "StorySystem.h"
#include "BuildingSystem.h"
#include "NarratorSystem.h"
#include "NPCController.h"

#include <GLFW/glfw3.h>
#include <glm/geometric.hpp>
#include <glm/gtc/matrix_inverse.hpp>
#include <glm/vec2.hpp>
#include <glm/vec4.hpp>
#include <algorithm>
#include <array>
#include <cctype>
#include <chrono>
#include <iostream>
#include <sstream>
#include <string>
#include <thread>

namespace {
bool TryComputeCursorRay(GLFWwindow* window, const Camera& camera, glm::vec3& out_ray_direction) {
    int framebuffer_width = 0;
    int framebuffer_height = 0;
    glfwGetFramebufferSize(window, &framebuffer_width, &framebuffer_height);
    if (framebuffer_width <= 0 || framebuffer_height <= 0) {
        return false;
    }

    double cursor_x = 0.0;
    double cursor_y = 0.0;
    glfwGetCursorPos(window, &cursor_x, &cursor_y);

    const float ndc_x = (2.0F * static_cast<float>(cursor_x)) / static_cast<float>(framebuffer_width) - 1.0F;
    const float ndc_y = 1.0F - (2.0F * static_cast<float>(cursor_y)) / static_cast<float>(framebuffer_height);
    const glm::mat4 inverse_view_projection = glm::inverse(camera.GetProjectionMatrix() * camera.GetViewMatrix());
    const glm::vec4 near_world = inverse_view_projection * glm::vec4(ndc_x, ndc_y, 0.0F, 1.0F);
    const glm::vec4 far_world = inverse_view_projection * glm::vec4(ndc_x, ndc_y, 1.0F, 1.0F);
    if (near_world.w == 0.0F || far_world.w == 0.0F) {
        return false;
    }

    const glm::vec3 near_point = glm::vec3(near_world) / near_world.w;
    const glm::vec3 far_point = glm::vec3(far_world) / far_world.w;
    glm::vec3 ray_direction = far_point - near_point;
    if (glm::dot(ray_direction, ray_direction) <= 0.0F) {
        return false;
    }

    out_ray_direction = glm::normalize(ray_direction);
    return true;
}

void ProcessConsoleCommands(Scene& scene) {
    std::streambuf* input_buffer = std::cin.rdbuf();
    if (input_buffer == nullptr || input_buffer->in_avail() <= 0) {
        return;
    }

    std::string command_line;
    if (!std::getline(std::cin, command_line) || command_line.empty() || command_line[0] != '/') {
        return;
    }

    std::istringstream parser(command_line);
    std::string command;
    parser >> command;

    if (command == "/give") {
        std::string item;
        int amount = 0;
        parser >> item >> amount;
        if (InventorySystem::AddItem(scene.player_inventory, item, amount)) {
            GF_LOG_INFO("Given " + std::to_string(amount) + " " + item + ".");
            GF_LOG_INFO(InventorySystem::InventorySummary(scene.player_inventory));
        } else {
            GF_LOG_INFO("Usage: /give <item> <amount>");
        }
        return;
    }

    if (command == "/craft") {
        std::string recipe_name;
        parser >> recipe_name;
        std::string error_message;
        if (InventorySystem::CraftRecipe(scene.player_inventory, recipe_name, error_message)) {
            const Recipe* recipe = InventorySystem::FindRecipeByName(recipe_name);
            if (recipe != nullptr) {
                GF_LOG_INFO(
                    "Crafted " + std::to_string(recipe->output_quantity) + " " + recipe->output_item + " from recipe " +
                    recipe_name + ".");
            } else {
                GF_LOG_INFO("Crafted recipe: " + recipe_name + ".");
            }
            GF_LOG_INFO(InventorySystem::InventorySummary(scene.player_inventory));
        } else {
            GF_LOG_INFO("Craft failed: " + error_message);
        }
        return;
    }

    if (command == "/inventory") {
        GF_LOG_INFO(InventorySystem::InventorySummary(scene.player_inventory));
        return;
    }

    if (command == "/recipes") {
        for (const Recipe& recipe : InventorySystem::DefaultRecipes()) {
            GF_LOG_INFO("Recipe: " + recipe.name + " -> " + recipe.output_item + " x" + std::to_string(recipe.output_quantity));
        }
        return;
    }

    if (command == "/factions") {
        FactionSystem::EnsureSceneFactions(scene);
        for (const auto& [faction_id, faction] : scene.factions) {
            const float reputation = FactionSystem::GetReputation(scene, faction_id);
            GF_LOG_INFO(
                faction.display_name + " [" + faction.category + "] rep=" + std::to_string(static_cast<int>(reputation)) +
                " build>=" + std::to_string(static_cast<int>(faction.min_reputation_to_build)));
        }
        return;
    }

    if (command == "/rep") {
        std::string faction_id;
        float delta = 0.0F;
        parser >> faction_id >> delta;
        if (!faction_id.empty() && std::abs(delta) > 0.0001F) {
            FactionSystem::EnsureSceneFactions(scene);
            FactionSystem::AddPlayerReputation(scene, faction_id, delta, "console");
        } else {
            GF_LOG_INFO("Usage: /rep <faction_id> <delta>");
        }
        return;
    }

    if (command == "/relationship") {
        std::string mode;
        parser >> mode;
        if (mode == "list") {
            RelationshipSystem::EnsureSceneRelationships(scene);
            if (scene.relationships.empty()) {
                GF_LOG_INFO("No relationship profiles.");
                return;
            }
            for (const auto& [npc_id, _] : scene.relationships) {
                (void)_;
                GF_LOG_INFO(RelationshipSystem::Summary(scene, npc_id));
            }
            return;
        }
        if (mode == "set" || mode == "add") {
            std::uint64_t npc_id = 0;
            std::string dimension;
            float value = 0.0F;
            parser >> npc_id >> dimension >> value;
            if (npc_id == 0 || dimension.empty()) {
                GF_LOG_INFO("Usage: /relationship list | /relationship <npc_id> | /relationship set|add <npc_id> trust|respect|grudge|debt|loyalty <value>");
                return;
            }
            const bool updated = RelationshipSystem::SetDimension(scene, npc_id, dimension, value, mode == "set");
            GF_LOG_INFO(updated ? RelationshipSystem::Summary(scene, npc_id) : "Invalid relationship dimension.");
            return;
        }
        std::uint64_t npc_id = 0;
        if (!mode.empty()) {
            try {
                npc_id = std::stoull(mode);
            } catch (const std::exception&) {
                npc_id = 0;
            }
        }
        if (npc_id == 0) {
            GF_LOG_INFO("Usage: /relationship list | /relationship <npc_id> | /relationship set|add <npc_id> trust|respect|grudge|debt|loyalty <value>");
            return;
        }
        GF_LOG_INFO(RelationshipSystem::Summary(scene, npc_id));
        return;
    }

    if (command == "/evolve_dialog") {
        std::uint64_t npc_id = scene.active_dialog_npc_id;
        parser >> npc_id;
        if (npc_id == 0 && !scene.entities.empty()) {
            npc_id = scene.entities.front().id;
        }
        const bool evolved = DialogEvolutionSystem::EvolveNpcDialog(scene, npc_id, "console_trigger", true);
        GF_LOG_INFO(evolved ? "Dialog evolution applied." : "Dialog evolution failed. Usage: /evolve_dialog [npc_id]");
        return;
    }

    if (command == "/economy") {
        EconomySystem::EnsureDefaults(scene);
        GF_LOG_INFO(EconomySystem::EconomySummary(scene));
        for (const EconomyTradeRoute& route : scene.economy.trade_routes) {
            GF_LOG_INFO("Route: " + EconomySystem::RouteSummary(route));
        }
        return;
    }

    if (command == "/trade") {
        std::string arg0;
        parser >> arg0;
        if (arg0.empty()) {
            GF_LOG_INFO("Usage: /trade <route_id> [raid] | /trade buy|sell <item> <qty> [faction_id]");
            return;
        }

        if (arg0 == "buy" || arg0 == "sell") {
            std::string item;
            int quantity = 0;
            std::string faction_id;
            parser >> item >> quantity >> faction_id;
            float faction_reputation = 0.0F;
            if (!faction_id.empty()) {
                faction_reputation = FactionSystem::GetReputation(scene, faction_id);
            }
            std::string trade_summary;
            const bool ok = InventorySystem::TradeWithSettlementMarket(
                scene,
                item,
                quantity,
                arg0 == "buy",
                faction_reputation,
                trade_summary);
            GF_LOG_INFO(trade_summary);
            if (ok) {
                GF_LOG_INFO(InventorySystem::InventorySummary(scene.player_inventory));
            }
            return;
        }

        const std::string route_id = arg0;
        std::string route_action;
        parser >> route_action;
        if (route_action == "raid") {
            EconomySystem::RegisterRouteRaid(scene, route_id);
            GF_LOG_INFO("Route raided: " + route_id);
            GF_LOG_INFO(EconomySystem::EconomySummary(scene));
            return;
        }

        bool found = false;
        for (const EconomyTradeRoute& route : scene.economy.trade_routes) {
            if (route.route_id == route_id) {
                found = true;
                break;
            }
        }
        if (!found) {
            GF_LOG_INFO("Unknown route id: " + route_id);
            return;
        }

        EconomySystem::RunTradeTick(scene);
        GF_LOG_INFO("Trade tick applied for route context: " + route_id);
        GF_LOG_INFO(EconomySystem::EconomySummary(scene));
        return;
    }

    if (command == "/story_event") {
        std::string event_id;
        parser >> event_id;
        if (event_id.empty()) {
            GF_LOG_INFO("Usage: /story_event <event_id>");
            return;
        }
        const bool applied = StorySystem::TriggerEventById(scene, event_id.c_str());
        GF_LOG_INFO(applied ? ("Story event triggered: " + event_id) : ("Story event not found/applied: " + event_id));
        return;
    }

    if (command == "/narrate") {
        std::string line;
        std::getline(parser, line);
        line.erase(line.begin(), std::find_if(line.begin(), line.end(), [](unsigned char ch) {
            return std::isspace(ch) == 0;
        }));
        if (!NarratorSystem::QueueLine(scene, line, "console")) {
            GF_LOG_INFO("Usage: /narrate <text>");
            return;
        }
        GF_LOG_INFO("Narrator line queued.");
        return;
    }

    if (command == "/npc_activity") {
        std::uint64_t npc_id = 0;
        std::string activity;
        std::string location;
        float hours = 0.0F;
        parser >> npc_id >> activity >> location >> hours;
        if (npc_id == 0 || activity.empty()) {
            GF_LOG_INFO("Usage: /npc_activity <npc_id> <activity> [location] [hours]");
            return;
        }

        const bool applied = NPCController::ForceActivity(scene, npc_id, activity, location, hours);
        GF_LOG_INFO(applied ? "NPC activity forced." : "NPC not found or invalid activity.");
        return;
    }

    if (command == "/npc_schedule") {
        std::string mode;
        parser >> mode;
        if (mode == "list") {
            for (const Entity& entity : scene.entities) {
                if (entity.buildable.IsValid()) {
                    continue;
                }
                GF_LOG_INFO(
                    "NPC " + std::to_string(entity.id) + " job=" + entity.schedule.job_id + " activity=" +
                    entity.schedule.current_activity + " location=" + entity.schedule.current_location + " home=(" +
                    std::to_string(entity.schedule.home_position.x) + "," + std::to_string(entity.schedule.home_position.z) +
                    ") work=(" + std::to_string(entity.schedule.workplace_position.x) + "," +
                    std::to_string(entity.schedule.workplace_position.z) + ")");
            }
            return;
        }

        std::uint64_t npc_id = 0;
        try {
            npc_id = mode.empty() ? 0ULL : std::stoull(mode);
        } catch (const std::exception&) {
            npc_id = 0;
        }
        if (npc_id == 0) {
            GF_LOG_INFO("Usage: /npc_schedule list | /npc_schedule <npc_id>");
            return;
        }

        const auto npc_it = std::find_if(scene.entities.begin(), scene.entities.end(), [&](const Entity& entity) {
            return entity.id == npc_id;
        });
        if (npc_it == scene.entities.end()) {
            GF_LOG_INFO("NPC not found: " + std::to_string(npc_id));
            return;
        }

        GF_LOG_INFO(
            "NPC " + std::to_string(npc_it->id) + " needs[hunger=" + std::to_string(static_cast<int>(npc_it->needs.hunger)) +
            ", energy=" + std::to_string(static_cast<int>(npc_it->needs.energy)) + ", social=" +
            std::to_string(static_cast<int>(npc_it->needs.social)) + ", fun=" +
            std::to_string(static_cast<int>(npc_it->needs.fun)) + "]");
        for (const ScheduleEntry& entry : npc_it->schedule.daily_schedule) {
            GF_LOG_INFO(
                "  " + std::to_string(entry.start_minute) + "-" + std::to_string(entry.end_minute) + " " +
                entry.activity + " @ " + entry.location);
        }
        return;
    }

    GF_LOG_INFO("Unknown command. Available: /give /craft /inventory /recipes /factions /rep /relationship /evolve_dialog /economy /trade /story_event /narrate /npc_schedule /npc_activity");
}
}  // namespace

void Engine::Run() {
    Init();

    InputManager input{};
    input.AttachWindow(renderer_.GetWindow());

    constexpr double fixed_dt = 1.0 / 60.0;
    double accumulator = 0.0;
    auto previous_time = std::chrono::steady_clock::now();

    while (!renderer_.ShouldClose()) {
        timer_.BeginFrame();
        renderer_.PollEvents();
        input.BeginFrame();

        if (input.IsKeyPressed(GLFW_KEY_ESCAPE)) {
            const bool saved = scene_.Save(scene_path_);
            if (saved) {
                GF_LOG_INFO("Scene saved: " + scene_path_);
            } else {
                GF_LOG_INFO("Scene save failed: " + scene_path_);
            }
            break;
        }

        const auto now = std::chrono::steady_clock::now();
        const std::chrono::duration<double> frame_delta = now - previous_time;
        previous_time = now;
        accumulator += frame_delta.count();

        while (accumulator >= fixed_dt) {
            Update(static_cast<float>(fixed_dt), input);
            accumulator -= fixed_dt;
        }

        renderer_.RenderFrame(scene_, camera_);

        if (timer_.ShouldUpdateFps()) {
            const std::string day_clock = timer_.DayClockText(scene_.day_progress, scene_.day_count);
            renderer_.DrawFPSOverlay(static_cast<float>(timer_.Fps()), day_clock);
            renderer_.SetWindowTitle(
                "ForgeEngine Runtime (Vulkan-first) | FPS: " + std::to_string(timer_.Fps()) +
                " | Frame: " + timer_.FrameTimeMsText() + "ms | " + day_clock);
            GF_LOG_INFO("Day time: " + std::to_string(scene_.day_progress));
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    Shutdown();
}

void Engine::Init() {
    Logger::Init();
    GF_LOG_INFO("ForgeEngine Vulkan runtime initialized");

    if (SceneLoader::Load(scene_path_, scene_)) {
        GF_LOG_INFO("Loaded scene: " + scene_path_);
    } else {
        SeedFallbackScene();
        GF_LOG_INFO("Using fallback scene entities");
    }

    renderer_.Init();
    camera_.position = {0.0F, 0.0F, 3.0F};
    camera_.yaw = -90.0F;
    camera_.pitch = 0.0F;
    camera_.aspect_ratio = renderer_.GetAspectRatio();
    camera_.smoothedYaw = camera_.yaw;
    camera_.smoothedPitch = camera_.pitch;
    camera_.smoothedPosition = camera_.position;
    GF_LOG_INFO("Render loop started");
}

void Engine::Update(float dt_seconds, const InputManager& input) {
    constexpr float look_sensitivity = 0.09F;
    constexpr float move_speed = 3.5F;
    constexpr float smoothing = 10.0F;

    const glm::vec2 mouse_delta = input.MouseDelta();
    camera_.yaw += mouse_delta.x * look_sensitivity;
    camera_.pitch -= mouse_delta.y * look_sensitivity;
    camera_.pitch = std::clamp(camera_.pitch, -89.0F, 89.0F);

    const glm::vec3 forward = camera_.Forward();
    const glm::vec3 forward_flat = glm::normalize(glm::vec3(forward.x, 0.0F, forward.z));
    const glm::vec3 right = glm::normalize(glm::cross(forward_flat, glm::vec3(0.0F, 1.0F, 0.0F)));

    glm::vec3 input_direction{0.0F, 0.0F, 0.0F};
    if (input.IsKeyPressed(GLFW_KEY_W)) {
        input_direction += forward_flat;
    }
    if (input.IsKeyPressed(GLFW_KEY_S)) {
        input_direction -= forward_flat;
    }
    if (input.IsKeyPressed(GLFW_KEY_D)) {
        input_direction += right;
    }
    if (input.IsKeyPressed(GLFW_KEY_A)) {
        input_direction -= right;
    }

    if (glm::dot(input_direction, input_direction) > 0.0F) {
        input_direction = glm::normalize(input_direction);
    }

    const glm::vec3 target_velocity = input_direction * move_speed;
    const float blend = std::min(1.0F, smoothing * dt_seconds);
    camera_velocity_ += (target_velocity - camera_velocity_) * blend;
    camera_.position += camera_velocity_ * dt_seconds;
    camera_.aspect_ratio = renderer_.GetAspectRatio();
    CameraInputState camera_input_state{};
    camera_input_state.mouse_delta = mouse_delta;
    camera_input_state.raw_position = camera_.position;
    camera_input_state.horizontal_speed = glm::length(glm::vec2(camera_velocity_.x, camera_velocity_.z));
    camera_.Update(dt_seconds, camera_input_state);
    scene_.player_proxy_position = camera_.position;

    const bool build_toggle_pressed = input.IsKeyPressed(GLFW_KEY_B);
    if (build_toggle_pressed && !was_build_toggle_pressed_) {
        const bool build_mode_enabled = scene_.ToggleBuildMode();
        GF_LOG_INFO(std::string("Build Mode: ") + (build_mode_enabled ? "ON" : "OFF"));
    }
    was_build_toggle_pressed_ = build_toggle_pressed;

    const bool left_mouse_pressed = glfwGetMouseButton(renderer_.GetWindow(), GLFW_MOUSE_BUTTON_LEFT) == GLFW_PRESS;
    glm::vec3 build_ray_direction{0.0F, 0.0F, 0.0F};
    const bool has_build_ray = scene_.build_mode_enabled && TryComputeCursorRay(renderer_.GetWindow(), camera_, build_ray_direction);
    if (has_build_ray) {
        UpdateGhostPreview(scene_, camera_.position, build_ray_direction);
    } else {
        scene_.build_ghost_preview.reset();
    }

    if (left_mouse_pressed && !was_left_mouse_pressed_ && scene_.build_mode_enabled) {
        if (has_build_ray) {
            const bool placed = scene_.TryPlaceBuildingFromRay(camera_.position, build_ray_direction);
            GF_LOG_INFO(placed ? "Placed building." : "Building placement blocked.");
        }
    }
    was_left_mouse_pressed_ = left_mouse_pressed;

    const bool interact_pressed = input.IsKeyPressed(GLFW_KEY_E);
    if (interact_pressed && !was_interact_pressed_) {
        constexpr float kDialogInteractionDistance = 2.5F;
        DialogSystem::TryStartDialog(scene_, camera_.position, kDialogInteractionDistance);
    }
    was_interact_pressed_ = interact_pressed;

    constexpr std::array<int, 3> kChoiceKeys = {GLFW_KEY_1, GLFW_KEY_2, GLFW_KEY_3};
    for (std::size_t i = 0; i < kChoiceKeys.size(); ++i) {
        const bool choice_pressed = input.IsKeyPressed(kChoiceKeys[i]);
        if (choice_pressed && !was_dialog_choice_pressed_[i]) {
            DialogSystem::HandleChoiceInput(scene_, static_cast<int>(i));
        }
        was_dialog_choice_pressed_[i] = choice_pressed;
    }

    ProcessConsoleCommands(scene_);
    scene_.Update(dt_seconds);
    CoCreatorSystem::TrimHistory(scene_);
}

void Engine::SeedFallbackScene() {
    scene_.entities.clear();
    scene_.elapsed_seconds = 0.0F;
    scene_.day_progress = 0.25F;
    scene_.day_cycle_speed = 0.01F;
    scene_.day_count = 1;
    scene_.world_time = WorldTime{};
    scene_.npc_relationships.clear();
    scene_.relationships.clear();
    scene_.factions.clear();
    scene_.player_reputation.clear();
    scene_.economy = EconomyState{};
    scene_.active_dialog_npc_id = 0;
    scene_.navmesh = NavmeshData{};
    scene_.active_npc_ids.clear();
    scene_.npc_navigation.clear();
    scene_.player_proxy_position = {0.0F, 0.0F, 0.0F};

    constexpr std::array<float, 5> kInitialX = {-0.85F, -0.45F, 0.0F, 0.45F, 0.85F};
    constexpr std::array<float, 5> kVelocityX = {0.30F, 0.25F, 0.20F, 0.15F, 0.10F};
    constexpr std::array<float, 5> kScale = {0.18F, 0.16F, 0.20F, 0.14F, 0.17F};
    constexpr std::array<glm::vec4, 5> kBaseColors = {
        glm::vec4(0.95F, 0.35F, 0.35F, 1.0F),
        glm::vec4(0.35F, 0.95F, 0.45F, 1.0F),
        glm::vec4(0.35F, 0.55F, 0.95F, 1.0F),
        glm::vec4(0.95F, 0.85F, 0.35F, 1.0F),
        glm::vec4(0.85F, 0.35F, 0.95F, 1.0F),
    };

    for (std::size_t i = 0; i < kInitialX.size(); ++i) {
        Entity entity{};
        entity.id = i + 1;
        entity.transform.pos = {kInitialX[i], 0.0F, 0.0F};
        entity.transform.scale = {kScale[i], kScale[i], 1.0F};
        entity.renderable.color = kBaseColors[i];
        entity.velocity = {kVelocityX[i], 0.0F, 0.0F};
        if (i == 0U) {
            DialogNode greeting{};
            greeting.id = "greeting";
            greeting.text = "Hello traveler. Need supplies or advice?";
            greeting.choices = {
                DialogChoice{"I need supplies.", "farewell", DialogEffect{"wood", 2, 2.5F}},
                DialogChoice{"Any advice?", "farewell", DialogEffect{"", 0, 1.0F}},
                DialogChoice{"Goodbye.", "", DialogEffect{"", 0, -0.5F}},
            };

            DialogNode farewell{};
            farewell.id = "farewell";
            farewell.text = "Stay safe out there.";
            farewell.choices = {
                DialogChoice{"Thanks.", "", DialogEffect{"", 0, 1.0F}},
            };

            entity.dialog.start_node_id = greeting.id;
            entity.dialog.nodes = {greeting, farewell};
            entity.faction.faction_id = "guild_builders";
            entity.faction.role = "quartermaster";
            entity.schedule.job_id = "quartermaster";
        } else {
            entity.schedule.job_id = "laborer";
        }
        entity.schedule.home_position = entity.transform.pos;
        entity.schedule.workplace_position = entity.transform.pos + glm::vec3(0.8F, 0.0F, 0.6F);
        scene_.entities.push_back(entity);
    }
    EconomySystem::EnsureDefaults(scene_);
}

void Engine::Shutdown() {
    renderer_.Shutdown();
    GF_LOG_INFO("Runtime shutdown complete");
    Logger::Shutdown();
}
