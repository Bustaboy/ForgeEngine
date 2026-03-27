# ForgeEngine V1 Backlog & Roadmap

**Last updated:** March 27, 2026  
**Owner:** Core Team (Runtime + Editor + Orchestration)

---

## 1) Current Status Snapshot
ForgeEngine V1 is in **late alpha preparation**.

Core foundations are in place across runtime, editor, and orchestration:
- Local-first architecture
- Scene as single source of truth (SSOT)
- Performance guardrails + lightweight mode
- Secure model manager and token handling
- Narrative + living-world simulation baseline

### Alpha Readiness Score (latest internal review)
**~84–86 / 100**

### Alpha Exit Criteria (target for alpha sign-off)
- All **P0 Immediate Polish** items closed
- **G7 (Entity → Sprite Mapping)** completed and validated in runtime/editor flow
- Basic onboarding + asset generation flow runs end-to-end without crash regressions
- Alpha readiness score reaches **≥ 92 / 100** in final review

### Major Completed Blocks
- Full Polish Round (save/load versioning, migration with backups, validation, `/validate_scene`)
- Graphics Layer **G1–G6** (Art Bible, AI generation core, asset pipeline + review flow, editor review UI, GPU-driven 2D foundation, texture loading + bindless descriptor binding)
- Narrative Layer (Story Bible, editor tools with AI sign-off, Narrator with TTS, basic automatic cutscenes, trait-aware voices)
- Dynamic Weather & Environmental Simulation (biome-aware; affects economy, movement, NPC mood, dialogs)
- Living NPC core (data model, schedules, `NPCController` routines/needs, off-screen fast-forward)
- Model Manager with secure token handling (no plain-text HF tokens)
- AI orchestration reliability improvements

---

## 2) Core Principles (Non-Negotiable)
- **Scene is the SSOT**
- **Local-first, no-code-first extensibility**
- **Minimal-change philosophy + strict backward compatibility**
- **Lightweight mode + guardrails for low-end hardware**
- **Prefer modular source assets + AI recombination over full generation**
- **User final sign-off required for major AI creative decisions**

---

## 3) Immediate Polish Before Alpha Gate
These items should be completed first because they reduce failure risk in daily use:

1. Fix model installed-state detection parity (file vs directory path checks)
2. Codify Free-Will model path SSOT policy (optional strict mode)
3. Low-resolution UX pass (relax hard minima; improve adaptive panel collapse)
4. Add integration test for asset install-state parity
5. Tighten release evidence workflow consistency (readiness metrics + smoke evidence templates)
6. Resolve documentation contract test mismatch from **PR #160** (update expected assertion or restore missing README contract string)

---

## 4) Next Major Delivery Blocks (Priority Order)

### 4.1 Graphics & Modular Content Continuation
- **G7**: AI-Generated Entity → Sprite Mapping (lookup + dynamic assignment + graceful fallback)
- **G8–G14 (Modular & Procedural Enhancements)**:
  - **G8**: Modular Kit-Bashing System (reusable modules + AI recombination for buildings/nature/props)
  - **G9**: Procedural Variations for Props & Characters
  - **G10**: Dynamic Loot / Item Generation from Templates
  - **G11**: Live Natural-Language Scene Editing
  - **G12**: Hybrid 2D ↔ Stylized 3D Toggle + basic glTF mesh loading
  - **G13**: Quality Gates + Post-Processing
  - **G14**: Advanced Consistency Tools (batch variants, IP-Adapter/ControlNet hooks)

### 4.2 Music / Audio Layer
- **M1**: Music Bible + audio upload/track management
- **M2**: AI track implementation + mood assignment
- **M3**: Runtime layered music mixer
- **M4**: Editor UI + review flow for audio
- Reuse note: follow **Art Bible**, asset-pipeline state, and review-flow patterns already proven in Graphics Layer

### 4.3 Living World & Simulation Depth
- Settlement/Village Management deepening (economy, morale, population loops)
- Smart Scene Templates (parameterized kits with AI variation; avoid copy/paste templates)
- Relationship/dialog consequence depth pass tied to weather, economy pressure, and faction state

### 4.4 Productization & Release Operations (Now visible in backlog)
- Cross-platform release hardening (Windows + Ubuntu packaging parity)
- Acceptance traceability maintenance for release gates
- Steam readiness policy calibration (critical vs warning thresholds from live playtest evidence)
- License metadata + attribution flow hardening for asset pipeline

### 4.5 Final Stretch (Post-Core Layer Completion)
- Combat polish + deeper integration (tactical grid/AP focus for Neon Collapse)
- Full AI orchestration expansion (agentic village/combat/story generation)
- Runtime efficiency enhancements (evaluate KV-cache compression and adjacent techniques; post-alpha)
- Optional cloud compute abstraction hooks (keep local-first default)

---

## 5) Backlog Ordering Rules
- Prefer **small, reviewable PRs** with clear acceptance notes
- Complete **asset mapping + modular kit-bashing** before deeper procedural/3D scope
- Scene SSOT + performance guardrails always outrank feature expansion
- Reuse existing systems first (`SpriteBatch`, Art Bible, `SceneLoader`, orchestrator stages)
- Every new user-facing layer should include:
  - editor control surface,
  - automation/test hook,
  - minimal docs update

---

## 6) Definition of Done for Backlog Items
A backlog item is only “done” when all apply:
- PR is small, focused, and has one clear objective
- Backward compatibility is preserved (older scenes still load and run)
- Scene remains the single source of truth (no side-channel state ownership introduced)
- Feature works on target local reference hardware
- Validation commands are included (tests/build/smoke as relevant) with outcomes captured
- No Scene contract regressions
- Editor/UX path is usable for no-code-first flow
- Documentation touchpoint updated (`README`, `BLUEPRINT`, `PLAYBOOK`, release docs, or backlog when major block closes)

---

## 7) Notes for Contributors
- This file tracks **active and upcoming** work only.
- Previously completed Graphics G1–G6 items remain intentionally excluded from active backlog.
- Keep this roadmap synchronized with execution/release docs after each major block lands.
- Prefer small PRs, run full validation steps from the PR template, and update this backlog when major blocks are completed.
