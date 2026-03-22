cmake_minimum_required(VERSION 3.16)

project(ForgeEngineRuntime VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

include(FetchContent)

find_package(Vulkan REQUIRED)
find_package(glfw3 3.3 REQUIRED)

FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
)
FetchContent_MakeAvailable(nlohmann_json)

add_executable(forge_runtime
    runtime/cpp/main.cpp
)

target_link_libraries(forge_runtime
    PRIVATE
        Vulkan::Vulkan
        glfw
        nlohmann_json::nlohmann_json
)


if(MSVC)
    target_compile_options(forge_runtime PRIVATE /W4 /permissive-)
else()
    target_compile_options(forge_runtime PRIVATE -Wall -Wextra -Wpedantic)
endif()

set_target_properties(forge_runtime PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
