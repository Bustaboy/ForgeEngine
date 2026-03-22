# ForgeEngine Deployment Guide (Idiot-Proof)

This guide is intentionally explicit. Follow it line-by-line.

## 0) What this produces

- **Windows installer:** `.msi`
- **Ubuntu installers:** `.deb` and `.AppImage`
- **macOS installer:** `.dmg`
- **Proof the build is real:** benchmark + generation pipeline + smoke evidence

## 1) Prerequisites

## Windows build host
Install:
1. .NET 8 SDK
2. Python 3.10+
3. MinGW-w64 (`g++`)
4. WiX Toolset v4 (`wix` CLI)

Quick checks:
```powershell
dotnet --version
python --version
g++ --version
wix --version
```

## Ubuntu build host
Install:
1. .NET 8 SDK
2. Python 3.10+
3. `g++`
4. `dpkg-deb`
5. `appimagetool`

Quick checks:
```bash
dotnet --version
python3 --version
g++ --version
dpkg-deb --version
appimagetool --version
```

## macOS build host
Install:
1. .NET 8 SDK
2. Python 3.10+
3. Xcode command line tools (`clang++`, `hdiutil`)

## 2) First-run mandatory AI prep

Run once per machine:

```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --prepare-models
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --benchmark
```

Expected: JSON output with model status and benchmark hardware summary.

## 3) One-command packaging

## Ubuntu (DEB + AppImage + validation)
```bash
./scripts/package_ubuntu.sh
```

What it does automatically:
1. Builds runtime C++ binary
2. Publishes .NET editor
3. Stages bundle payload
4. Runs post-build validation:
   - `--prepare-models`
   - `--benchmark`
   - `--run-generation-pipeline` with `app/samples/interview-brief.sample.json`
   - `scripts/run_smoke_and_capture_evidence.py --os ubuntu`
5. Produces `.deb`
6. Produces `.AppImage`
7. Writes `release_manifest.json`

Artifacts land in:
- `build/release/linux-x64/`

## Windows (MSI + validation)
```powershell
pwsh -f scripts/package_windows.ps1 -Version 0.1.0
```

What it does automatically:
1. Builds runtime C++ binary
2. Publishes .NET editor
3. Stages bundle payload
4. Runs post-build validation:
   - `--prepare-models`
   - `--benchmark`
   - `--run-generation-pipeline` with sample brief
   - `scripts/run_smoke_and_capture_evidence.py --os windows`
5. Builds MSI with WiX
6. Writes `release_manifest.json`

Artifacts land in:
- `build/release/win-x64/`

## macOS (DMG)
```bash
./scripts/package_macos.sh
```

Artifacts land in:
- `build/release/osx-arm64/`

## 4) GitHub Releases automation

Workflow file:
- `.github/workflows/release-packaging.yml`

Triggers:
1. Manual dispatch (`workflow_dispatch`) with `version`
2. Git tag push (`v*`)

What CI does:
- Builds on Ubuntu, Windows, macOS
- Runs packaging scripts per host
- Uploads artifacts
- On tagged releases, publishes installer assets to GitHub Release

## 5) Manual verification commands

Run before requesting release approval:

```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --prepare-models
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --benchmark
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py \
  --run-generation-pipeline \
  --generate-prototype app/samples/interview-brief.sample.json \
  --output build/generated-prototypes \
  --bot-playtest-scenario app/samples/generated-prototype/cozy-colony-tales/testing/bot-baseline-scenario.v1.json
PYTHONPATH=ai-orchestration/python python3 scripts/run_smoke_and_capture_evidence.py --os ubuntu --output-root build/release-evidence
```

On Windows host:
```powershell
$env:PYTHONPATH="ai-orchestration/python"
python ai-orchestration/python/orchestrator.py --prepare-models
$env:PYTHONPATH="ai-orchestration/python"
python ai-orchestration/python/orchestrator.py --benchmark
python scripts/run_smoke_and_capture_evidence.py --os windows --output-root build/release-evidence
```

## 6) Fast failure checklist

If build fails:
1. `dotnet` missing -> install .NET 8 SDK
2. `g++` missing -> install compiler toolchain
3. `wix` missing on Windows -> install WiX v4
4. `appimagetool` missing on Ubuntu -> install AppImageKit tool
5. `--run-generation-pipeline` failing -> inspect JSON output and generated logs under `build/generated-prototypes`
6. smoke evidence failing -> inspect logs under `build/release-evidence/runs/<timestamp>/`

This deployment flow is aligned with native desktop priority: Windows + Ubuntu first, with automated installer artifacts and end-to-end generation validation.
