# App Module

GameForge V1 application startup is anchored to the **C# editor/launcher shell** (`editor/csharp/Program.cs`).

Module boundaries for V1:
- **App entrypoint (launcher/editor shell):** C#
- **Game runtime entrypoint (generated game runtime):** C++ (`runtime/cpp/main.cpp`)
- **AI orchestration/tooling automation:** Python (`ai-orchestration/python/`), optional for app startup

Bootstrap commands:
- Ubuntu/Linux: `./scripts/bootstrap.sh`
- Windows: `pwsh -f scripts/bootstrap.ps1`


Interview state contract (V1):
- **Language-agnostic source of truth:** `app/schemas/interview-session.v1.schema.json`
- **Persistence owner:** C# editor layer (`editor/csharp/Interview/InterviewSessionStore.cs`)
- **Python orchestration role:** consume/produce JSON that conforms to the shared schema contract
