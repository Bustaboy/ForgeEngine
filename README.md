# GameForge V1

GameForge V1 is a **local-first, single-player, no-code-first** game creation project.

This repository currently provides the **Milestone 1 skeleton**:
- `app/` app startup/module boundary docs
- `editor/` C# editor launcher shell (main app entrypoint)
- `runtime/` C++ runtime placeholder (generated game runtime entrypoint)
- `ai-orchestration/` Python orchestration placeholder (tooling/automation only)
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

## Optional Runtime-Only Verification

If .NET SDK is not available yet, you can still verify the C++ runtime build path:

```bash
./scripts/bootstrap.sh --runtime-only
```
