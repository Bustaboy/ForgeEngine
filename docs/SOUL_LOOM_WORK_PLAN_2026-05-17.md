# Soul Loom Work Plan — 2026-05-17

> **Most recent work file.** Use this as the single planning entry point when resuming the project.  
> Supersedes informal status notes from prior chats; does not replace [`BACKLOG_APRIL_2026.md`](BACKLOG_APRIL_2026.md) (sprint priorities) or [`acceptance_traceability_v1.json`](../docs/release/acceptance_traceability_v1.json) (release gates).

**Prepared:** 2026-05-17  
**Last updated:** 2026-05-18  
**Product:** Soul Loom V1 (local-first, AI-native game engine)  
**Repo baseline:** `main` @ PR [#222](https://github.com/Bustaboy/ForgeEngine/pull/222) — Soul Loom rebrand, orchestration package, editor onboarding, legacy brand removal merged.

---

## 1) Executive summary

Soul Loom has completed the **feature skeleton for Milestones 1–8** of [`SOUL_LOOM_EXECUTION_PLAN.md`](../SOUL_LOOM_EXECUTION_PLAN.md): foundation, interview, prototype generation, editor shell, genre scaffolds, asset pipeline, bot playtest, and Steam readiness **policy/UI**. **Workspace stabilization landed on `main` (2026-05-18):** `Soul.Editor`, `soul_runtime`, `orchestration/` package, onboarding UX, and **full removal** of GameForge/ForgeEngine naming (`SOUL_LOOM_*` specs, `soulloom.*` schemas, `soul_hooks.py`).

The product is **not RC-shippable** today because **P0 acceptance evidence is still incomplete** (fresh install VMs + promoted traceability rows), several **P1 UX tests are open**, and **active-development epics** (kit-bashing depth, adaptive music, scene templates, cloud abstraction) are unfinished.

**Strategic goal for the next phase:** promote archived smoke/install evidence into traceability JSON (Phase 1), expand CI to full test suites (Phase 2), then land one high-value gameplay epic from the April backlog without scope creep.

---

## 2) Source-of-truth map

| Need | Document |
|---|---|
| Product vision & scope lock | [`SOUL_LOOM_V1_BLUEPRINT.md`](../SOUL_LOOM_V1_BLUEPRINT.md), [`SOUL_LOOM_DECISIONS_LOCK.md`](../SOUL_LOOM_DECISIONS_LOCK.md) |
| Milestone history & sprint rhythm | [`SOUL_LOOM_EXECUTION_PLAN.md`](../SOUL_LOOM_EXECUTION_PLAN.md) |
| Sprint priorities & AI role routing | [`docs/BACKLOG_APRIL_2026.md`](BACKLOG_APRIL_2026.md) |
| Release pass/fail (AT-001..031) | [`docs/release/acceptance_traceability_v1.json`](../docs/release/acceptance_traceability_v1.json) |
| Feature status (stable vs in dev) | [`README.md`](../README.md) |
| CI / test expectations | [`docs/testing/TEST_POLICY.md`](testing/TEST_POLICY.md) |
| **This plan** | **`docs/SOUL_LOOM_WORK_PLAN_2026-05-17.md`** |

**Deprecated (do not plan from):** [`backlog.md`](../backlog.md), [`docs/release/RC_HANDOFF_V1.md`](release/RC_HANDOFF_V1.md) (frozen 2026-03-22; AT-003/AT-026 status is stale vs live JSON).

---

## 3) Current state snapshot (2026-05-18)

### 3.1 Milestones vs execution plan

| Milestone | Plan deliverable | Status | Notes |
|---|---|---|---|
| M1 Foundation | Repo, ADRs, bootstrap, project lifecycle | **Done** | `tests/test_m1_skeleton.py`, `tests/test_project_lifecycle.py` |
| M2 Interview | Long Q&A, 3 options, think-for-me, brief export | **Done** | C# + Python contract tests |
| M3 Prototype | One-click scaffold, partial regen, lock protection | **Done** | `app/samples/generated-prototype/cozy-colony-tales/` |
| M4 Editor + co-pilot | Panels, lock/undo, previews, selection context | **Mostly done** | AT-029/030/031 UX gaps |
| M5 Genre kits | RTS/sim + RPG playable samples | **Scaffold done** | JSON modules exist; tuning/playability depth ongoing |
| M6 Assets | Import, tagging, license, Art Bible | **Done** | `tests/test_asset_import_pipeline.py` |
| M7 Bot playtest | Reports, dead-end detection | **Done** | AT-026 covered in traceability |
| M8 Steam + RC | Readiness score, gates, packaging, audit | **Policy done; evidence incomplete** | Upload is **stub** only; perf metrics use **fixtures** |

### 3.2 Acceptance test rollup (live JSON)

| Priority | Total | Covered | Partial | Missing |
|---|---:|---:|---:|---:|
| P0 | 14 | 11 | **3** | 0 |
| P1 | 14 | 9 | 3 | **2** |
| P2 | 3 | 0 | 0 | **2** |

**P0 partial (RC blockers):**

| ID | Gap | Next action |
|---|---|---|
| AT-001 | No clean-machine install evidence | Run fresh install on Windows 11 + Ubuntu 22.04 VMs; archive logs under `docs/release/evidence/runs/` |
| AT-010 | Windows smoke **run passed locally**; not yet promoted in traceability JSON | Local PASS: `docs/release/evidence/runs/20260518T041200Z/` (gitignored). Archive copy + set `status: covered` in traceability PR |
| AT-011 | Ubuntu smoke not archived | `py -3.11 scripts/run_smoke_and_capture_evidence.py --os ubuntu --output-root docs/release/evidence/runs` on Ubuntu 22.04 host |

**P1 partial:**

| ID | Gap | Next action |
|---|---|---|
| AT-005 | Think-for-me coherence | Manual 5-prompt review + tighten test fixtures |
| AT-012 | Vulkan init E2E | Target-hardware startup verification note in evidence |
| AT-030 | Simple vs advanced inspector default | Add UI harness test for tab visibility state |

**P1/P2 missing (non-RC but planned):**

| ID | Gap | Next action |
|---|---|---|
| AT-027 | Git OFF by default | Editor integration test on new project |
| AT-028 | Git opt-in | Test enable-Git flow + failure messaging |
| AT-029 | Top-level navigation discoverability | Fixture-backed nav test + one manual walkthrough |
| AT-031 | Plain-language copy | Copy fixture assertions + UX review pass |

### 3.3 README “in active development”

| Area | Code today | Plan phase |
|---|---|---|
| Kit-bashing | `kit_bashing.py`, tests, editor hook | Phase 3 (product) |
| Adaptive music | `AudioSystem.cpp` track switching only | Phase 3 (product) |
| Scene templates | Not implemented | Phase 4 (post-RC or parallel if resourced) |
| Cloud compute abstraction | Not implemented | Phase 4 (foundation only) |

### 3.4 Workspace hygiene — **complete (2026-05-18)**

Merged to `main` via PR #222:

| Area | Status |
|---|---|
| Rebrand (`Soul.Editor`, `soul_runtime`, `SOUL_LOOM_*` env) | **Merged** |
| Orchestration package split + `soul_hooks.py` | **Merged** |
| Editor onboarding, theme, MainWindow splits, diagnostics | **Merged** |
| Legacy brand purge (`GAMEFORGE_*` → `SOUL_LOOM_*`, no `FORGEENGINE_*` fallbacks) | **Merged** |
| Local test verification (pre-merge) | **88** pytest passed, **4** skipped; **61** `Soul.Editor.Tests` passed |
| Windows smoke capture (pre-merge) | **PASS** AT-010 run `20260518T041200Z` (evidence local only) |

**Hygiene note:** delete stale `.forgeengine/` / `forgeengine.log` locally if present; they are no longer part of the product layout.

---

## 4) Phased work plan

### Phase 0 — Stabilize workspace — **DONE (2026-05-18)**

**Goal:** Clean branch so CI and docs match reality.

| # | Task | Status |
|---|---|---|
| 0.1 | Review uncommitted diff; keep `build-g7/` out of git | **Done** — `.gitignore` updated |
| 0.2 | Rebrand (`Soul.Editor`, `soul_runtime`, `SOUL_LOOM_*` env) | **Done** — merged #222 |
| 0.3 | Orchestration package split (`orchestration/`, `soul_hooks.py`) | **Done** — merged #222 |
| 0.4 | Editor onboarding / theme / diagnostics / test fixes | **Done** — merged #222 |
| 0.5 | README “Project Documents” + `SOUL_LOOM_*` spec filenames | **Done** — merged #222 |
| 0.6 | Remove legacy GameForge/ForgeEngine branding entirely | **Done** — `9b7e0f8` in #222 |

**Exit criteria:** **Met** — `main` includes PR #222; local full suites green before merge.

---

### Phase 1 — RC evidence closure (3–5 days)

**Goal:** All **P0** acceptance rows **covered** with archived evidence (not fixture-only where manual is required).

| # | Task | AT | Steps | Evidence artifact |
|---|---|---|---|---|
| 1.1 | Windows smoke run | AT-010 | Follow [`CROSS_PLATFORM_SMOKE_RUNBOOK.md`](release/CROSS_PLATFORM_SMOKE_RUNBOOK.md); run capture script | **Local PASS** `20260518T041200Z` — **archive** to tracked evidence + promote JSON |
| 1.2 | Ubuntu smoke run | AT-011 | Same on Ubuntu 22.04 host or CI runner with GPU where needed | `.../runs/<timestamp>-ubuntu/` |
| 1.3 | Fresh install validation | AT-001 | Clean VM: clone → `Setup-Alpha.ps1` / `setup.sh` → launch editor → launcher smoke | Install log + first-launch screenshot or log |
| 1.4 | Promote traceability JSON | AT-010, AT-011, AT-001 | Set `status: covered`, update `last_verified_utc`, `evidence_strength` | PR updating `acceptance_traceability_v1.json` + `.md` mirror |
| 1.5 | Target-hardware perf capture (stretch) | AT-020–024 | Run `scripts/collect_readiness_metrics.py` on RTX 2070 / i5 / 16 GB box | Replace synthetic fixtures where possible |

**Exit criteria (RC gate per acceptance matrix):**

- All **P0** = `covered` in traceability JSON  
- `python scripts/validate_traceability.py` passes  
- Evidence index updated: [`docs/release/evidence/INDEX.md`](release/evidence/INDEX.md)

---

### Phase 2 — CI and test hardening (2–4 days)

**Goal:** Expand PR gate per [`TEST_POLICY.md`](testing/TEST_POLICY.md) without flaking.

| # | Task | Rationale | Done when |
|---|---|---|---|
| 2.1 | Run full `pytest tests/` locally; fix or quarantine reds | Only subset runs in PR today | **Done locally** — 88 passed, 4 skipped (2026-05-18); wire into PR gate |
| 2.2 | Run full `dotnet test Soul.Editor.Tests.csproj` | Same | **Done locally** — 61 passed Release (2026-05-18); wire into PR gate |
| 2.3 | Update `.github/workflows/pr-validation.yml` | Policy expansion target | PR runs full stable Python + C# suites |
| 2.4 | Add AT-027/028 Git default tests | P2 but low effort | New editor tests committed |
| 2.5 | Add AT-030 inspector tab visibility test | P1 partial closure | Test in `EditorShellTests.cs` |

**Exit criteria:** PR validation runs expanded suites; no new flaky skips without ADR/note in TEST_POLICY.

---

### Phase 3 — Product epic (pick ONE for next 2-week sprint)

Choose **one** primary epic; treat others as stretch. Aligns with [`BACKLOG_APRIL_2026.md`](BACKLOG_APRIL_2026.md) high priority.

#### Option A — Weather system depth (recommended if “living world” is the demo story)

| # | Task | Acceptance |
|---|---|---|
| A.1 | Document weather → NPC movement / morale / economy / combat matrix | Editor tooltips + `art_bible.json` notes |
| A.2 | Wire editor Weather panel to show systemic multipliers live | Panel reflects runtime state |
| A.3 | Add regression tests for profile transitions | Python or C# contract tests |
| A.4 | Perf check: 60 FPS on reference scene with storm + 20 NPCs | Log in evidence or readiness metrics |

#### Option B — 7-stage pipeline polish (recommended if AI reliability is the pain point)

| # | Task | Acceptance |
|---|---|---|
| B.1 | Finish orchestration refactor (`orchestration/` package) with stable public CLI | **Mostly done** — merged #222; verify CLI docs + remove placeholder prints |
| B.2 | Sequential model load/unload for 8/12/16 GB VRAM tiers | Benchmark doc updated |
| B.3 | Harden error surfaces in editor AI Orchestration panel | User-visible retry + manual failover preserved |
| B.4 | Bot playtest hook always runs on `--run-generation-pipeline` in smoke | AT-006 smoke path unchanged |

#### Option C — Adaptive music layer (recommended if demo needs audio punch)

| # | Task | Acceptance |
|---|---|---|
| C.1 | Define cue map: weather, combat, tension, settlement pressure | JSON schema in project config |
| C.2 | Extend `AudioSystem.cpp` blending beyond track swap | Crossfade or layer bus documented |
| C.3 | Editor controls for music profile (minimal) | Panel or inspector section |
| C.4 | No regression on AT-021/022 perf gates | Metrics collector still passes |

**Sprint exit:** One vertical demo path recorded (short screen capture or smoke notes).

---

### Phase 4 — Post-RC / parallel foundations (backlog)

Schedule after Phase 1 exit or in parallel if second contributor available.

| Epic | Key deliverables | Doc reference |
|---|---|---|
| Scene templates | village / dungeon / overworld / arena starters | README in active dev; backlog §lower |
| Cloud abstraction foundation | provider interface, job contract, no first-party hosting | Blueprint §14; backlog medium |
| Scene schema versioning | version fields, migration path for scene JSON | backlog medium |
| Accessibility baseline | keyboard, focus, contrast, automation names | `SOUL_LOOM_UX_FOUNDATIONS.md` |
| Generation safety | path safety, untrusted JSON, HF token handling | backlog medium |
| Kit-bashing upgrades | consistency scoring, editor validation | `kit_bashing.py` + tests |
| Player persistence (exported games) | clarify scope vs editor save | backlog lower |
| `.cursor/rules/registry.mdc` | AI role → model map for Cursor | backlog ongoing |
| Real README screenshots | replace placeholders | backlog lower |

---

## 5) UX and copy closure (P1)

Tracked separately because they affect non-coder RC narrative but do not block P0.

| # | Task | AT | Reference |
|---|---|---|---|
| 5.1 | Navigation discoverability test fixture | AT-029 | `SOUL_LOOM_UX_FOUNDATIONS.md` §Information Architecture |
| 5.2 | Plain-language copy audit of core dialogs | AT-031 | `SOUL_LOOM_UX_COPY_STANDARDS_V1.md` |
| 5.3 | Verify Project Home → Interview → Prototype → Editor → Testing → Publish flow without docs | AT-029 manual | One recorded walkthrough |

---

## 6) Known stubs and technical debt (do not forget)

| Item | Location | Risk | Action |
|---|---|---|---|
| Steam upload stub | `MainWindowViewModel.RunSteamUploadStubAsync` | Users may think publish is live | Label clearly in UI until API exists |
| ComfyUI consistency placeholders | `ai-orchestration/python/consistency.py` | Style drift on generated assets | Wire or gate behind feature flag |
| Performance metrics fixtures | `collect_readiness_metrics.py` + sample JSON | False confidence on RC | Phase 1.5 target-hardware capture |
| Orchestrator placeholder print | `orchestration/core.py` | Confusing CLI output | Remove or route to structured logging |
| PR gate subset only | `pr-validation.yml` | Regressions slip through | Phase 2 |
| Scene templates | none | README promise gap | Phase 4 or descope in README until started |

---

## 7) Recommended execution order (summary)

```
Phase 0  →  DONE (PR #222 on main)
Phase 1  →  AT-001, AT-010 archive + AT-011 Ubuntu smoke + traceability promotion
Phase 2  →  CI expansion (2.1/2.2 green locally) + AT-027/028/030 tests
Phase 3  →  ONE epic: Weather (A) | Pipeline polish (B) | Music (C)
Phase 4  →  templates, cloud, a11y, schema versioning (parallel backlog)
Phase 5  →  UX/copy AT-029/031
```

**Estimated calendar time to RC-ready (evidence only):** 1–2 weeks focused part-time, assuming VMs and hardware access.

**Estimated calendar time to “strong V1 demo”:** +2–4 weeks after RC for one Phase 3 epic + Phase 5 UX.

---

## 8) Sprint board template (copy into weekly notes)

### Sprint goal (fill in)
_Example: Close P0 smoke evidence and land weather depth MVP._

### To Do
- [ ] Phase 1.1b: archive Windows smoke `20260518T041200Z` + promote AT-010 in traceability JSON
- [ ] Phase 1.2: Ubuntu smoke run + evidence
- [ ] Phase 1.3: fresh install validation (AT-001) on clean VMs
- [ ] Phase 1.4: traceability JSON promotion PR
- [ ] Phase 2.3: expand `pr-validation.yml` to full pytest + full `Soul.Editor.Tests`
- [ ] Phase 3: epic tasks (A/B/C — pick one)

### In Progress
- [ ] Phase 1 — RC evidence closure

### Blocked
- [ ] _blocker → owner_

### Done
- [x] Phase 0 — workspace stabilization + legacy brand removal (2026-05-18, PR #222)
- [x] Full local pytest + Soul.Editor.Tests before merge (2026-05-18)
- [x] Windows smoke capture script PASS locally (20260518T041200Z; pending archive)

### Risks this sprint
| Risk | Mitigation |
|---|---|
| No Ubuntu hardware | Use GitHub-hosted runner + contract-only smoke where GPU absent |
| WIP merge conflicts | Phase 0 first, small commits |
| Scope creep on weather | Cap at 4 systemic links + editor visibility; defer combat edge cases |

---

## 9) KPI checkpoints (from execution plan §7)

Track after Phase 1 smoke on reference hardware:

| KPI | Target | How to measure |
|---|---|---|
| Blank project → first playable prototype | < 45 min | Timed internal run with sample brief |
| Non-coder E2E completion rate | ≥ 60% | 3–5 participant sessions post UX phase |
| Critical crash rate (core workflow) | < 3% sessions | Readiness metrics / crash handler logs |
| “I can change what I see” confidence | ≥ 8/10 | Post-session survey after Phase 5 |

---

## 10) Document maintenance

When this plan is superseded:

1. Create `docs/SOUL_LOOM_WORK_PLAN_YYYY-MM-DD.md` with the new date in the title.  
2. Add a one-line pointer at the top of **this** file: `Superseded by SOUL_LOOM_WORK_PLAN_<new-date>.md`.  
3. Update README “Project Documents” table to link the newest file only.

---

## Changelog

| Date | Change |
|---|---|
| 2026-05-18 | Phase 0 marked complete (PR #222 on `main`); documented local test/smoke results; AT-010 Windows run PASS pending traceability promotion; Phase 2.1/2.2 local green; legacy brand purge noted complete. |
| 2026-05-17 | Initial work plan created from codebase + doc audit (post-hiatus status review). |
