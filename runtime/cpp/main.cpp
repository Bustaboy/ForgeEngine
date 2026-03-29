#include "Engine.h"

#include <string_view>

int main(int argc, char* argv[]) {
    Engine engine;

    if (argc > 1 && std::string_view(argv[1]) == "--smoke-headless") {
        engine.RunHeadlessSmoke();
        return 0;
    }

    engine.Run();
    return 0;
}
