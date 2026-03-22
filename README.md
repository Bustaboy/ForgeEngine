# ForgeEngine V1

ForgeEngine V1 is a **local-first, single-player, no-code-first** game creation stack with:
- C++ runtime (`runtime/`)
- C# editor shell (`editor/`)
- Python AI orchestration (`ai-orchestration/`)

## Quick Start

### Ubuntu/Linux
```bash
./scripts/bootstrap.sh
```

### Windows (PowerShell)
```powershell
pwsh -f scripts/bootstrap.ps1
```

## First-Run AI Setup (Mandatory Before Real Generation)

Run these once on a fresh machine:

```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --prepare-models
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --benchmark
```

This ensures local model artifacts are prepared and hardware recommendations are captured.

## First-Run Generation Pipeline Smoke

Run end-to-end generation and bot validation using the sample brief:

```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py \
  --run-generation-pipeline \
  --generate-prototype app/samples/interview-brief.sample.json \
  --output build/generated-prototypes \
  --bot-playtest-scenario app/samples/generated-prototype/cozy-colony-tales/testing/bot-baseline-scenario.v1.json
```

Then capture cross-platform smoke evidence:

```bash
PYTHONPATH=ai-orchestration/python python3 scripts/run_smoke_and_capture_evidence.py --os ubuntu --output-root build/release-evidence
# or on Windows host:
$env:PYTHONPATH="ai-orchestration/python"
python ai-orchestration/python/orchestrator.py --benchmark
python scripts/run_smoke_and_capture_evidence.py --os windows --output-root build/release-evidence
```

## One-Command Packaging

### Ubuntu (DEB + AppImage)
```bash
./scripts/package_ubuntu.sh
```

### Windows (MSI)
```powershell
pwsh -f scripts/package_windows.ps1 -Version 0.1.0
```

### macOS (DMG)
```bash
./scripts/package_macos.sh
```

See `DEPLOYMENT_GUIDE_IDIOT_PROOF.md` for zero-guesswork setup, prerequisites, and release flow.
