cmake_minimum_required(VERSION 3.16)

project(ForgeEngineRuntime VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(FORGE_JSON_HEADER_CANDIDATES
    "${CMAKE_CURRENT_SOURCE_DIR}/external/nlohmann/json.hpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/external/nlohmann_json/single_include/nlohmann/json.hpp"
)
set(FORGE_JSON_HEADER_PATH "")
foreach(candidate ${FORGE_JSON_HEADER_CANDIDATES})
    if(EXISTS "${candidate}")
        set(FORGE_JSON_HEADER_PATH "${candidate}")
        break()
    endif()
endforeach()
if(FORGE_JSON_HEADER_PATH STREQUAL "")
    message(FATAL_ERROR "Missing vendored nlohmann/json.hpp at runtime/cpp/external/nlohmann/json.hpp. Run ./scripts/bootstrap.sh --runtime-only first (or scripts/bootstrap.ps1 -RuntimeOnly on Windows).")
endif()

set(FORGE_VULKAN_HINT_PATHS)
if(DEFINED ENV{VULKAN_SDK})
    list(APPEND FORGE_VULKAN_HINT_PATHS "$ENV{VULKAN_SDK}")
endif()
if(WIN32)
    if(DEFINED ENV{ProgramFiles})
        list(APPEND FORGE_VULKAN_HINT_PATHS "$ENV{ProgramFiles}/VulkanSDK")
    endif()
    if(DEFINED ENV{ProgramFiles\(x86\)})
        list(APPEND FORGE_VULKAN_HINT_PATHS "$ENV{ProgramFiles\(x86\)}/VulkanSDK")
    endif()
else()
    list(APPEND FORGE_VULKAN_HINT_PATHS /usr /usr/local /opt/vulkansdk)
endif()

find_package(Vulkan MODULE QUIET)
if(NOT Vulkan_FOUND)
    find_package(Vulkan QUIET HINTS ${FORGE_VULKAN_HINT_PATHS})
endif()
if(NOT Vulkan_FOUND)
    find_package(Vulkan REQUIRED)
endif()

find_package(glfw3 3.3 QUIET)
if(NOT glfw3_FOUND)
    find_path(GLFW3_INCLUDE_DIR GLFW/glfw3.h
        HINTS
            $ENV{GLFW_DIR}/include
            /usr/include
            /usr/local/include
    )
    find_library(GLFW3_LIBRARY NAMES glfw3 glfw
        HINTS
            $ENV{GLFW_DIR}/lib
            /usr/lib
            /usr/local/lib
            /usr/lib/x86_64-linux-gnu
    )

    if(NOT GLFW3_INCLUDE_DIR OR NOT GLFW3_LIBRARY)
        message(FATAL_ERROR "glfw3 not found. Install glfw3 dev package or set GLFW_DIR.")
    endif()

    add_library(glfw UNKNOWN IMPORTED)
    set_target_properties(glfw PROPERTIES
        IMPORTED_LOCATION "${GLFW3_LIBRARY}"
        INTERFACE_INCLUDE_DIRECTORIES "${GLFW3_INCLUDE_DIR}"
    )
endif()

set(FORGE_RUNTIME_SOURCES
    main.cpp
    Engine.cpp
    BuildingSystem.h
    BuildingSystem.cpp
    InventorySystem.h
    InventorySystem.cpp
    DialogSystem.h
    DialogSystem.cpp
    Timer.cpp
    Scene.cpp
    VulkanRenderer.cpp
    SceneLoader.cpp
    src/input/InputManager.cpp
)

add_executable(forge_runtime ${FORGE_RUNTIME_SOURCES})

target_include_directories(forge_runtime
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/external
        ${CMAKE_CURRENT_SOURCE_DIR}/external/nlohmann
        ${CMAKE_CURRENT_SOURCE_DIR}/external/nlohmann_json/single_include
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

find_program(GLSLC_EXECUTABLE glslc
    HINTS
        ENV VULKAN_SDK
        /usr/bin
        /usr/local/bin
    PATH_SUFFIXES Bin bin
)
set(FORGE_SHADER_DIR ${CMAKE_CURRENT_SOURCE_DIR}/shaders)
set(FORGE_SHADER_OUTPUT_DIR ${CMAKE_BINARY_DIR}/shaders)
set(FORGE_SHADER_SOURCES
    ${FORGE_SHADER_DIR}/vertex.vert
    ${FORGE_SHADER_DIR}/fragment.frag
)

if(GLSLC_EXECUTABLE)
    file(MAKE_DIRECTORY ${FORGE_SHADER_OUTPUT_DIR})

    set(FORGE_SPV_OUTPUTS)
    foreach(shader_file ${FORGE_SHADER_SOURCES})
        get_filename_component(shader_name ${shader_file} NAME)
        set(shader_output ${FORGE_SHADER_OUTPUT_DIR}/${shader_name}.spv)
        add_custom_command(
            OUTPUT ${shader_output}
            COMMAND ${GLSLC_EXECUTABLE} ${shader_file} -o ${shader_output}
            DEPENDS ${shader_file}
            COMMENT "Compiling shader ${shader_name}"
            VERBATIM
        )
        list(APPEND FORGE_SPV_OUTPUTS ${shader_output})
    endforeach()

    add_custom_target(compile_shaders ALL DEPENDS ${FORGE_SPV_OUTPUTS})
    add_dependencies(forge_runtime compile_shaders)
else()
    message(WARNING "glslc not found; shader compilation target disabled.")
endif()

set_target_properties(forge_runtime PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
