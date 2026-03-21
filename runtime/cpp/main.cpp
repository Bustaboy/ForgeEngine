#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

int main() {
    const std::filesystem::path repo_root = std::filesystem::current_path();

    std::cout << "GameForge V1 minimal app (C++ runtime)\n";
    std::cout << "Mode: local-first, single-player, no-code-first\n";
    std::cout << "Target OS: Windows + Ubuntu\n";
    std::cout << "Rendering direction: Vulkan-first\n";

    const std::vector<std::string> required_paths = {
        "app",
        "editor/csharp",
        "runtime/cpp",
        "ai-orchestration/python",
    };

    for (const auto& rel_path : required_paths) {
        const auto full_path = repo_root / rel_path;
        const bool exists = std::filesystem::exists(full_path);
        std::cout << "- " << rel_path << ": " << (exists ? "OK" : "MISSING")
                  << " (" << full_path.string() << ")\n";
    }

    std::cout << "App started successfully.\n";
    return 0;
}
