# GameForge Codex Prompt Playbook (V1 Execution)

## Purpose
This document converts the V1 execution plan into **Codex-optimized prompts** you can run milestone-by-milestone.

It is designed to:
- keep tasks small and executable,
- reduce ambiguity,
- produce verifiable deliverables,
- enforce scope discipline.

---

## 1) Codex Prompt Best Practices (Applied)
Use these rules for every prompt:

1. **One objective per prompt**
   - Avoid asking for architecture, UI, and testing in one request.
2. **Always define scope boundaries**
   - Include what is explicitly out of scope.
3. **Require concrete outputs**
   - Files to create/modify, expected commands, and acceptance criteria.
4. **Demand verification steps**
   - Ask for checks/tests and a short report of pass/fail.
5. **Pin context to project docs**
   - Reference `GAMEFORGE_V1_BLUEPRINT.md` and `GAMEFORGE_EXECUTION_PLAN.md`.
6. **Prefer incremental PR-sized changes**
   - Target 1–3 days of work per prompt.
7. **Enforce non-destructive behavior**
   - Preserve existing work, request migrations for breaking changes.
8. **Ask for assumptions explicitly**
   - If unknowns exist, require Codex to list assumptions before implementation.

---

## 2) Global System Prompt (Recommended)
Use this once at the start of a coding session:

```text
You are implementing GameForge V1 incrementally.
Follow these requirements strictly:
- Prioritize local-first, single-player, no-code-first workflows.
- Keep scope aligned with GAMEFORGE_V1_BLUEPRINT.md and GAMEFORGE_EXECUTION_PLAN.md.
- Do not add multiplayer, marketplace, or first-party cloud hosting.
- Make small, reviewable changes with clear acceptance criteria.
- For each task: (1) plan, (2) implement, (3) run checks, (4) summarize deliverables and risks.
- If requirements are ambiguous, state assumptions and proceed with safest scoped option.
```

---

## 3) Reusable Task Prompt Template (Copy/Paste)

```text
Task: <short task title>

Goal:
<what must be true when done>

Context:
- Read: GAMEFORGE_V1_BLUEPRINT.md
- Read: GAMEFORGE_EXECUTION_PLAN.md
- This task belongs to: <Milestone X>

Scope In:
- <bullet 1>
- <bullet 2>

Scope Out:
- <explicitly excluded items>

Deliverables:
1) <file/code deliverable>
2) <file/code deliverable>
3) <test/check deliverable>

Acceptance Criteria:
- <measurable criterion 1>
- <measurable criterion 2>
- <measurable criterion 3>

Implementation Constraints:
- Keep changes minimal and modular.
- Preserve backward compatibility unless migration is included.
- Add concise inline docs where logic is non-obvious.

Validation:
- Run relevant checks/tests.
- Report exact commands and outcomes.

Output Format:
- Summary of changes
- Files changed
- Tests/checks run
- Known limitations / follow-up tasks
```

---

## 4) Milestone Prompt Pack

## Milestone 1 — Foundation & Architecture

### Prompt M1-P1: Repo skeleton + bootstrap
```text
Task: Establish GameForge V1 repository skeleton and local bootstrap flow

Goal:
Create a clean module structure and a single bootstrap command so a new contributor can run the project quickly.

Context:
- Read GAMEFORGE_EXECUTION_PLAN.md Milestone 1.
- Read GAMEFORGE_V1_BLUEPRINT.md sections on local-first and V1 scope.

Scope In:
- Create top-level folders for app/editor/runtime/ai-orchestration/docs/scripts.
- Add bootstrap script(s) and setup docs.
- Add a minimal app entrypoint that runs successfully.

Scope Out:
- Full feature implementation.
- Cloud provider integrations.

Deliverables:
1) Folder/module structure committed.
2) Bootstrap command and setup documentation.
3) Basic run verification command output.

Acceptance Criteria:
- Fresh clone can run bootstrap and start minimal app.
- Setup steps are documented in markdown.
- No references to unsupported V1 features.

Validation:
- Run bootstrap command.
- Run minimal app start command.
- Report pass/fail.
```

### Prompt M1-P2: ADR baseline
```text
Task: Create architecture decision record baseline for V1

Goal:
Document stack choices and rationale to avoid re-deciding fundamentals.

Scope In:
- Add ADR template and first ADR entries for runtime, UI framework, data format, and AI orchestration boundary.

Scope Out:
- Implementing the full stack itself.

Deliverables:
1) docs/adr/0001-*.md and related initial ADR files.
2) Short architecture overview page linking ADRs.

Acceptance Criteria:
- Every major decision includes alternatives + tradeoffs.
- Decisions align with local-first and no-code-first constraints.
```

---

## Milestone 2 — AI Interview Engine

### Prompt M2-P1: Interview state schema
```text
Task: Implement interview state model and persistence

Goal:
Define and persist structured interview data for long sessions.

Scope In:
- Create schema for concept, genre weights, mechanics, narrative, style, and constraints.
- Add save/load logic for interview sessions.

Scope Out:
- Full prompt intelligence logic.

Deliverables:
1) Interview schema definitions.
2) Persistence layer for local save/load.
3) Unit tests for serialization/deserialization.

Acceptance Criteria:
- Session can be resumed without data loss.
- Schema is versioned for future migrations.
```

### Prompt M2-P2: 3-option suggestion logic
```text
Task: Add uncertainty handling with 3-option suggestions

Goal:
When user is uncertain, system produces three curated options with tradeoffs.

Scope In:
- Implement option generation contract and response format.
- Add UX hook to display and select options.

Scope Out:
- Full prototype generation.

Deliverables:
1) Option generation function and interface binding.
2) Selection persistence into interview state.
3) Tests covering unknown/ambiguous input cases.

Acceptance Criteria:
- For ambiguous input, exactly 3 options are returned.
- Selected option is stored and influences next questions.
```

### Prompt M2-P3: “Think of something” mode
```text
Task: Add think-for-me mode with 3 directional concepts

Goal:
Generate three coherent direction proposals and request explicit user confirmation before major decisions.

Deliverables:
1) Mode trigger and generation pipeline.
2) Confirmation gate before committing direction.
3) Structured output for downstream prototype generator.

Acceptance Criteria:
- No direction is committed without user confirmation.
- Output is machine-readable and human-readable.
```

---

## Milestone 3 — Prototype Generator

### Prompt M3-P1: One-click prototype pipeline
```text
Task: Build one-click prototype generation from interview brief

Goal:
Generate a playable baseline scene from saved interview output.

Scope In:
- Parse brief.
- Create scene scaffold, basic player control, basic UI, save/load hook.

Scope Out:
- Full genre complexity.

Deliverables:
1) Prototype generation command/workflow.
2) Generated sample project artifacts.
3) Smoke test script for launch success.

Acceptance Criteria:
- Prototype builds and launches from one command/action.
- Generated project includes core scaffold components.
```

### Prompt M3-P2: Regenerate with lock awareness
```text
Task: Support partial regeneration without destroying locked content

Goal:
Allow iterative regeneration while protecting locked entities/regions.

Deliverables:
1) Lock-aware regeneration logic.
2) Conflict prompt when regeneration touches locked areas.
3) Tests for protected-content behavior.

Acceptance Criteria:
- Locked content is never overwritten silently.
- User confirmation required for destructive updates.
```

---

## Milestone 4 — Visual Editor + Copilot

### Prompt M4-P1: Editor shell + object selection
```text
Task: Implement editor shell with hierarchy, viewport, inspector, and chat panel

Goal:
Provide non-coders an interactive visual workspace.

Deliverables:
1) Editor layout and docking.
2) Object selection pipeline.
3) Simple vs advanced inspector sections.

Acceptance Criteria:
- Selecting object updates inspector and AI context.
- Editor opens generated projects without crashing.
```

### Prompt M4-P2: Undo/redo + before/after preview
```text
Task: Implement safe editing controls

Goal:
Add confidence features for non-technical users.

Deliverables:
1) Multi-step undo/redo timeline.
2) Before/after preview modal for major AI edits.
3) Integration tests for rollback behavior.

Acceptance Criteria:
- User can undo multiple operations reliably.
- Preview shown for major mutation actions.
```

---

## Milestone 5 — RTS/Sim + RPG Basic Kits

### Prompt M5-P1: RTS/sim starter kit
```text
Task: Add RTS/sim baseline systems

Goal:
Provide basic playable RTS/sim foundation.

Scope In:
- Units/agents
- Resource loop
- Building placement
- Basic progression tree

Deliverables:
1) Reusable RTS/sim template module.
2) Sample scenario map.
3) Basic balancing config file.

Acceptance Criteria:
- Sample map playable with core loop intact.
- Systems configurable without source edits where possible.
```

### Prompt M5-P2: RPG starter kit + consequence model
```text
Task: Add RPG baseline systems with choice consequences

Goal:
Enable quests/dialogue/inventory/leveling with visible action consequences.

Deliverables:
1) Quest and branching dialogue framework.
2) Inventory + leveling modules.
3) Consequence state tracker and branch visualization.

Acceptance Criteria:
- Choices alter NPC/world state in sample project.
- Branch view reflects live state transitions.
```

---

## Milestone 6 — Assets + Style

### Prompt M6-P1: Asset ingestion and cataloging
```text
Task: Build asset import pipeline with auto-tagging

Goal:
Allow AI-generated assets and manual uploads with organized cataloging.

Deliverables:
1) Asset ingestion pipeline.
2) Auto-tag classification into categories.
3) Search/filter panel in asset browser.

Acceptance Criteria:
- Imported assets become searchable by tags/category.
- Failed imports return actionable errors.
```

### Prompt M6-P2: Style presets and consistency tools
```text
Task: Implement project style system

Goal:
Maintain consistent visual style with built-in and user-defined presets.

Deliverables:
1) Preset definitions and selection UI.
2) User-defined preset creation flow.
3) “Match project style” helper action.

Acceptance Criteria:
- Users can create/save/select custom presets.
- Style helper applies consistent transformations on sample assets.
```

---

## Milestone 7 — Testing Assistant

### Prompt M7-P1: Bot playtest harness
```text
Task: Implement automated bot-first playtesting pipeline

Goal:
Run baseline gameplay validation before human testing.

Deliverables:
1) Bot run orchestrator.
2) Test scenario definition format.
3) Inconclusive-result flagging.

Acceptance Criteria:
- Bot tests execute on generated prototypes.
- Inconclusive runs are explicitly marked for human review.
```

### Prompt M7-P2: AI playtest report
```text
Task: Generate actionable playtest report

Goal:
Surface pacing/progression/economy/quest/perf issues clearly.

Deliverables:
1) Structured report schema.
2) UI report viewer.
3) Export to markdown/json.

Acceptance Criteria:
- Report lists at least progression, economy, dead-end, pacing, and performance sections.
- Reports are persisted per test run.
```

---

## Milestone 8 — Steam Readiness + Release Candidate

### Prompt M8-P1: Readiness scoring + gating
```text
Task: Implement Steam readiness scoring and policy gating

Goal:
Provide pre-publish confidence with critical-vs-warning logic.

Deliverables:
1) Readiness score calculator.
2) Checklist UI with critical/warning labels.
3) Gate behavior for blocking critical issues.

Acceptance Criteria:
- Critical issues prevent publish action.
- Warnings allow override with explicit acknowledgement.
```

### Prompt M8-P2: Commercial declaration workflow
```text
Task: Add commercial use declaration and policy flow

Goal:
Support free non-commercial and commercial policy messaging in-app.

Deliverables:
1) Project-level commercial declaration setting.
2) Policy text surfaces for commercial criteria and revenue share threshold.
3) Audit log entry for declaration changes.

Acceptance Criteria:
- Project can be marked commercial/non-commercial.
- Policy text appears in relevant publish and settings flows.
```

---

## 5) Prompt Sequencing Rules
1. Do not start Milestone N+1 until Milestone N exit criteria are met.
2. Within a milestone, run prompts in order unless blocked.
3. If blocked, open a “BLOCKER prompt”:
   - summarize blocker,
   - propose 2–3 options,
   - recommend one with tradeoffs.

---

## 6) Quality Gate Prompt (Run after every 2–3 prompts)
Use this to prevent hidden debt:

```text
Quality Gate Review:
- Review recent changes for scope drift, regressions, and architecture consistency.
- Verify alignment with GAMEFORGE_V1_BLUEPRINT.md and GAMEFORGE_EXECUTION_PLAN.md.
- List:
  1) What is done and stable,
  2) What is risky,
  3) What should be refactored now vs later,
  4) Whether to continue or pause for cleanup.
- Run relevant tests/checks and report exact commands + outputs.
```

---

## 7) “Don’t Let Codex Do This” Checklist
Reject any output that:
- introduces multiplayer in V1,
- adds first-party cloud hosting requirements,
- bypasses confirmation for major destructive edits,
- lacks tests/checks for behavior changes,
- ships large unreviewable rewrites when incremental changes are possible.

---

## 8) First Prompt to Run Next (Immediate)
```text
Task: Milestone 1 Prompt 1 — Repository skeleton and bootstrap

Use CODEX_PROMPT_PLAYBOOK.md and GAMEFORGE_EXECUTION_PLAN.md as source constraints.
Implement only Milestone 1 deliverables for repo/module structure + local bootstrap + minimal run path.
Do not implement Milestone 2+ features.
Include setup docs and exact verification commands.
Return:
- summary,
- files changed,
- commands run,
- next recommended prompt.
```

