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

## Prerequisites
### Ubuntu
- `g++` with C++17 support.
- Git.

### Windows
- PowerShell 7+ (`pwsh`) recommended.
- MinGW-w64 `g++` with C++17 support available in PATH.
- Git.

> Python is reserved for AI orchestration/tooling flows and is **not** a hard dependency for core runtime bootstrapping in this milestone.

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

## What Bootstrap Does
1. Verifies required repository folders exist.
2. Compiles the minimal runtime app entrypoint from `runtime/cpp/main.cpp`.
3. Starts the minimal app binary.

## Startup Verification Notes

### Ubuntu verification
Expected successful output includes:
- `GameForge V1 bootstrap (Ubuntu/Linux)`
- `OK - app`
- `OK - editor/csharp`
- `OK - runtime/cpp`
- `OK - ai-orchestration/python`
- `GameForge V1 minimal app (C++ runtime)`
- `App started successfully.`
- `Bootstrap completed successfully.`

### Windows verification
Expected successful output includes:
- `GameForge V1 bootstrap (Windows)`
- The same `OK - ...` structure checks as Ubuntu.
- `GameForge V1 minimal app (C++ runtime)`
- `App started successfully.`
- `Bootstrap completed successfully.`

## Manual Minimal App Run

### Ubuntu/Linux
```bash
./build/runtime/gameforge_runtime
```

### Windows (PowerShell)
```powershell
.\build\runtime\gameforge_runtime.exe
```
