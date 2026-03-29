# ADR 0002: Editor and Tooling UI Framework (C#)

- **Status:** Accepted
- **Date:** 2026-03-21
- **Owners:** Editor + UX
- **Deciders:** Soul Loom maintainers

## Context
Soul Loom requires a desktop editor oriented to non-coders with rapid iteration on UX. The V1 stack lock fixes editor/tooling UI language to C#, and V1 targets Windows and Ubuntu.

## Decision
Use **C#** for editor and tooling UI, with architecture that keeps UX logic and domain contracts independent from runtime internals.

## Alternatives Considered
1. **C++ editor UI (single-language codebase)**
   - Pros:
     - Fewer language boundaries.
     - Potential tighter integration with runtime.
   - Cons:
     - Slower UI iteration for no-code-first workflows.
     - Higher complexity for onboarding UX-heavy contributors.
2. **Web-based editor (TypeScript/Electron)**
   - Pros:
     - Fast UI iteration and broad component ecosystem.
   - Cons:
     - Violates locked V1 stack for editor language.
     - Additional packaging/performance overhead for desktop-heavy workflows.
3. **Python desktop UI**
   - Pros:
     - Rapid prototyping.
   - Cons:
     - Not aligned with V1 stack lock.
     - Higher risk for long-term rich editor performance/maintainability.

## Tradeoffs
- We optimize for desktop UX delivery speed and maintainable tooling architecture.
- We accept cross-language integration complexity between C# editor and C++ runtime.
- We defer framework-level (e.g., Avalonia/WPF/WinUI) finalization to a follow-up ADR if needed.

## Consequences
- Positive:
  - Enables productive no-code interface iteration (inspectors, node graphs, timelines).
  - Keeps editor concerns separate from runtime internals.
- Negative:
  - Requires robust interop boundary design and versioning.
- Neutral/Follow-up:
  - A later ADR may pin the exact C# UI framework once prototyping data is gathered.

## Scope and Non-Goals
- In scope:
  - Language baseline and architectural separation for editor/tooling.
- Out of scope:
  - Final widget toolkit selection.
  - Runtime feature implementation.

## Compliance Checks
- Local-first impact: Strong; editor runs as local desktop app.
- No-code-first UX impact: Strong positive; supports visual-first workflows.
- V1 scope boundaries (single-player, no marketplace, no first-party cloud): Aligned.
- Target OS impact (Windows + Ubuntu): Aligned when framework selection preserves cross-platform support.

## References
- Related ADRs: ADR-0001, ADR-0003, ADR-0004
- External references: GAMEFORGE_V1_BLUEPRINT.md, GAMEFORGE_EXECUTION_PLAN.md

