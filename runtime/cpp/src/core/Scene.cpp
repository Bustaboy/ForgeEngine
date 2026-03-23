#include "Scene.h"

#include "../../templates/generated_gameplay.h"

#include <cmath>
#include <fstream>

#include <nlohmann/json.hpp>

void Scene::Update(float dt_seconds) {
    elapsed_seconds += dt_seconds;

    for (Entity& entity : entities) {
        entity.transform.pos += entity.velocity * dt_seconds;

        constexpr float bound = 1.15F;
        if (entity.transform.pos.x < -bound) {
            entity.transform.pos.x = -bound;
            entity.velocity.x = std::abs(entity.velocity.x);
        } else if (entity.transform.pos.x > bound) {
            entity.transform.pos.x = bound;
            entity.velocity.x = -std::abs(entity.velocity.x);
        }

        entity.transform.pos.y = std::sin(elapsed_seconds * 1.7F + static_cast<float>(entity.id) * 0.37F) * 0.08F;
    }

    UpdateGameplay(*this, dt_seconds);
}

bool Scene::Save(const std::string& path) const {
    nlohmann::json root;
    root["elapsed_seconds"] = elapsed_seconds;
    root["entities"] = nlohmann::json::array();

    for (const Entity& entity : entities) {
        nlohmann::json item;
        item["id"] = entity.id;
        item["transform"]["pos"] = {entity.transform.pos.x, entity.transform.pos.y, entity.transform.pos.z};
        item["transform"]["rot"] = {entity.transform.rot.x, entity.transform.rot.y, entity.transform.rot.z};
        item["transform"]["scale"] = {entity.transform.scale.x, entity.transform.scale.y, entity.transform.scale.z};
        item["renderable"]["color"] = {entity.renderable.color.x, entity.renderable.color.y, entity.renderable.color.z, entity.renderable.color.w};
        item["velocity"] = {entity.velocity.x, entity.velocity.y, entity.velocity.z};
        root["entities"].push_back(item);
    }

    std::ofstream file(path);
    if (!file.is_open()) {
        return false;
    }

    file << root.dump(2);
    return file.good();
}

bool Scene::Load(const std::string& path) {
    std::ifstream file(path);
    if (!file.is_open()) {
        return false;
    }

    nlohmann::json root;
    try {
        file >> root;
    } catch (...) {
        return false;
    }

    elapsed_seconds = root.value("elapsed_seconds", 0.0F);

    entities.clear();
    if (!root.contains("entities") || !root["entities"].is_array()) {
        return true;
    }

    for (const nlohmann::json& item : root["entities"]) {
        Entity entity{};
        entity.id = item.value("id", 0ULL);

        if (item.contains("transform")) {
            const nlohmann::json& transform = item["transform"];
            if (transform.contains("pos") && transform["pos"].is_array() && transform["pos"].size() == 3) {
                entity.transform.pos = {
                    transform["pos"][0].get<float>(),
                    transform["pos"][1].get<float>(),
                    transform["pos"][2].get<float>()};
            }
            if (transform.contains("rot") && transform["rot"].is_array() && transform["rot"].size() == 3) {
                entity.transform.rot = {
                    transform["rot"][0].get<float>(),
                    transform["rot"][1].get<float>(),
                    transform["rot"][2].get<float>()};
            }
            if (transform.contains("scale") && transform["scale"].is_array() && transform["scale"].size() == 3) {
                entity.transform.scale = {
                    transform["scale"][0].get<float>(),
                    transform["scale"][1].get<float>(),
                    transform["scale"][2].get<float>()};
            }
        }

        if (item.contains("renderable") && item["renderable"].contains("color") && item["renderable"]["color"].is_array() && item["renderable"]["color"].size() == 4) {
            entity.renderable.color = {
                item["renderable"]["color"][0].get<float>(),
                item["renderable"]["color"][1].get<float>(),
                item["renderable"]["color"][2].get<float>(),
                item["renderable"]["color"][3].get<float>()};
        }

        if (item.contains("velocity") && item["velocity"].is_array() && item["velocity"].size() == 3) {
            entity.velocity = {
                item["velocity"][0].get<float>(),
                item["velocity"][1].get<float>(),
                item["velocity"][2].get<float>()};
        }

        entities.push_back(entity);
    }

    return true;
}
