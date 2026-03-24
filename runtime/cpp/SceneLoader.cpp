#include "SceneLoader.h"

#include "Logger.h"
#include <fstream>
#include <algorithm>
#include <nlohmann/json.hpp>

namespace {
using json = nlohmann::json;

json Vec3ToJson(const glm::vec3& value) {
    return json{{"x", value.x}, {"y", value.y}, {"z", value.z}};
}

glm::vec3 Vec3FromJson(const json& node, const glm::vec3& fallback) {
    glm::vec3 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    value.z = node.value("z", fallback.z);
    return value;
}

json Vec4ToJson(const glm::vec4& value) {
    return json{{"x", value.x}, {"y", value.y}, {"z", value.z}, {"w", value.w}};
}

glm::vec4 Vec4FromJson(const json& node, const glm::vec4& fallback) {
    glm::vec4 value = fallback;
    value.x = node.value("x", fallback.x);
    value.y = node.value("y", fallback.y);
    value.z = node.value("z", fallback.z);
    value.w = node.value("w", fallback.w);
    return value;
}

float Clamp01(float value) {
    return std::clamp(value, 0.0F, 1.0F);
}

json EntityToJson(const Entity& entity) {
    return json{
        {"id", entity.id},
        {"transform",
         {{"pos", Vec3ToJson(entity.transform.pos)},
          {"rot", Vec3ToJson(entity.transform.rot)},
          {"scale", Vec3ToJson(entity.transform.scale)}}},
        {"renderable", {{"color", Vec4ToJson(entity.renderable.color)}}},
        {"velocity", Vec3ToJson(entity.velocity)},
    };
}

Entity EntityFromJson(const json& node) {
    Entity entity{};
    entity.id = node.value("id", entity.id);

    if (node.contains("transform") && node["transform"].is_object()) {
        const json& transform = node["transform"];
        if (transform.contains("pos") && transform["pos"].is_object()) {
            entity.transform.pos = Vec3FromJson(transform["pos"], entity.transform.pos);
        }
        if (transform.contains("rot") && transform["rot"].is_object()) {
            entity.transform.rot = Vec3FromJson(transform["rot"], entity.transform.rot);
        }
        if (transform.contains("scale") && transform["scale"].is_object()) {
            entity.transform.scale = Vec3FromJson(transform["scale"], entity.transform.scale);
        }
    }

    if (node.contains("renderable") && node["renderable"].is_object()) {
        const json& renderable = node["renderable"];
        if (renderable.contains("color") && renderable["color"].is_object()) {
            entity.renderable.color = Vec4FromJson(renderable["color"], entity.renderable.color);
        }
    }

    if (node.contains("velocity") && node["velocity"].is_object()) {
        entity.velocity = Vec3FromJson(node["velocity"], entity.velocity);
    }

    return entity;
}

json DirectionalLightToJson(const DirectionalLight& light) {
    return json{
        {"direction", Vec3ToJson(light.direction)},
        {"color", Vec3ToJson(light.color)},
        {"intensity", light.intensity},
    };
}
}  // namespace

bool SceneLoader::Load(const std::string& path, Scene& scene) {
    std::ifstream file(path);
    if (!file.is_open()) {
        return false;
    }

    json document;
    try {
        file >> document;
    } catch (const std::exception& exception) {
        GF_LOG_INFO("Scene JSON parse failed: " + std::string(exception.what()));
        return false;
    }

    if (!document.contains("entities") || !document["entities"].is_array()) {
        return false;
    }

    scene.entities.clear();
    scene.entities.reserve(document["entities"].size());

    for (const json& entity_node : document["entities"]) {
        if (entity_node.is_object()) {
            scene.entities.push_back(EntityFromJson(entity_node));
        }
    }

    scene.elapsed_seconds = document.value("elapsed_seconds", 0.0F);
    scene.day_progress = Clamp01(document.value("day_progress", scene.day_progress));
    scene.day_cycle_speed = std::max(0.0F, document.value("day_cycle_speed", scene.day_cycle_speed));
    scene.day_count = std::max(1U, document.value("day_count", scene.day_count));

    if (document.contains("directional_light") && document["directional_light"].is_object()) {
        const json& light = document["directional_light"];
        if (light.contains("direction") && light["direction"].is_object()) {
            scene.directional_light.direction = Vec3FromJson(light["direction"], scene.directional_light.direction);
        }
        if (light.contains("color") && light["color"].is_object()) {
            scene.directional_light.color = Vec3FromJson(light["color"], scene.directional_light.color);
        }
        scene.directional_light.intensity = light.value("intensity", scene.directional_light.intensity);
    }

    scene.Update(0.0F);
    return true;
}

bool SceneLoader::Save(const std::string& path, const Scene& scene) {
    json document;
    document["entities"] = json::array();
    document["entities"].reserve(scene.entities.size());

    for (const Entity& entity : scene.entities) {
        document["entities"].push_back(EntityToJson(entity));
    }
    document["elapsed_seconds"] = scene.elapsed_seconds;
    document["day_progress"] = scene.day_progress;
    document["day_cycle_speed"] = scene.day_cycle_speed;
    document["day_count"] = scene.day_count;
    document["directional_light"] = DirectionalLightToJson(scene.directional_light);

    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }

    file << document.dump(4) << '\n';
    return true;
}
