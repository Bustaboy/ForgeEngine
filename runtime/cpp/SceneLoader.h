#pragma once

#include "Scene.h"

#include <string>

class SceneLoader {
public:
    static bool Load(const std::string& path, Scene& scene);
    static bool Save(const std::string& path, const Scene& scene);
};
