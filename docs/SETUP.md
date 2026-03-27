# GameForge V1 Setup (Windows + Ubuntu)

This setup is intentionally simple and local-first for Milestone 1.

## Scope Guardrails (V1)
- Single-player only.
- No marketplace.
- No first-party cloud hosting.
- Runtime stack lock: C++.
- Editor stack lock: C#.
- AI orchestration stack lock: Python.
- Rendering direction: Vulkan-first.

## Entrypoint Boundaries (V1)
- Main app entrypoint: **C# editor launcher shell**.
- Game runtime entrypoint: **C++ runtime**.
- Python entrypoint: **optional**, for AI orchestration/automation commands only (not required for app startup).

## Prerequisites
### Ubuntu
- `g++` with C++17 support.
- .NET SDK 8+ (`dotnet`) for C# app startup.
- Git.

### Windows
- PowerShell 7+ (`pwsh`) recommended.
- MinGW-w64 `g++` with C++17 support available in PATH.
- .NET SDK 8+ (`dotnet`) for C# app startup.
- Git.

## Bootstrap Command

### Ubuntu/Linux
```bash
git clone <repo-url>
cd ForgeEngine
./scripts/bootstrap.sh
```

### Windows (PowerShell)
```powershell
git clone <repo-url>
cd ForgeEngine
pwsh -f scripts/bootstrap.ps1
```

For headless launcher verification in automation:

Ubuntu/Linux:
```bash
./scripts/bootstrap.sh --launcher-smoke
```

Windows (PowerShell):
```powershell
pwsh -f scripts/bootstrap.ps1 -LauncherSmoke
```

## Optional Runtime-Only Mode

Use this when the C# toolchain is not installed yet but you want to verify runtime compilation.

Ubuntu/Linux:
```bash
./scripts/bootstrap.sh --runtime-only
```

Windows (PowerShell):
```powershell
pwsh -f scripts/bootstrap.ps1 -RuntimeOnly
```

## What Bootstrap Does
1. Verifies required repository folders exist.
2. Configures and builds the C++ `forge_runtime` target via CMake.
3. Starts C# app entrypoint (`editor/csharp/Program.cs` via `dotnet run`) by default.
4. If runtime-only mode is used, starts the C++ runtime binary directly.

## Startup Verification Notes

### Ubuntu verification
Expected successful output includes:
- `GameForge V1 bootstrap (Ubuntu/Linux)`
- `OK - app`
- `OK - editor/csharp`
- `OK - runtime/cpp`
- `OK - ai-orchestration/python`
- `== Building Runtime Entrypoint (C++) ==`
- `== Starting C# App Entrypoint ==`
- an editor window opens

For headless launcher smoke, expect:
- `Editor launcher started successfully.`

### Windows verification
Expected successful output includes:
- `GameForge V1 bootstrap (Windows)`
- The same `OK - ...` structure checks as Ubuntu.
- `== Building Runtime Entrypoint (C++) ==`
- `== Starting C# App Entrypoint ==`
- an editor window opens

For headless launcher smoke, expect:
- `Editor launcher started successfully.`
