# GameForge V1

GameForge V1 is a **local-first, single-player, no-code-first** game creation project.

This repository currently provides the **Milestone 1 skeleton**:
- `app/` app-level module placeholder
- `editor/` C# editor placeholder
- `runtime/` C++ runtime placeholder (minimal runnable entrypoint)
- `ai-orchestration/` Python orchestration placeholder
- `docs/` setup and project docs
- `scripts/` local bootstrap scripts

## Quick Start

### Ubuntu/Linux
```bash
./scripts/bootstrap.sh
```

### Windows (PowerShell)
```powershell
pwsh -f scripts/bootstrap.ps1
```

## Minimal App Start Command

The minimal app entrypoint is built and launched from `runtime/cpp/main.cpp` by the bootstrap scripts.

Manual Ubuntu/Linux run (after bootstrap):
```bash
./build/runtime/gameforge_runtime
```
