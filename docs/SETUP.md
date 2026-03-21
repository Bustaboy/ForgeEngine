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
- Python 3.10+ installed and available as `python3` (Ubuntu) or `python` (Windows).
- Git.

> Note: C++ and C# placeholders are included as module skeletons, but bootstrap only requires Python for now.

## Bootstrap Command

### Ubuntu
```bash
git clone <repo-url>
cd ForgeEngine
python3 scripts/bootstrap.py
```

### Windows (PowerShell)
```powershell
git clone <repo-url>
cd ForgeEngine
python scripts/bootstrap.py
```

## What Bootstrap Does
1. Prints environment details.
2. Verifies required repository folders exist.
3. Starts the minimal app entrypoint (`app/main.py`) unless `--skip-run` is passed.

## Startup Verification Notes

### Ubuntu verification
Expected successful output includes:
- `GameForge V1 local bootstrap`
- `OK - app`
- `OK - editor/csharp`
- `OK - runtime/cpp`
- `OK - ai-orchestration/python`
- `App started successfully.`
- `Bootstrap completed successfully.`

### Windows verification
Expected successful output includes the same status lines as Ubuntu.
Use either:
- `python scripts/bootstrap.py`
- `./scripts/bootstrap.ps1`

## Manual Minimal App Run

Ubuntu:
```bash
python3 app/main.py
```

Windows (PowerShell):
```powershell
python app/main.py
```
