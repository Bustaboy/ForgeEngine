#include "TextureLoader.h"

#define STB_IMAGE_IMPLEMENTATION
#include "external/stb/stb_image.h"

#include <cstring>
#include <stdexcept>

namespace TextureLoader {
LoadedImage LoadImageRGBA8(const std::string& path) {
    int width = 0;
    int height = 0;
    int channels = 0;

    stbi_uc* pixels = stbi_load(path.c_str(), &width, &height, &channels, STBI_rgb_alpha);
    if (pixels == nullptr) {
        throw std::runtime_error("Failed to decode texture: " + path);
    }

    LoadedImage result{};
    result.width = width;
    result.height = height;
    result.rgba8.resize(static_cast<std::size_t>(width) * static_cast<std::size_t>(height) * 4U);
    std::memcpy(result.rgba8.data(), pixels, result.rgba8.size());

    stbi_image_free(pixels);
    return result;
}
}  // namespace TextureLoader
