cmake_minimum_required(VERSION 3.16)

project(ForgeEngineRuntime VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Vulkan REQUIRED)
find_package(glfw3 3.3 REQUIRED)

set(FORGE_RUNTIME_SOURCES
    runtime/cpp/main.cpp
    runtime/cpp/Engine.cpp
    runtime/cpp/Timer.cpp
    runtime/cpp/VulkanRenderer.cpp
)

add_executable(forge_runtime ${FORGE_RUNTIME_SOURCES})

target_include_directories(forge_runtime
    PRIVATE
        runtime/cpp
        external/glm
        external/nlohmann_json/include
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

find_program(GLSLC_EXECUTABLE glslc HINTS ENV VULKAN_SDK PATH_SUFFIXES Bin bin)
set(FORGE_SHADER_DIR ${CMAKE_CURRENT_SOURCE_DIR}/runtime/cpp/shaders)
set(FORGE_SHADER_OUTPUT_DIR ${CMAKE_BINARY_DIR}/shaders)
set(FORGE_SHADER_SOURCES
    ${FORGE_SHADER_DIR}/basic.vert
    ${FORGE_SHADER_DIR}/basic.frag
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
