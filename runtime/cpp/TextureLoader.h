#pragma once

#include <vulkan/vulkan.h>

#include <cstdint>
#include <string>
#include <vector>

namespace TextureLoader {
struct LoadedImage {
    int width = 0;
    int height = 0;
    std::vector<std::uint8_t> rgba8{};
};

LoadedImage LoadImageRGBA8(const std::string& path);
}
