# GameForge UX Foundations (V1)

## Purpose
Define UI/UX standards for GameForge V1 so non-coders can build games confidently without needing technical knowledge.

## UX North Star
A first-time hobby creator should be able to:
1. Start with a vague idea,
2. Get a playable prototype,
3. Make meaningful visual edits,
4. Publish readiness-checkable output,
without opening code.

## Core UX Principles
1. **Clarity over power density**
   - Show only what the user needs now; hide advanced controls behind expandable panels.
2. **AI with user control**
   - AI suggests and executes, but major changes require confirmation.
3. **Safe experimentation**
   - Undo/redo, previews, and lock protection should be always visible and easy to use.
4. **Context-aware assistance**
   - AI should react to selected objects/regions and current editor context.
5. **Progress visibility**
   - Users should always know where they are: concept, prototype, refine, test, publish.

## Primary Personas (V1)
- **Hobby Creator (primary):** little/no coding knowledge, wants quick results and easy edits.
- **Power Creator (secondary):** uses visual tools mostly but may inspect/edit code occasionally.

## Information Architecture
Top-level navigation (V1):
1. Project Home
2. AI Interview
3. Prototype
4. Editor
5. Testing
6. Publish
7. Settings

## Core Screens and UX Requirements

### 1) Project Home
- Create/open/recent projects.
- Show project status badges (Not Started, Prototype Ready, Testing Needed, Publish-Ready).
- Git toggle visible but OFF by default.

### 2) AI Interview
- Question batches should be manageable.
- Offer 3 suggestions when user is uncertain.
- “Think of something” returns 3 direction proposals.
- Show a live “Design Brief Summary” panel while interviewing.

### 3) Prototype Screen
- One-click generate prototype button.
- Show generation progress and what was created.
- Provide “Regenerate section” and “Continue refining” actions.

### 4) Editor (Primary Work Area)
- Viewport center.
- Left: hierarchy/outliner.
- Right: inspector with Simple tab default + Advanced tab collapsed.
- Bottom/right: AI chat panel aware of current selection.
- Top ribbon: Undo, Redo, Lock/Unlock, Preview Changes.

### 5) Testing
- Bot test first, then human test.
- Show concise report cards: gameplay, progression, economy, performance.
- Inconclusive bot runs are clearly flagged.

### 6) Publish
- Readiness score with checklist.
- Critical blockers shown first.
- Warnings can be overridden with explicit confirmation.
- Audit trail preview and consent-based upload options.

## Interaction Standards
- Important actions use plain-language verbs (“Generate Prototype”, “Lock Selection”).
- Destructive actions require confirmation and clear impact summary.
- Every AI-generated change should include what changed and where.
- Repeated AI failures (5x sequential) should auto-suggest guided manual mode.

## Accessibility and Usability Baseline (V1)
- Scalable UI text (100–150%).
- Color contrast target at least WCAG AA for core text/actions.
- Keyboard shortcuts for core actions (undo/redo, save, search/select).
- Tooltips for all non-obvious controls.

## UX Deliverables by Milestone
- M1: Navigation map + wireframe set (low fidelity). Baseline artifact: `GAMEFORGE_UX_WIREFRAME_BASELINE_V1.md`.
- M2: Interview UX flow + summary panel.
- M3: Prototype progress UX and recovery states.
- M4: Editor interaction model finalized.
- M7: Testing report readability pass.
- M8: Publish/readiness funnel polish pass.

## UX Quality Metrics
- Time to first playable prototype (new user): under 45 minutes.
- Task success for “change selected object via AI”: at least 90% in internal tests.
- User confidence score (“I can change what I see”): at least 8/10.
- Critical UX confusion issues in core flow: zero before release candidate.

## Out of Scope (V1)
- Advanced customizable workspace layouts.
- In-app tutorials for every tool (focus on essential onboarding only).
- Multi-user collaborative editor UX.

## Supporting UX Copy Artifact
- Canonical UX copy baseline for core actions, warnings, and confirmations: `GAMEFORGE_UX_COPY_STANDARDS_V1.md`.

## Change Control
- Major UX flow changes must update:
  - `GAMEFORGE_V1_BLUEPRINT.md`
  - `GAMEFORGE_EXECUTION_PLAN.md`
  - `GAMEFORGE_ACCEPTANCE_TEST_MATRIX.md` (if acceptance behavior changes)
