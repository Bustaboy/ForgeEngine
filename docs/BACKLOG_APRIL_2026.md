# Soul Loom Backlog — April 2026

**Scope:** Full role registry integration, sprint-ready priorities, and follow-ups from product/code review (README, `TEST_POLICY.md`, blueprint, and execution plan alignment) for **Soul Loom**.

**Related documents:** [`README.md`](../README.md), [`SOUL_LOOM_V1_BLUEPRINT.md`](../SOUL_LOOM_V1_BLUEPRINT.md), [`SOUL_LOOM_EXECUTION_PLAN.md`](../SOUL_LOOM_EXECUTION_PLAN.md), [`docs/testing/TEST_POLICY.md`](testing/TEST_POLICY.md).

---

## AI role registry (complete list)

Use these names consistently in routing tables and in `ai-orchestration/python/model_manager.py` (and future `.cursor/rules/registry.mdc`).

Interviewer, Director/Planner, Coder, Writer, Asset Prompter, Graphics Implementer, Consistency Guard, QA Tester/Playtester, Janitor, Style Enforcer, Edge Case Thinker, Refinement Specialist, Scope Guardian, Cross-Content Checker, Player Experience Simulator, Technical Debt Auditor, Level Designer, Balance/Economy Designer, Sound/Audio Designer, UI/UX Designer, Narrative Consistency Keeper, Performance Advisor, Voice Direction Specialist, Localization Coordinator, Tutorial Designer, Data Analyst, Build/Release Engineer, Marketing Agent, Community Feedback Synthesizer.

---

## High priority (next 1–2 sprints)

### Dynamic weather system expansion

Extend weather so types and transitions clearly impact **NPC movement**, **morale / dialog tone**, **economy pacing**, **day/night interplay**, and **combat**. (The runtime already exposes multiple profiles; this epic is **depth, tuning, and editor visibility**, not a from-scratch subsystem.)

- Non-intrusive **C# Avalonia** editor previews and ghost/simulation hooks where useful.
- **Procedural kit-bashing** and Art Bible **consistency** under weather-driven lighting/mood.
- **Strict 60 FPS Vulkan** path; validate on reference hardware per README guardrails.

**Role routing:** Director/Planner → Coder (Qwen 3 Coder), Writer (Mistral Magistral / Large Creative), Graphics Implementer + Asset Prompter (Flux / SD3.5 + Gemma 4), Consistency Guard (Gemma 4), QA Tester/Playtester (Qwen 3 Instruct), Performance Advisor (Qwen 3 Coder), Player Experience Simulator (Mistral), Edge Case Thinker (DeepSeek V3) as needed.

### Adaptive music and dynamic audio layer

Tie **music cues**, **SFX**, and **real-time blending** to world state: weather, relationships, tension, combat, and settlement pressure. Support **localization-friendly** voice direction and atmospheric writing.

**Role routing:** Director/Planner → Sound/Audio Designer (Mistral Magistral / Large Creative), Coder (Qwen 3 Coder), Writer + Voice Direction Specialist (Mistral), UI/UX Designer (Gemma 4), Player Experience Simulator, Consistency Guard, Localization Coordinator when strings/voice specs change.

### 7-stage AI generation pipeline — polish and optimization

Improve **orchestration**, **Art Bible scoring**, **refinement loops**, **error handling**, and **bot playtesting** hooks. Reduce **VRAM spikes** via sequential load / infer / unload and clearer model lifecycle.

**Role routing:** Director/Planner → Refinement Specialist (Qwen 3 Instruct), Consistency Guard (Gemma 4), QA Tester/Playtester + Data Analyst (Qwen 3 Instruct), Janitor + Technical Debt Auditor (Qwen 3 Coder), Performance Advisor, Build/Release Engineer (Qwen 3 Coder).

### NPC relationship and free-will behavior deepening

Stronger coupling between **weather**, **economy**, **schedules**, **combat**, and **dialog** while keeping **performance guardrails** and the “living world” feel.

**Role routing:** Director/Planner → Coder (Qwen 3 Coder) + Writer (Mistral), Player Experience Simulator, Edge Case Thinker (DeepSeek V3), Narrative Consistency Keeper (Gemma 4), QA Tester/Playtester.

---

## Medium priority

### Procedural kit-bashing and asset pipeline upgrades

Better **consistency scoring**, **post-processing**, and **editor validation** tools.

**Role routing:** Director/Planner → Graphics Implementer, Asset Prompter (Gemma 4), Consistency Guard, Level Designer (Qwen 3 Instruct).

### Editor UX, previews, and HUD improvements

Clearer controls for **weather**, **NPCs**, **pipeline**, and **real-time simulations**.

**Role routing:** Director/Planner → UI/UX Designer (Gemma 4), Coder, Graphics Implementer, Tutorial Designer (Mistral).

### Expanded bot playtesting and QA automation

Richer **simulated playthroughs**, **edge-case detection**, and **playtest data** analysis.

**Role routing:** Director/Planner → QA Tester/Playtester, Data Analyst, Edge Case Thinker, Scope Guardian (Gemma 4).

### Full performance and technical debt sweep

**Hotspots**, **memory** (Vulkan / C++), **warning cleanup** (e.g. ignored `[[nodiscard]]`, nullable C# paths, dead helpers), and **maintainability**. Treat noisy compiler warnings (e.g. GCC `-Wmaybe-uninitialized` on well-initialized `std::string` members) as **verify then fix or document**.

**Role routing:** Director/Planner → Performance Advisor, Technical Debt Auditor, Janitor, Coder (Qwen 3 Coder), Build/Release Engineer.

### Testing and CI expansion (explicit)

Execute [`docs/testing/TEST_POLICY.md`](testing/TEST_POLICY.md): widen **PR gates** over time (full Python + C# suites where stable), add **regression tests** for changed behavior, keep **native runtime / bootstrap** in slower or release workflows, and maintain **acceptance traceability** (`docs/release/acceptance_traceability_v1.json`). Add **C++ tests** for pure logic where CMake/tooling allows without blocking GPU-less CI.

**Role routing:** Director/Planner → QA Tester/Playtester, Technical Debt Auditor, Build/Release Engineer, Coder.

### Scene and content schema versioning

**Scene JSON** (and related AI outputs) need **version fields**, **migration strategy**, and **compatibility rules** so editor exports and codegen do not silently break older projects.

**Role routing:** Director/Planner → Coder, Technical Debt Auditor, QA Tester/Playtester, Data Analyst (for fixture corpora).

### Accessibility and inclusive UX (V1 baseline)

Implement [`SOUL_LOOM_UX_FOUNDATIONS.md`](../SOUL_LOOM_UX_FOUNDATIONS.md) accessibility baseline where committed: **keyboard**, **focus order**, **contrast**, **control labels** / automation names for Avalonia, without bloating the default simple mode.

**Role routing:** Director/Planner → UI/UX Designer (Gemma 4), Player Experience Simulator, QA Tester/Playtester.

### Third-party cloud compute abstraction (foundation)

Distinct from per-role **`use_cloud`**: a **provider-shaped** layer for optional **remote jobs** (queue, auth, job contract) per blueprint **§14** — no first-party hosting required.

**Role routing:** Director/Planner → Coder, Technical Debt Auditor, Build/Release Engineer, Security-minded review (fold into Janitor/Auditor or explicit security pass).

### Generation safety and trust boundaries

**Path safety** for generated files, expectations around **untrusted JSON**, **secrets** (e.g. HF token handling), and **co-creator apply** behavior documented and tested.

**Role routing:** Director/Planner → Technical Debt Auditor, Coder, QA Tester/Playtester, Consistency Guard for policy copy.

---

## Lower priority / backlog

### Quest, lore, dialog, and narrative expansions

Writer, Narrative Consistency Keeper, Voice Direction Specialist, Localization Coordinator (Qwen 3 Instruct).

### Economy and balance tuning

Balance/Economy Designer (DeepSeek V3), Data Analyst.

### Tutorial and onboarding flow

Tutorial Designer (Mistral), Player Experience Simulator, UI/UX Designer.

### Marketing, trailers, and community content

Marketing Agent (Mistral), Community Feedback Synthesizer (Gemma 4).

### Steam readiness, packaging, and deployment

Build/Release Engineer; extend with **OSS/license attribution bundles**, **signing strategy**, **update channels**, and **crash/diagnostics policy** aligned with README **crash-free** KPIs (local-first, privacy-respecting).

### Ongoing cross-content validation and feature creep prevention

Consistency Guard, Scope Guardian, Cross-Content Checker (Gemma 4).

### Scene templates (curated starters)

Deliver **village / dungeon / overworld / arena** (and similar) **templates** called out in README “in active development” — quality bar, Art Bible defaults, and editor entry points.

**Role routing:** Director/Planner → Level Designer, UI/UX Designer, Writer (flavor), Consistency Guard, QA Tester/Playtester.

### Player-facing persistence (exported games)

Clarify and implement **save/load** for **shipped or exported playables** vs **editor scene save** — scope, format, and UX (if in V1 product scope).

**Role routing:** Director/Planner → Coder, UI/UX Designer, QA Tester/Playtester, Player Experience Simulator.

### Documentation and marketing assets

Replace **placeholder** README screenshots when the UI stabilizes; keep **deployment** and **test** docs in sync with gates.

**Role routing:** Tutorial Designer, Marketing Agent, Build/Release Engineer (for release notes).

---

## Ongoing — AI role registry maintenance

- Implement and extend **role-to-model mapping** in `model_manager.py` with **smart reuse** of workhorse models (e.g. Gemma 4 27B, Qwen 3 Coder 32B, Mistral Magistral 22B).
- Add **`use_cloud` (or equivalent) per role/task** for optional unlimited-quality paths when aligned with the cloud abstraction.
- Test **sequential load → infer → unload** for **8 / 12 / 16 GB** VRAM tiers.
- Monitor **total disk footprint** (target **95–120 GB** with reuse).
- **Cursor integration:** add `.cursor/rules/registry.mdc` documenting all roles and model recommendations (repo currently has no `.cursor` content; this closes that gap).

**Role routing:** Director/Planner + Technical Debt Auditor + Janitor + Build/Release Engineer.

---

## Interview and style enforcement (cross-cutting)

- **Interviewer:** use for design-intake quality and brief completeness when kicking off major epics or pipeline changes.
- **Style Enforcer:** pair with **style/copy** in UI and **Art Bible** enforcement; coordinate with Consistency Guard and Writer so tone stays coherent.

---

## Changelog (this document)

- **April 2026 (rebrand — code + binaries):** C# app renamed to **`Soul.Editor`** (assembly, `avares://`, tests project **`Soul.Editor.Tests`**). CMake runtime target/output is **`soul_runtime`** (`project(SoulRuntime)`). Python venv path hook is **`soul_loom.pth`** (replaces `soul_loom.pth`). User-facing **LoomGuard** copy is **LoomGuard** (config key `loomguard` / `loomguard_keep_message` unchanged for JSON compatibility). Log discovery prefers **`%LocalAppData%\Soul\logs`** and still checks **`SoulLoom`** / **`SoulLoom`** as legacy fallbacks.
- **April 2026 (rebrand):** Canonical product name is **Soul Loom** across README, specs, editor UI strings, CI artifacts, and Python env vars (`SOUL_LOOM_*`, with `SOUL_LOOM_*` retained as legacy aliases). Root **`backlog.md`** and **`docs/release/RC_HANDOFF_V1.md`** remain **`[DEPRICATED]`** snapshots with pointers here and to live traceability.
- **April 2026 (documentation pass):** Prior doc alignment pass; superseded by rebrand entry above.
- **April 2026:** Merged sprint backlog with role registry, added gaps from code/product review: **cloud abstraction**, **scene templates**, **TEST_POLICY / CI expansion**, **accessibility**, **schema versioning**, **player persistence**, **generation safety**, **release hardening**, **`.cursor` registry**, and **technical warning / debt** note.
