#include "MeshLoader.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <nlohmann/json.hpp>
#include <string>
#include <vector>
#include <glm/common.hpp>
#include <glm/geometric.hpp>

namespace {
using json = nlohmann::json;

struct AccessView {
    const std::vector<std::uint8_t>* bytes = nullptr;
    std::size_t offset = 0;
    std::size_t stride = 0;
    std::size_t count = 0;
    int component_type = 0;
    std::string type{};
};

bool ReadBytes(const std::filesystem::path& path, std::vector<std::uint8_t>& out) {
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file.is_open()) {
        return false;
    }
    const auto size = file.tellg();
    if (size <= 0) {
        out.clear();
        return true;
    }
    out.resize(static_cast<std::size_t>(size));
    file.seekg(0);
    file.read(reinterpret_cast<char*>(out.data()), size);
    return true;
}

std::size_t ComponentSize(int component_type) {
    switch (component_type) {
        case 5120:
        case 5121:
            return 1;
        case 5122:
        case 5123:
            return 2;
        case 5125:
        case 5126:
            return 4;
        default:
            return 0;
    }
}

std::size_t TypeComponents(const std::string& type) {
    if (type == "SCALAR") return 1;
    if (type == "VEC2") return 2;
    if (type == "VEC3") return 3;
    if (type == "VEC4") return 4;
    return 0;
}

bool BuildAccessView(const json& gltf, const std::vector<std::vector<std::uint8_t>>& buffers, int accessor_index, AccessView& out) {
    if (!gltf.contains("accessors") || !gltf["accessors"].is_array()) {
        return false;
    }
    const auto& accessors = gltf["accessors"];
    if (accessor_index < 0 || static_cast<std::size_t>(accessor_index) >= accessors.size()) {
        return false;
    }
    const json& accessor = accessors[accessor_index];
    const int view_index = accessor.value("bufferView", -1);
    if (!gltf.contains("bufferViews") || !gltf["bufferViews"].is_array()) {
        return false;
    }
    const auto& buffer_views = gltf["bufferViews"];
    if (view_index < 0 || static_cast<std::size_t>(view_index) >= buffer_views.size()) {
        return false;
    }
    const json& view = buffer_views[view_index];
    const int buffer_index = view.value("buffer", -1);
    if (buffer_index < 0 || static_cast<std::size_t>(buffer_index) >= buffers.size()) {
        return false;
    }

    out.bytes = &buffers[static_cast<std::size_t>(buffer_index)];
    out.offset = static_cast<std::size_t>(view.value("byteOffset", 0)) + static_cast<std::size_t>(accessor.value("byteOffset", 0));
    out.count = static_cast<std::size_t>(accessor.value("count", 0));
    out.component_type = accessor.value("componentType", 0);
    out.type = accessor.value("type", "");
    const std::size_t comp_size = ComponentSize(out.component_type);
    const std::size_t comps = TypeComponents(out.type);
    if (comp_size == 0 || comps == 0 || out.count == 0) {
        return false;
    }
    const std::size_t default_stride = comp_size * comps;
    out.stride = static_cast<std::size_t>(view.value("byteStride", static_cast<int>(default_stride)));
    return out.stride >= default_stride;
}

template <typename T>
T ReadScalar(const AccessView& view, std::size_t index, std::size_t component) {
    const std::size_t offset = view.offset + index * view.stride + component * ComponentSize(view.component_type);
    T value{};
    if (offset + sizeof(T) > view.bytes->size()) {
        return value;
    }
    std::memcpy(&value, view.bytes->data() + offset, sizeof(T));
    return value;
}

float ReadFloat(const AccessView& view, std::size_t index, std::size_t component) {
    return ReadScalar<float>(view, index, component);
}

std::uint32_t ReadIndex(const AccessView& view, std::size_t index) {
    if (view.component_type == 5123) {
        return static_cast<std::uint32_t>(ReadScalar<std::uint16_t>(view, index, 0));
    }
    if (view.component_type == 5121) {
        return static_cast<std::uint32_t>(ReadScalar<std::uint8_t>(view, index, 0));
    }
    return ReadScalar<std::uint32_t>(view, index, 0);
}

}  // namespace

bool MeshLoader::LoadSimpleGltf(const std::string& path, std::uint32_t primitive_index, LoadedMesh& out_mesh, std::string& error) {
    out_mesh = LoadedMesh{};
    error.clear();

    std::ifstream gltf_file(path);
    if (!gltf_file.is_open()) {
        error = "Unable to open glTF file.";
        return false;
    }

    json gltf;
    try {
        gltf_file >> gltf;
    } catch (...) {
        error = "Invalid glTF JSON.";
        return false;
    }

    if (!gltf.contains("buffers") || !gltf["buffers"].is_array()) {
        error = "glTF missing buffers array.";
        return false;
    }

    const std::filesystem::path gltf_path(path);
    const std::filesystem::path base_dir = gltf_path.parent_path();

    std::vector<std::vector<std::uint8_t>> buffers;
    for (const json& buffer_node : gltf["buffers"]) {
        const std::string uri = buffer_node.value("uri", "");
        std::vector<std::uint8_t> bytes;
        if (uri.empty()) {
            error = "Embedded GLB buffers are not supported in V1 minimal loader.";
            return false;
        }
        if (!ReadBytes(base_dir / uri, bytes)) {
            error = "Failed to read buffer URI: " + uri;
            return false;
        }
        buffers.push_back(std::move(bytes));
    }

    if (!gltf.contains("meshes") || !gltf["meshes"].is_array() || gltf["meshes"].empty()) {
        error = "glTF missing meshes.";
        return false;
    }

    const json& mesh = gltf["meshes"][0];
    if (!mesh.contains("primitives") || !mesh["primitives"].is_array() || mesh["primitives"].empty()) {
        error = "glTF mesh missing primitives.";
        return false;
    }
    const auto& primitives = mesh["primitives"];
    const std::size_t primitive_slot = std::min<std::size_t>(primitive_index, primitives.size() - 1);
    const json& primitive = primitives[primitive_slot];

    if (!primitive.contains("attributes") || !primitive["attributes"].is_object()) {
        error = "glTF primitive missing attributes.";
        return false;
    }

    const json& attrs = primitive["attributes"];
    const int position_accessor = attrs.value("POSITION", -1);
    const int normal_accessor = attrs.value("NORMAL", -1);
    const int uv_accessor = attrs.value("TEXCOORD_0", -1);
    const int indices_accessor = primitive.value("indices", -1);

    AccessView pos_view{};
    if (!BuildAccessView(gltf, buffers, position_accessor, pos_view) || pos_view.type != "VEC3" || pos_view.component_type != 5126) {
        error = "Unsupported POSITION accessor format.";
        return false;
    }

    AccessView normal_view{};
    const bool has_normals = BuildAccessView(gltf, buffers, normal_accessor, normal_view) && normal_view.type == "VEC3";
    AccessView uv_view{};
    const bool has_uvs = BuildAccessView(gltf, buffers, uv_accessor, uv_view) && uv_view.type == "VEC2";

    out_mesh.vertices.reserve(pos_view.count);
    glm::vec3 bounds_min(std::numeric_limits<float>::max());
    glm::vec3 bounds_max(std::numeric_limits<float>::lowest());

    for (std::size_t i = 0; i < pos_view.count; ++i) {
        LoadedMeshVertex vertex{};
        vertex.position = glm::vec3(
            ReadFloat(pos_view, i, 0),
            ReadFloat(pos_view, i, 1),
            ReadFloat(pos_view, i, 2));
        if (has_normals) {
            vertex.normal = glm::normalize(glm::vec3(
                ReadFloat(normal_view, i, 0),
                ReadFloat(normal_view, i, 1),
                ReadFloat(normal_view, i, 2)));
        }
        if (has_uvs) {
            vertex.uv = glm::vec2(ReadFloat(uv_view, i, 0), ReadFloat(uv_view, i, 1));
        }
        bounds_min = glm::min(bounds_min, vertex.position);
        bounds_max = glm::max(bounds_max, vertex.position);
        out_mesh.vertices.push_back(vertex);
    }

    if (indices_accessor >= 0) {
        AccessView index_view{};
        if (!BuildAccessView(gltf, buffers, indices_accessor, index_view) || index_view.type != "SCALAR") {
            error = "Unsupported indices accessor format.";
            return false;
        }
        out_mesh.indices.reserve(index_view.count);
        for (std::size_t i = 0; i < index_view.count; ++i) {
            out_mesh.indices.push_back(ReadIndex(index_view, i));
        }
    } else {
        out_mesh.indices.reserve(out_mesh.vertices.size());
        for (std::size_t i = 0; i < out_mesh.vertices.size(); ++i) {
            out_mesh.indices.push_back(static_cast<std::uint32_t>(i));
        }
    }

    out_mesh.bounds_min = bounds_min;
    out_mesh.bounds_max = bounds_max;
    return true;
}
