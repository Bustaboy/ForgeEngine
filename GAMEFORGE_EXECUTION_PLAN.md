# GameForge Execution Plan (V1)

## Role and Working Model
This plan treats execution as if run by:
- **Senior Developer**: responsible for architecture, technical quality, and implementation sequencing.
- **Project Manager**: responsible for scope control, milestone planning, delivery tracking, and risk management.

Primary objective:
- Ship a **local-first, AI-first, no-code-friendly** V1 that can produce basic RTS/sim + RPG single-player games with a practical end-to-end workflow.

---

## 0) Ground Rules (Scope Control)
To prevent project failure by scope creep, these are hard rules for V1:
1. Single-player only.
2. Desktop local app first.
3. No custom cloud hosting by us in V1.
4. No marketplace in V1.
5. Modding is deferred.
6. Every milestone must produce a demoable deliverable.
7. Every sprint must end with a written “done/not done” checkpoint.

---

## 1) Delivery Structure
- **Cadence:** 2-week sprints
- **Planning horizon:** 12 sprints (about 6 months)
- **Definition of Done (DoD) per feature:**
  - Works locally on target machine spec (i5 / RTX 2070 / 16GB RAM)
  - Works on V1 OS targets: Windows + Ubuntu
  - Has basic UX polish (not raw developer-only UI)
  - Has at least one automated or scripted validation check
  - Is documented in the project changelog

Artifacts produced every sprint:
- Sprint goal
- Task board (To Do / In Progress / Blocked / Done)
- Demo video or live run notes
- Risk updates
- Next sprint commitment
- UX notes for any changed user-facing flow

---

## 2) Milestone Map (Bite-Size Chunks + Deliverables)

## Milestone 1 — Project Foundation & Architecture (Sprint 1)
### Goal
Create a stable technical base so later work does not collapse.

### Deliverables
1. Repository structure for app, AI orchestration, editor, runtime, and build pipeline.
2. Architecture decision record (ADR) document with chosen stack and rationale.
3. Local project bootstrap command that starts app + services for development.
4. Minimal “hello project” flow (create/load/save empty project).
5. Initial UX wireframes + navigation map in `GAMEFORGE_UX_FOUNDATIONS.md`.

Stack lock for V1 ADR baseline:
- Core engine/runtime systems: C++
- Editor/tooling UI: C#
- AI orchestration and automation: Python
- Rendering direction: Vulkan-first

### Exit Criteria
- New contributor can clone and run the app locally in under 20 minutes.
- Empty project can be created, saved, reopened successfully.
- Basic startup path validated on Windows and Ubuntu developer environments.

---

## Milestone 2 — AI Interview Engine (Sprints 2–3)
### Goal
Implement deep Q&A flow that can run long sessions and maintain context.

### Deliverables
1. Conversational interview framework with topic modules:
   - genre
   - game loop
   - progression
   - narrative
   - art/style
2. “3 options” generator for uncertain user responses.
3. “Think of something” mode returning 3 direction proposals.
4. Structured design brief output (JSON + human-readable markdown).
5. Save/load interview state and user preference profiles.

### Exit Criteria
- A user can complete a long interview and export a usable design brief.
- AI always asks confirmation before major decisions.

---

## Milestone 3 — Prototype Generator (Sprints 4–5)
### Goal
Generate first playable prototype from interview brief.

### Deliverables
1. One-click prototype generation pipeline.
2. Core systems scaffold:
   - scene/world skeleton
   - player controller
   - basic UI
   - save/load hook
3. Regenerate-or-iterate workflow (partial regeneration of systems).
4. Gameplay-first sequencing guardrails:
   - gameplay tasks prioritized over story and visuals.

### Exit Criteria
- From a saved brief, prototype can be generated and launched in one flow.
- Regeneration does not destroy locked content without confirmation.
- If AI fails a requested operation 5 consecutive times, system offers guided manual mode.

---

## Milestone 4 — Visual Editor + AI Copilot (Sprints 6–7)
### Goal
Enable non-coders to refine everything visually with AI assistance.

### Deliverables
1. Editor shell with panels:
   - hierarchy
   - inspector (simple + advanced tabs)
   - viewport
   - chat assistant pane
2. Context-aware object editing (AI sees current selection).
3. Lock/unlock system for objects/regions.
4. Undo/redo timeline with multi-step rollback.
5. Before/after preview for significant AI edits.

### Exit Criteria
- Non-coder can adjust generated content without opening code.
- Lock + undo protections reliably prevent accidental destructive edits.

---

## Milestone 5 — Genre Kits: RTS/Sim + RPG Basics (Sprints 8–9)
### Goal
Provide practical baseline systems for both target genres.

### Deliverables
1. RTS/Sim starter systems:
   - units/agents
   - resource loop
   - building placement
   - basic tech/progression tree
2. RPG starter systems:
   - quests
   - branching dialogue
   - inventory/equipment
   - leveling/progression
3. Consequence framework:
   - player actions affect NPC responses and world state.
4. Branch visualization panel for quest/dialogue logic.

### Exit Criteria
- Sample RTS/sim project and sample RPG project are both playable.
- Choice consequences are visible and testable.

---

## Milestone 6 — Assets and Style Consistency (Sprint 10)
### Goal
Support AI generation + import flow with style consistency controls.

### Deliverables
1. Asset ingestion pipeline:
   - AI-generated asset import
   - manual upload import
   - free library suggestion integration point
2. Auto-tagging and cataloging (characters, props, UI, audio, etc.).
3. Project style system:
   - style preset selection
   - user-defined presets
   - “match project style” assistance
4. Failure messaging when generation quality is insufficient.
5. License allow-list enforcement for third-party assets and metadata validation.

### Exit Criteria
- Imported assets are automatically organized and searchable.
- Style consistency tools are usable in at least one end-to-end test project.
- Unsupported or unclear asset licenses are blocked with actionable feedback.

---

## Milestone 7 — Testing and Balance Assistant (Sprint 11)
### Goal
Implement AI-first validation before human playtesting.

### Deliverables
1. Automated bot playtest harness.
2. Inconclusive result detection + human-test escalation flag.
3. AI playtest report with:
   - progression issues
   - economy pressure points
   - quest dead ends
   - pacing warnings
   - performance signals
4. Difficulty profile templates: Easy / Medium / Hard / Extreme.

### Exit Criteria
- Every generated prototype can run through bot testing.
- Actionable report is produced and visible in UI.

---

## Milestone 8 — Steam Readiness + Commercial Rules + Release Candidate (Sprint 12)
### Goal
Ship an end-to-end V1 candidate.

### Deliverables
1. Steam readiness score (0–100) and checklist UI.
2. Critical vs warning gate logic:
   - critical blocks publish flow
   - warnings allow user override
3. Store-page draft helper (description/tags basic pass).
4. Commercial project declaration flow:
   - free non-commercial mode
   - commercial definition (paid or MTX)
   - revenue share policy text (5% after first $1,000)
5. Release candidate package + install instructions.
6. Publish audit-trail generation with user-confirmed upload flow and local signed copy.
7. Numeric quality thresholds enforced in readiness checks:
   - crash-free session target >= 97%
   - 60 FPS target, with critical fail if sustained below 30 FPS on target validation scenes
   - scene load target below 20 seconds on target hardware

### Exit Criteria
- A non-coder can go from idea -> prototype -> refine -> test -> readiness check in one system.

---

## 3) Cross-Cutting Workstreams (Run Every Sprint)
1. **Performance budget management**
   - Track startup time, memory use, and editor responsiveness.
2. **UX simplification**
   - Replace unclear labels, reduce jargon, streamline actions.
   - Keep `GAMEFORGE_UX_FOUNDATIONS.md` aligned with current product behavior.
3. **Reliability and crash recovery**
   - Autosave integrity checks, recovery path after crash.
4. **Telemetry (local-friendly)**
   - Optional local analytics for failed generation actions.
5. **Documentation**
   - Keep user guide and developer setup updated continuously.
6. **Cross-platform validation**
   - Keep Windows and Ubuntu smoke checks green for active milestones.

---

## 4) Task Sizing Template (Bite-Size Unit)
Every implementation ticket should fit roughly 0.5 to 2 days and include:
- User story (“As a non-coder, I can…”)
- Acceptance criteria (3–6 checks)
- Demo step
- Risk/unknown note

Example bite-size ticket:
- “As a user, when I select an object and type ‘make this brighter’, the AI updates only that object and shows preview before apply.”

---

## 5) Team Operating Rhythm (2 People)
### Weekly cadence
- Monday: plan sprint tasks and priorities
- Mid-week: risk review + scope cuts if needed
- Friday: demo + retrospective + decision log update

### Responsibility split
- You (Product Owner): vision decisions, acceptance feedback, prioritization
- Me (Senior Dev/PM): architecture, implementation sequencing, blockers, delivery reporting

---

## 6) Risk Register + Mitigation
1. **Risk:** AI output quality inconsistency
   - Mitigation: enforce confirmation gates, previews, and regeneration options.
2. **Risk:** scope explosion from “nice-to-have” features
   - Mitigation: hard in/out scope list and sprint freeze discipline.
3. **Risk:** local hardware performance bottlenecks
   - Mitigation: performance checks every sprint and fallback quality tiers.
4. **Risk:** unclear publishing compliance
   - Mitigation: keep policy checks modular and updateable.
5. **Risk:** over-reliance on AI with weak manual fallback
   - Mitigation: editor must always allow direct manual adjustments.

---

## 7) KPI Targets for First 6 Months
1. Time from blank project to first playable prototype: under 45 minutes.
2. Non-coder completion rate for end-to-end flow: at least 60% in internal tests.
3. Critical crash rate during core workflow: under 3% sessions.
4. User-perceived “I can change what I see” confidence score: at least 8/10.

---

## 8) Immediate Next 11 Working Sessions (Practical Start)
1. Finalize stack and architecture ADR.
2. Set up repo modules and local bootstrap scripts.
3. Implement project create/load/save baseline.
4. Build interview schema and state model.
5. Add first interview question modules.
6. Implement 3-option suggestion behavior.
7. Add “think of something” mode.
8. Export design brief (JSON + markdown).
9. Start prototype scaffold generator.
10. Demo first idea->brief->prototype vertical slice.
11. Add Git integration toggle (default OFF) for newly created projects.

---

## 9) Definition of V1 Release Candidate
V1 is considered release-candidate ready when all are true:
1. End-to-end flow works locally without coding.
2. Both RTS/sim and RPG basic templates are functional.
3. Lock/undo/preview safety controls are stable.
4. Bot test + report pipeline is operational.
5. Steam readiness checklist works with critical/warning gating.
6. Core docs are complete enough for a new hobby creator to start.

---

## 10) What Happens After V1 (Preview, Not Commitment)
Potential V2 themes:
- Multiplayer foundation
- Modding toolkit
- Full third-party cloud execution integrations
- Better simulation depth and larger world scaling

V2 items are intentionally excluded from V1 commitments unless they are required to protect V1 architecture.
