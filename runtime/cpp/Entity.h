#pragma once

#include <glm/vec3.hpp>
#include <glm/vec4.hpp>
#include <glm/vec2.hpp>

#include <cstdint>
#include <map>
#include <string>
#include <vector>

struct Transform {
    glm::vec3 pos{0.0F, 0.0F, 0.0F};
    glm::vec3 rot{0.0F, 0.0F, 0.0F};
    glm::vec3 scale{0.20F, 0.20F, 1.0F};
};

struct Renderable {
    glm::vec4 color{0.35F, 0.85F, 0.65F, 1.0F};
};

struct BuildableComponent {
    std::string type{};
    glm::ivec2 grid_size{1, 1};

    [[nodiscard]] bool IsValid() const {
        return !type.empty();
    }
};

struct InventoryComponent {
    std::map<std::string, int> items{};
};

using Inventory = InventoryComponent;

struct DialogEffect {
    std::string inventory_item{};
    int inventory_delta = 0;
    float relationship_delta = 0.0F;
};

struct DialogChoice {
    std::string text{};
    std::string next_node_id{};
    DialogEffect effect{};
};

struct DialogNode {
    std::string id{};
    std::string text{};
    std::vector<DialogChoice> choices{};

    [[nodiscard]] bool IsValid() const {
        return !id.empty() && !text.empty();
    }
};

struct DialogComponent {
    std::vector<DialogNode> nodes{};
    std::string start_node_id{};
    std::string active_node_id{};
    bool in_progress = false;

    [[nodiscard]] bool IsValid() const {
        if (nodes.empty()) {
            return false;
        }
        for (const DialogNode& node : nodes) {
            if (!node.IsValid()) {
                return false;
            }
        }
        return true;
    }
};

struct Entity {
    std::uint64_t id = 0;
    Transform transform{};
    Renderable renderable{};
    BuildableComponent buildable{};
    InventoryComponent inventory{};
    DialogComponent dialog{};
    glm::vec3 velocity{0.0F, 0.0F, 0.0F};
};

struct DirectionalLight {
    glm::vec3 direction{0.0F, -1.0F, 0.0F};
    glm::vec3 color{1.0F, 1.0F, 1.0F};
    float intensity = 1.0F;
};
