#pragma once

#include "Scene.h"

#include <nlohmann/json.hpp>

#include <string>

class SceneLoader {
public:
    static bool Load(const std::string& path, Scene& scene);
    static bool Save(const std::string& path, const Scene& scene);
    static bool Validate(const std::string& path, std::string& report);
};
