# GameForge V1 Blueprint

## 1) Product Vision
GameForge V1 is a **local-first AI game creation engine** aimed at people with little or no coding experience.

Core promise:
- The AI does the heavy lifting (planning, system setup, logic, content scaffolding, testing support).
- Users can still fine-tune everything through a visual editor and chat-based commands.
- No coding required by default, but advanced users can access and modify code if they want.

Primary target users:
- Hobby creators first.
- Expand to indie studios later.

Game focus for V1:
- **Basic support for both RTS/simulation and RPG** in 2D and 3D.
- Graphics target: **stylized, good-looking indie 3D** (non-pixel-art default).
- Single-player only in V1 (hard lock).

---

## 2) Guiding Principles
1. **No-code first**
   - Users must be able to build and ship without touching code.
2. **User stays in control**
   - AI suggests and executes; major decisions require user confirmation.
3. **Deep discovery before generation**
   - Q&A can be very deep (20+ minutes, even hours) when needed.
4. **Transparent changes**
   - Show before/after previews for significant changes.
   - Strong undo/redo history.
5. **Local-first with cloud-ready foundation**
   - V1 runs locally.
   - Build extension points for optional 3rd-party cloud compute later.

---

## 3) End-to-End User Flow (V1)

### Stage A: Project Initialization (AI Interview)
- The user starts with chat.
- AI asks manageable batches of questions (not overwhelming).
- If user is unsure, AI offers 3 suggestions/options.
- If user says “think of something,” AI returns 3 strong routes and asks user to pick.
- The interview can run as long as needed for clarity.

Outputs from this stage:
- Core game concept.
- Genre blend (RTS/sim + RPG weights).
- Scope target (medium campaign + open-end sandbox).
- Style preset selection.
- Initial mechanics map.

### Stage B: One-Click Prototype Generation
- AI generates a playable prototype from interview results.
- User can:
  - iterate via targeted edits,
  - regenerate sections,
  - or request alternate variants when needed.

### Stage C: Gameplay-First Iteration Order
Enforce this order by default:
1. Core gameplay loop quality
2. Story/quest/dialogue structure
3. Visual polish

Reasoning:
- Stable mechanics reduce rework in later stages.
- Story affects asset and world needs.
- Visual polish should happen after systems stabilize.

### Stage D: Visual Editor + AI Co-Pilot
- Full graphical editor with intuitive tools:
  - brush-like editing,
  - sliders,
  - node graphs,
  - timelines,
  - object inspector.
- Clicking an object opens:
  - simple options + chat edit box,
  - advanced settings in collapsible/sub-tab UI.
- AI should understand selected context and propose direct edits.

### Stage E: Testing Pipeline
1. AI bot testing first (mandatory)
2. If bot results are inconclusive -> explicit flag for human testing
3. Human testing pass
4. Publish readiness pass

### Stage F: Publish to Steam (Assisted)
- Pre-publish “Steam Readiness Score” (0–100).
- Detailed checklist of complete/missing items.
- AI generates store-page assistance (description, tags, basic copy).
- Policy gates:
  - critical issues block publishing,
  - minor/borderline issues warn only.

---

## 4) AI Behavior Specification

### Decision Governance
- Minor edits: AI may auto-apply (with undo available).
- Major design/system shifts: always ask for approval first.
- If change impacts locked areas, AI must warn and request explicit permission.

### Optioning Rules
- Default suggestion behavior: offer up to 3 options.
- For ambiguous requests: clarify with short follow-up questions.
- For “I don’t know”: propose options with tradeoff explanations.

### Failure Handling
- If AI cannot reliably generate requested content:
  - state limitation clearly,
  - suggest nearest alternatives,
  - offer import from free library,
  - allow manual upload.

### Quality Priorities
- Creation mode priority: **Quality > Speed > Hardware usage**.
- Quick-preview mode priority: **Speed > Quality**.

---

## 5) Editing, Control, and Safety of Changes

### Undo/Redo + History
- Deep timeline (like Ctrl+Z behavior users expect).
- Ability to roll back multiple steps safely.
- Recommended: named checkpoints/snapshots.

### Locking System
- Users can select map regions/entities/systems and lock them.
- Locked elements are protected from AI overwrites unless user confirms.
- Simple lock/unlock icon in ribbon for usability.

### View-Linked AI Assistance
- AI should understand what user is currently looking at or has selected.
- If unclear, AI asks user to select target object/region before editing.

---

## 6) Core Engine Systems for V1
V1 must include all major single-player foundations except multiplayer:
- Physics
- Animation
- Audio
- UI system
- Save/load
- Quest/dialogue system
- Inventory and progression systems
- World simulation hooks

Multiplayer:
- Explicitly out of scope for V1.
- Deferred to later phase.

---

## 7) Genre Feature Targets

### RTS/Simulation Support (V1 Basic)
- Base building
- Unit/agent control
- Resource systems
- Tech/progression trees
- Medium-depth simulation foundations

Simulation feel target:
- Inspired by management/sandbox structure where systems interact and evolve.

### RPG Support (V1 Basic)
- Quests and quest chains
- Dialogue choices
- NPC relationship/response changes based on player actions
- Character progression/leveling
- Inventory/equipment systems

Narrative expectation:
- Consequences matter.
- Branching structure should be visualized for creators.

---

## 8) Content and Asset Pipeline

### Asset Sources
- AI generation where reliable.
- Free-library suggestions when generation is weak.
- Manual import always available.

### Asset Management
- Auto-analysis and smart cataloging of imports.
- Organize into reusable library categories (characters, props, UI, audio, etc.).

### Style Consistency
- Project-wide style consistency controls.
- “Match project style” behavior by default.
- Preset system with user-extensible library:
  - Cozy Stylized
  - Semi-Realistic
  - Low-Poly Clean
  - Dark Fantasy Stylized
  - plus user-defined presets.

---

## 9) Difficulty, Balance, and World Liveliness

### Difficulty Modes
- Built-in presets: Easy, Medium, Hard, Extreme.

### Balance Tooling
- AI should detect likely frustration spikes and balancing problems.
- AI playtest report should include:
  - progression bottlenecks,
  - economy stress points,
  - quest dead ends,
  - pacing anomalies,
  - performance notes.

### Living World Target
- Medium-depth dynamic world simulation in V1.
- Non-static world behavior over time (e.g., evolving states and systemic reactions).

---

## 10) Save/Load Expectations
- Multiple save slots by default.
- Autosave enabled by default.
- Hardcore/limited-save behavior only when intentionally selected by user.
- AI may suggest checkpoint placement based on game structure.

---

## 11) Publishing, Policy, and Compliance Model

### Publishing Destination
- Steam-first export flow.

### Content Policy Stance
- Creation environment remains open/flexible for local development.
- Publishing assistant performs platform-specific compliance checks.
- Explicit content is allowed in-editor, but export readiness should reflect platform constraints and risk areas.

### Rights and Ownership
- User owns created game/IP.
- GameForge acts as tooling.

### Copyright Risk Checks
- Automatic checks for potential content similarity/copyright concerns.

---

## 12) Monetization Model (V1 Business Rules)
- Free for non-commercial use.
- Commercial use applies to:
  - games sold for money,
  - games containing MTX.
- Proposed commercial rule:
  - **5% revenue share after first $1,000 gross revenue per game**.

Note:
- Enforcement implementation details can be phased.
- Early V1 may emphasize honor-based terms + declaration workflow before hard enforcement automation.

---

## 13) Technical Scope Boundaries for V1

### In Scope
- Local desktop application.
- AI-first game creation interview + prototype generation.
- Visual editor with AI-assisted refinement.
- Single-player game output.
- Steam readiness checks and assisted publish workflow.
- Foundation architecture for optional 3rd-party cloud compute integration.

### Out of Scope (V1)
- Native multiplayer.
- First-class modding platform.
- First-party hosted cloud service.
- Asset marketplace.

---

## 14) 3rd-Party Cloud Integration Foundation (No First-Party Hosting)
Goal:
- GameForge does not need to run its own cloud.
- Users can connect external providers later.

V1 foundation requirements:
- Provider abstraction layer (generic job API).
- “Connect provider” account/config UI stub.
- Local vs external execution routing hooks.
- Secure key storage strategy placeholder.
- Clear labeling of where tasks run (local vs external).

Deferred to later versions:
- Billing orchestration.
- Managed provider marketplace.
- One-click cloud onboarding.

---

## 15) MVP Definition (What “Success” Means)
A successful V1 should let a non-coder:
1. Describe a vague game idea through deep AI Q&A.
2. Receive a playable prototype.
3. Refine mechanics, narrative, and visuals using visual tools + chat.
4. Run AI-assisted testing and readiness checks.
5. Export toward Steam with clear compliance feedback.

Primary success criterion (6 months):
- Creator can build a game they personally want to play without needing to code.

---

## 16) Implementation Milestones (2-Person Side Project)

### Milestone 1: Interview + Concept Core
- Q&A engine
- preference memory
- 3-option suggestion logic
- concept document output

### Milestone 2: Prototype Generator
- one-click prototype build
- regeneration/edit cycle
- gameplay-first iteration pipeline

### Milestone 3: Visual Editor + AI Co-Pilot
- object selection + chat edit
- simple/advanced panels
- lock/unlock, undo/redo, snapshots

### Milestone 4: Genre Foundations
- RTS/sim basic systems
- RPG basic systems
- consequence tracking + branch visualization

### Milestone 5: Testing + Publish Assistant
- bot testing harness
- AI playtest report
- Steam readiness score + checklist

### Milestone 6: Local-First Hardening + Cloud Hooks
- performance stabilization for target spec
- provider integration scaffolding
- policy/commercial flow wiring

---

## 17) Minimum Target Hardware (Creator Machine)
- CPU: Intel i5-class
- GPU: RTX 2070-class
- RAM: 16GB

This is the baseline expectation for acceptable local experience in V1.

---

## 18) Non-Negotiables Recap
- No-code creation path must be complete.
- Users can still access/modify code if they choose.
- AI does heavy lifting; user has final authority over major decisions.
- Locking and undo protections are mandatory.
- Single-player focus for V1.
- Local-first architecture with future 3rd-party cloud compatibility.

