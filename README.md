# ForgeEngine V1

**ForgeEngine** is a local-first, AI-native game creation engine that lets hobby creators and indie studios build single-player games without writing code. The engine combines a high-performance **C++ Vulkan runtime**, a **C# visual editor** (Avalonia), and a **Python AI orchestration layer** that handles everything from deep design interviews to prototype generation, NPC behavior, asset pipeline, and Steam readiness checks — all running entirely on your own hardware. What makes it distinct is the tight loop between living-world simulation (NPCs with real schedules and free-will sparks, dynamic weather, economy, and settlement layers) and an AI generation pipeline guided by a per-project Art Bible, so the output is always consistent with your creative vision.

---

## Features

### Core Gameplay Systems
- **Day/Night cycle** — configurable cycle speed and day counter, editor panel with live scene sync
- **Building system** — grid-based placement with ghost preview and inventory cost validation before committing a build
- **Living NPCs** — schedule-driven agents with a probabilistic free-will spark system (LLM-backed); up to 3 spontaneous behavior sparks per NPC per day with per-NPC cap and cooldown, visible in the editor's NPC panel
- **Settlement layer** — village morale, food/shared stockpile, population tracking, and economy pressure feeding into NPC behavior and dialog tone
- **Tactical combat** — turn-aware unit state machine with inventory integration and relationship-aware resolution
- **Weather system** — 7 dynamic profiles (sunny, rain, storm, snow, sandstorm, fog, windy), each modifying movement speed, economy multipliers, relationship drift, and NPC dialog tone in real time
- **Relationship system** — persistent NPC-to-player and NPC-to-NPC relationship scores that affect dialog branches, weather drift, and combat outcomes
- **Navmesh** — runtime pathfinding for NPC movement and combat unit routing
- **Dialog + Story systems** — branching dialog trees with narrator layer, consequence tracking, and branch visualization

### AI Orchestration Pipeline
- **7-stage generation pipeline**: Story Analysis → Concept Doc → Asset Planning → Code Generation → Integration → Bot Validation → Export
- **Deep AI interview engine** — batched Q&A with uncertainty detection, "think of something" mode (returns 3 options), and long-session continuity; outputs structured JSON + markdown design brief
- **Art Bible-guided asset generation** — per-project `art_bible.json` encodes art direction, rendering keywords, palette, lighting, and composition constraints; every AI image prompt is automatically enhanced before submission
- **Asset review flow** — generated graphics assets start in `pending-review` state; each carries a review block (`decision`, `reviewer`, `timestamp_utc`) before being promoted to the asset library
- **Performance guardrails** — 60 FPS target on reference hardware (RTX 2070 / i5 / 16 GB), with a hard fail gate below 30 FPS sustained; scene load must complete in under 20 seconds; crash-free session target ≥ 97%
- **Co-creator system** — AI co-pilot understands the current editor selection and can propose, preview, and apply targeted scene edits with undo protection
- **Bot playtest harness** — automated validation with AI-generated playtest reports covering progression blockers, economy stress, quest dead-ends, and pacing anomalies

### Editor (C# / Avalonia)
- Panels: Day/Night, Building, Living NPCs, Weather, Dialog, Story, Inventory Recipes, AI Orchestration, Co-Creator
- Simple + Advanced inspector tabs; lock/unlock system; deep undo/redo timeline
- Before/after previews for significant AI edits
- Playtest Report Viewer; Steam Readiness Score (0–100) with critical/warning gate logic
- Commercial project declaration flow (free non-commercial; 5% revenue share after first $1,000 gross)

---

## Quick Start

### Prerequisites
- Windows 10/11 or Ubuntu 22.04+
- GPU: RTX 2070-class or better (Vulkan 1.2+)
- RAM: 16 GB
- .NET 8 SDK, CMake ≥ 3.25, Ninja, Python 3.10+, Vulkan SDK

### 1. One-command setup

**Windows (PowerShell):**
```powershell
pwsh -f ./scripts/Setup-Alpha.ps1
```
Installs .NET 8, CMake, Ninja, Python, Vulkan SDK, and MSYS2/MinGW automatically, then bootstraps the full environment.

**Linux / macOS:**
```bash
chmod +x scripts/setup.sh && ./scripts/setup.sh
```

Force a full clean reset (fresh venv + model prep):
```powershell
pwsh -f scripts/Setup-Alpha.ps1 -Fresh   # Windows
./scripts/setup.sh --fresh               # Linux/macOS
```

### 2. Prepare local AI models (first run only)
```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --prepare-models
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py --benchmark
```

### 3. Run the editor
```bash
dotnet run --project editor/csharp/GameForge.Editor.csproj
```

### 4. Generate your first asset or scene
Run the full generation pipeline from a design brief:
```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py \
  --run-generation-pipeline \
  --generate-prototype app/samples/interview-brief.sample.json \
  --output build/generated-prototypes
```

Run end-to-end smoke with bot validation:
```bash
PYTHONPATH=ai-orchestration/python python3 ai-orchestration/python/orchestrator.py \
  --run-generation-pipeline \
  --generate-prototype app/samples/interview-brief.sample.json \
  --output build/generated-prototypes \
  --bot-playtest-scenario app/samples/generated-prototype/cozy-colony-tales/testing/bot-baseline-scenario.v1.json
```

Capture cross-platform smoke evidence:
```bash
PYTHONPATH=ai-orchestration/python python3 scripts/run_smoke_and_capture_evidence.py \
  --os ubuntu --output-root build/release-evidence
```

### 5. Package for distribution
```bash
./scripts/package_ubuntu.sh           # DEB + AppImage
pwsh -f scripts/package_windows.ps1 -Version 0.1.0   # MSI
./scripts/package_macos.sh            # DMG
```

See [`DEPLOYMENT_GUIDE_IDIOT_PROOF.md`](DEPLOYMENT_GUIDE_IDIOT_PROOF.md) for zero-guesswork setup, prerequisites, and release flow.

---

## Status

### Working today
| Area | Status |
|---|---|
| C++ Vulkan runtime (core loop, scene, input, camera) | Stable |
| Day/Night cycle | Stable |
| Building system with ghost placement | Stable |
| Inventory system | Stable |
| Living NPCs — schedule + free-will sparks | Stable |
| Settlement layer (morale, economy, population) | Stable |
| Tactical combat | Stable |
| Weather system (7 profiles, systemic effects) | Stable |
| Relationship system | Stable |
| Navmesh + pathfinding | Stable |
| Dialog + Story + Narrator systems | Stable |
| C# editor shell with all current panels | Stable |
| Python AI orchestration + 7-stage pipeline | Stable |
| AI interview engine (uncertainty + "think for me") | Stable |
| Art Bible prompt enhancement | Stable |
| Asset review flow (pending-review state machine) | Stable |
| Bot playtest harness + report | Stable |
| Steam Readiness Score + gate logic | Stable |
| One-command setup scripts (Windows + Linux) | Stable |

### In active development
| Area | Notes |
|---|---|
| **Graphics modular kit-bashing** | Procedural prop assembly from a shared part library |
| **Music layer** | Adaptive soundtrack system tied to world state and combat |
| **Scene Templates** | Curated starting points (village, dungeon, overworld, arena) |
| **Cloud compute abstraction** | Provider plug-in layer for optional remote generation jobs |

---

## Screenshots

> _Replace image paths with actual captures when available._

| | |
|---|---|
| ![Living NPCs with dynamic schedules](docs/screenshots/living-npcs-placeholder.png) | **Living NPCs with dynamic schedules** — NPCs follow daily routines, deviate via free-will sparks, and react to weather, morale, and settlement economy in real time. |
| ![AI Asset Review Pipeline in editor](docs/screenshots/asset-review-placeholder.png) | **AI Asset Review Pipeline in editor** — Generated assets enter a `pending-review` queue; reviewers approve or reject before assets enter the project library, keeping Art Bible consistency enforced. |
| ![Building system with ghost preview](docs/screenshots/building-ghost-placeholder.png) | **Building system with ghost preview** — Place structures on a grid with a translucent ghost preview that shows footprint and real-time inventory cost before committing. |
| ![Day/Night cycle controls](docs/screenshots/day-night-placeholder.png) | **Day/Night cycle** — Configurable speed and progress exposed directly in the editor panel, with downstream effects on lighting, NPC schedules, and weather transitions. |

---

## Project Documents

| Document | Purpose |
|---|---|
| [`GAMEFORGE_V1_BLUEPRINT.md`](GAMEFORGE_V1_BLUEPRINT.md) | Product vision, guiding principles, feature targets, technical scope, and MVP definition |
| [`CODEX_PROMPT_PLAYBOOK.md`](CODEX_PROMPT_PLAYBOOK.md) | Codex-optimized prompts for milestone-by-milestone implementation, reusable task templates, and scope discipline rules |
| [`GAMEFORGE_EXECUTION_PLAN.md`](GAMEFORGE_EXECUTION_PLAN.md) | Sprint-level milestone map, risk register, KPI targets, and team operating rhythm |
| [`GAMEFORGE_UX_FOUNDATIONS.md`](GAMEFORGE_UX_FOUNDATIONS.md) | UX wireframe baseline, interaction patterns, and no-coder-first labeling standards |
| [`DEPLOYMENT_GUIDE_IDIOT_PROOF.md`](DEPLOYMENT_GUIDE_IDIOT_PROOF.md) | Zero-guesswork setup, prerequisites, and release flow |

---

## License

Free for non-commercial use. Commercial projects (sold games or games containing MTX) are subject to a **5% revenue share after the first $1,000 gross revenue per game**. See [`GAMEFORGE_V1_BLUEPRINT.md`](GAMEFORGE_V1_BLUEPRINT.md) §12 for full terms.
