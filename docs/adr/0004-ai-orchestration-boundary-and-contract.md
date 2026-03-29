# ADR 0004: AI Orchestration Boundary and Contract

- **Status:** Accepted
- **Date:** 2026-03-21
- **Owners:** AI Orchestration + Architecture
- **Deciders:** Soul Loom maintainers

## Context
V1 requires AI-assisted planning and automation while preserving user control and predictable editor/runtime behavior. The stack lock sets AI orchestration to Python. Architectural boundaries are needed so AI can propose/execute changes without directly mutating runtime internals in unsafe ways.

## Decision
Use **Python** for AI orchestration as a separate orchestration layer that:
1. Produces structured intents/plans and patch-like change requests.
2. Calls explicit editor/runtime APIs instead of writing engine memory/state directly.
3. Requires confirmation gates for major design/system changes.
4. Keeps operation logs and supports undo-aware workflows.

## Alternatives Considered
1. **Embed AI logic directly in runtime (C++)**
   - Pros:
     - Fewer process boundaries.
     - Potential lower invocation overhead.
   - Cons:
     - Harder to iterate prompt/orchestration logic quickly.
     - Increased risk of unstable coupling between AI and core engine state.
2. **Embed AI orchestration directly in editor (C# only)**
   - Pros:
     - Centralized tooling layer.
   - Cons:
     - Conflicts with V1 stack lock for AI orchestration language.
     - Reduces portability of orchestration scripts/automation.
3. **Remote cloud-only AI orchestration service**
   - Pros:
     - Centralized model management.
   - Cons:
     - Violates local-first default and no first-party cloud hosting in V1.
     - Introduces offline fragility and operational scope creep.

## Tradeoffs
- We optimize for local-first AI iteration velocity, clear control boundaries, and safer change governance.
- We accept IPC/API boundary design and integration test overhead.
- We intentionally avoid direct AI-to-runtime mutation paths in V1.

## Consequences
- Positive:
  - Better maintainability and safer user-facing change control.
  - Faster experimentation in orchestration logic without destabilizing runtime/editor cores.
- Negative:
  - Requires well-defined contracts and versioning between components.
- Neutral/Follow-up:
  - Later ADR may define offline model provider abstractions and optional third-party cloud extension points.

## Scope and Non-Goals
- In scope:
  - AI orchestration ownership, location, and guardrail boundaries.
- Out of scope:
  - Specific model vendor/provider selection.
  - Hosted AI platform implementation.

## Compliance Checks
- Local-first impact: Strong; orchestration operates locally by default.
- No-code-first UX impact: Strong positive; supports guided, explainable automation with user confirmation.
- V1 scope boundaries (single-player, no marketplace, no first-party cloud): Aligned.
- Target OS impact (Windows + Ubuntu): Aligned via Python support on both targets.

## References
- Related ADRs: ADR-0001, ADR-0002, ADR-0003
- External references: GAMEFORGE_V1_BLUEPRINT.md, GAMEFORGE_EXECUTION_PLAN.md

