#include "SceneLoader.h"

#include "Logger.h"
#include "templates/generated_gameplay.h"

#include <cmath>
#include <cstddef>
#include <fstream>
#include <algorithm>

#include <glm/gtc/constants.hpp>
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

glm::vec3 Lerp(const glm::vec3& start, const glm::vec3& end, float t) {
    return start + (end - start) * Clamp01(t);
}

float SegmentT(float progress, float start, float end) {
    if (end <= start) {
        return 0.0F;
    }
    return Clamp01((progress - start) / (end - start));
}

glm::vec3 SampleSkyColor(float day_progress) {
    constexpr glm::vec3 kNightSky{0.03F, 0.05F, 0.12F};
    constexpr glm::vec3 kSunriseSky{0.95F, 0.48F, 0.20F};
    constexpr glm::vec3 kDaySky{0.70F, 0.85F, 1.00F};
    constexpr glm::vec3 kSunsetSky{0.98F, 0.38F, 0.25F};

    if (day_progress < 0.25F) {
        return Lerp(kNightSky, kSunriseSky, SegmentT(day_progress, 0.0F, 0.25F));
    }
    if (day_progress < 0.50F) {
        return Lerp(kSunriseSky, kDaySky, SegmentT(day_progress, 0.25F, 0.50F));
    }
    if (day_progress < 0.75F) {
        return Lerp(kDaySky, kSunsetSky, SegmentT(day_progress, 0.50F, 0.75F));
    }
    return Lerp(kSunsetSky, kNightSky, SegmentT(day_progress, 0.75F, 1.0F));
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

void Scene::Update(float dt_seconds) {
    elapsed_seconds += dt_seconds;

    day_progress = Clamp01(day_progress);
    const float sun_angle = day_progress * glm::two_pi<float>();
    const glm::vec3 sun_direction = glm::normalize(glm::vec3(std::cos(sun_angle), std::sin(sun_angle), 0.25F));
    const float daylight = std::max(0.0F, sun_direction.y);
    const glm::vec3 sky_color = SampleSkyColor(day_progress);

    directional_light.direction = sun_direction;
    directional_light.color = sky_color;
    directional_light.intensity = 0.15F + daylight * 1.15F;

    for (std::size_t i = 0; i < entities.size(); ++i) {
        Entity& entity = entities[i];

        entity.transform.pos += entity.velocity * dt_seconds;
        entity.transform.pos.y += std::sin((elapsed_seconds * 1.35F) + static_cast<float>(i) * 0.85F) * 0.35F * dt_seconds;
        entity.transform.rot.z = elapsed_seconds * (0.3F + static_cast<float>(i) * 0.15F);

        if (entity.transform.pos.x > 1.2F) {
            entity.transform.pos.x = -1.2F;
        }

        const float pulse_r = 0.5F + 0.5F * std::sin(elapsed_seconds * (0.9F + static_cast<float>(i) * 0.1F));
        const float pulse_g = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.1F + static_cast<float>(i) * 0.07F));
        const float pulse_b = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.3F + static_cast<float>(i) * 0.05F));

        entity.renderable.color.r = 0.25F + 0.75F * pulse_r;
        entity.renderable.color.g = 0.25F + 0.75F * pulse_g;
        entity.renderable.color.b = 0.25F + 0.75F * pulse_b;
        entity.renderable.color.a = 1.0F;
    }

    UpdateGameplay(*this, dt_seconds);
}

bool Scene::Save(const std::string& path) const {
    return SceneLoader::Save(path, *this);
}

bool Scene::Load(const std::string& path) {
    return SceneLoader::Load(path, *this);
}
