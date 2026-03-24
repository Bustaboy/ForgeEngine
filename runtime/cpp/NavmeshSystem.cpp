#include "NavmeshSystem.h"

#include "EconomySystem.h"
#include "Scene.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <queue>
#include <unordered_map>

namespace {
struct Cell {
    int x = 0;
    int z = 0;
};

struct QueueNode {
    int x = 0;
    int z = 0;
    float score = 0.0F;

    bool operator<(const QueueNode& other) const {
        return score > other.score;
    }
};

float DistanceHeuristic(const Cell& a, const Cell& b) {
    const float dx = static_cast<float>(a.x - b.x);
    const float dz = static_cast<float>(a.z - b.z);
    return std::sqrt(dx * dx + dz * dz);
}

bool IsNpcEntity(const Entity& entity) {
    return entity.buildable.IsValid() == false && (!entity.faction.role.empty() || entity.dialog.IsValid());
}

glm::vec3 SettlementAnchor(const std::string& settlement) {
    std::uint32_t hash = 2166136261U;
    for (unsigned char c : settlement) {
        hash ^= static_cast<std::uint32_t>(c);
        hash *= 16777619U;
    }

    const float x = static_cast<float>(static_cast<int>((hash % 23U)) - 11);
    const float z = static_cast<float>(static_cast<int>(((hash / 23U) % 23U)) - 11);
    return {x, 0.0F, z};
}

Cell WorldToCell(const NavmeshData& navmesh, const glm::vec3& world) {
    const int x = static_cast<int>(std::floor((world.x - static_cast<float>(navmesh.min_x)) / navmesh.cell_size));
    const int z = static_cast<int>(std::floor((world.z - static_cast<float>(navmesh.min_z)) / navmesh.cell_size));
    return {x, z};
}

glm::vec3 CellToWorldCenter(const NavmeshData& navmesh, int x, int z) {
    return {
        static_cast<float>(navmesh.min_x) + (static_cast<float>(x) + 0.5F) * navmesh.cell_size,
        0.0F,
        static_cast<float>(navmesh.min_z) + (static_cast<float>(z) + 0.5F) * navmesh.cell_size,
    };
}

Entity* FindEntity(Scene& scene, std::uint64_t entity_id) {
    for (Entity& entity : scene.entities) {
        if (entity.id == entity_id) {
            return &entity;
        }
    }
    return nullptr;
}

bool BuildGridPath(const NavmeshData& navmesh, const Cell& start, const Cell& goal, std::vector<NavPathNode>& out_nodes) {
    out_nodes.clear();
    if (!navmesh.IsInside(start.x, start.z) || !navmesh.IsInside(goal.x, goal.z)) {
        return false;
    }

    const int size = navmesh.width * navmesh.height;
    std::vector<float> g_score(static_cast<std::size_t>(size), std::numeric_limits<float>::infinity());
    std::vector<int> came_from(static_cast<std::size_t>(size), -1);
    std::priority_queue<QueueNode> open;

    const int start_index = navmesh.Index(start.x, start.z);
    const int goal_index = navmesh.Index(goal.x, goal.z);
    g_score[static_cast<std::size_t>(start_index)] = 0.0F;
    open.push(QueueNode{start.x, start.z, DistanceHeuristic(start, goal)});

    constexpr Cell kDirections[8] = {{1, 0}, {-1, 0}, {0, 1}, {0, -1}, {1, 1}, {-1, -1}, {1, -1}, {-1, 1}};

    while (!open.empty()) {
        const QueueNode current = open.top();
        open.pop();
        const int current_index = navmesh.Index(current.x, current.z);
        if (current_index == goal_index) {
            int walk_index = goal_index;
            while (walk_index != -1) {
                const int x = walk_index % navmesh.width;
                const int z = walk_index / navmesh.width;
                out_nodes.push_back(NavPathNode{x, z});
                walk_index = came_from[static_cast<std::size_t>(walk_index)];
            }
            std::reverse(out_nodes.begin(), out_nodes.end());
            return !out_nodes.empty();
        }

        for (const Cell& direction : kDirections) {
            const int nx = current.x + direction.x;
            const int nz = current.z + direction.z;
            if (!navmesh.IsInside(nx, nz)) {
                continue;
            }
            const int neighbor_index = navmesh.Index(nx, nz);
            if (navmesh.walkable[static_cast<std::size_t>(neighbor_index)] == 0U) {
                continue;
            }

            const float move_cost = (direction.x == 0 || direction.z == 0) ? 1.0F : 1.4142F;
            const float tentative_g = g_score[static_cast<std::size_t>(current_index)] + move_cost;
            if (tentative_g >= g_score[static_cast<std::size_t>(neighbor_index)]) {
                continue;
            }

            came_from[static_cast<std::size_t>(neighbor_index)] = current_index;
            g_score[static_cast<std::size_t>(neighbor_index)] = tentative_g;
            const float score = tentative_g + DistanceHeuristic(Cell{nx, nz}, goal);
            open.push(QueueNode{nx, nz, score});
        }
    }

    return false;
}

std::size_t CountBuildables(const Scene& scene) {
    std::size_t count = 0;
    for (const Entity& entity : scene.entities) {
        if (entity.buildable.IsValid()) {
            ++count;
        }
    }
    return count;
}

void RefreshActiveNpcIds(Scene& scene) {
    scene.active_npc_ids.clear();
    for (const Entity& entity : scene.entities) {
        if (IsNpcEntity(entity)) {
            scene.active_npc_ids.push_back(entity.id);
        }
    }
}

}  // namespace

namespace NavmeshSystem {

void MarkDirty(Scene& scene) {
    scene.navmesh.dirty = true;
}

void Rebuild(Scene& scene) {
    int min_x = -12;
    int max_x = 12;
    int min_z = -12;
    int max_z = 12;

    for (const Entity& entity : scene.entities) {
        min_x = std::min(min_x, static_cast<int>(std::floor(entity.transform.pos.x - std::abs(entity.transform.scale.x) - 2.0F)));
        max_x = std::max(max_x, static_cast<int>(std::ceil(entity.transform.pos.x + std::abs(entity.transform.scale.x) + 2.0F)));
        min_z = std::min(min_z, static_cast<int>(std::floor(entity.transform.pos.z - std::abs(entity.transform.scale.z) - 2.0F)));
        max_z = std::max(max_z, static_cast<int>(std::ceil(entity.transform.pos.z + std::abs(entity.transform.scale.z) + 2.0F)));
    }

    NavmeshData rebuilt{};
    rebuilt.cell_size = 1.0F;
    rebuilt.min_x = min_x;
    rebuilt.min_z = min_z;
    rebuilt.width = std::max(8, max_x - min_x + 1);
    rebuilt.height = std::max(8, max_z - min_z + 1);
    rebuilt.walkable.assign(static_cast<std::size_t>(rebuilt.width * rebuilt.height), 1U);

    for (const Entity& entity : scene.entities) {
        if (!entity.buildable.IsValid()) {
            continue;
        }

        const float half_x = std::max(0.5F, std::abs(entity.transform.scale.x) * 0.5F + 0.25F);
        const float half_z = std::max(0.5F, std::abs(entity.transform.scale.z) * 0.5F + 0.25F);
        const int start_x = static_cast<int>(std::floor(entity.transform.pos.x - half_x));
        const int end_x = static_cast<int>(std::ceil(entity.transform.pos.x + half_x));
        const int start_z = static_cast<int>(std::floor(entity.transform.pos.z - half_z));
        const int end_z = static_cast<int>(std::ceil(entity.transform.pos.z + half_z));

        for (int z = start_z; z <= end_z; ++z) {
            for (int x = start_x; x <= end_x; ++x) {
                const Cell cell = WorldToCell(rebuilt, glm::vec3(static_cast<float>(x), 0.0F, static_cast<float>(z)));
                if (!rebuilt.IsInside(cell.x, cell.z)) {
                    continue;
                }
                rebuilt.walkable[static_cast<std::size_t>(rebuilt.Index(cell.x, cell.z))] = 0U;
            }
        }
    }

    rebuilt.last_buildable_count = CountBuildables(scene);
    rebuilt.revision = scene.navmesh.revision + 1;
    rebuilt.dirty = false;
    scene.navmesh = std::move(rebuilt);

    RefreshActiveNpcIds(scene);
    scene.npc_navigation.clear();
}

bool BuildPath(Scene& scene, std::uint64_t entity_id, const glm::vec3& target_world, const std::string& mode, const std::string& route_id) {
    Entity* entity = FindEntity(scene, entity_id);
    if (entity == nullptr) {
        return false;
    }

    if (scene.navmesh.dirty || scene.navmesh.walkable.empty()) {
        Rebuild(scene);
    }

    const Cell start = WorldToCell(scene.navmesh, entity->transform.pos);
    const Cell goal = WorldToCell(scene.navmesh, target_world);
    std::vector<NavPathNode> nodes;
    if (!BuildGridPath(scene.navmesh, start, goal, nodes)) {
        return false;
    }

    NpcNavigationState& navigation = scene.npc_navigation[entity_id];
    navigation.mode = mode;
    navigation.route_id = route_id;
    navigation.path = std::move(nodes);
    navigation.path_index = navigation.path.size() > 1U ? 1U : 0U;
    navigation.target_world = target_world;
    return true;
}

void Update(Scene& scene, float /*dt_seconds*/) {
    const std::size_t buildable_count = CountBuildables(scene);
    if (scene.navmesh.walkable.empty() || scene.navmesh.dirty || scene.navmesh.last_buildable_count != buildable_count) {
        Rebuild(scene);
    }

    RefreshActiveNpcIds(scene);

    if (scene.active_dialog_npc_id != 0) {
        BuildPath(scene, scene.active_dialog_npc_id, scene.player_proxy_position, "dialog", "");
    }

    if (scene.economy.trade_routes.empty()) {
        return;
    }

    for (std::uint64_t npc_id : scene.active_npc_ids) {
        if (npc_id == scene.active_dialog_npc_id) {
            continue;
        }

        Entity* entity = FindEntity(scene, npc_id);
        if (entity == nullptr) {
            continue;
        }

        const std::string role_lower = entity->faction.role;
        const bool should_trade = role_lower.find("trader") != std::string::npos || role_lower.find("merchant") != std::string::npos ||
                                  role_lower.find("caravan") != std::string::npos || role_lower.find("quartermaster") != std::string::npos;
        if (!should_trade) {
            continue;
        }

        NpcNavigationState& nav = scene.npc_navigation[npc_id];
        if (nav.route_id.empty()) {
            const std::size_t route_index = static_cast<std::size_t>(npc_id % scene.economy.trade_routes.size());
            nav.route_id = scene.economy.trade_routes[route_index].route_id;
            nav.mode = "trade";
            nav.heading_to_destination = true;
            nav.desired_speed = 1.25F + (static_cast<float>(route_index) * 0.1F);
        }

        auto route_it = std::find_if(scene.economy.trade_routes.begin(), scene.economy.trade_routes.end(), [&](const EconomyTradeRoute& route) {
            return route.route_id == nav.route_id;
        });
        if (route_it == scene.economy.trade_routes.end()) {
            nav.route_id.clear();
            nav.path.clear();
            continue;
        }

        const glm::vec3 route_from = SettlementAnchor(route_it->from_settlement);
        const glm::vec3 route_to = SettlementAnchor(route_it->to_settlement);
        const glm::vec3 desired_target = nav.heading_to_destination ? route_to : route_from;

        const bool needs_path = nav.path.empty() || nav.path_index >= nav.path.size();
        const float dx = entity->transform.pos.x - desired_target.x;
        const float dz = entity->transform.pos.z - desired_target.z;
        const float close_distance_sq = dx * dx + dz * dz;
        if (close_distance_sq <= 1.0F) {
            nav.heading_to_destination = !nav.heading_to_destination;
            BuildPath(scene, npc_id, nav.heading_to_destination ? route_to : route_from, "trade", nav.route_id);
            continue;
        }

        if (needs_path) {
            BuildPath(scene, npc_id, desired_target, "trade", nav.route_id);
        }
    }
}

}  // namespace NavmeshSystem
