cmake_minimum_required(VERSION 3.16)

project(ForgeEngineRuntime VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Vulkan REQUIRED)
find_package(glfw3 3.3 REQUIRED)

add_executable(forge_runtime
    runtime/cpp/main.cpp
)

target_link_libraries(forge_runtime
    PRIVATE
        Vulkan::Vulkan
        glfw
)

if(MSVC)
    target_compile_options(forge_runtime PRIVATE /W4 /permissive-)
else()
    target_compile_options(forge_runtime PRIVATE -Wall -Wextra -Wpedantic)
endif()

set_target_properties(forge_runtime PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
