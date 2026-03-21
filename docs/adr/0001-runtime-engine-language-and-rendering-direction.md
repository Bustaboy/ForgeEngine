# ADR 0001: Runtime Engine Language and Rendering Direction

- **Status:** Accepted
- **Date:** 2026-03-21
- **Owners:** Runtime + Architecture
- **Deciders:** GameForge V1 maintainers

## Context
GameForge V1 needs a foundational runtime decision that supports single-player game execution, performant simulation loops, and cross-platform desktop support (Windows + Ubuntu). The V1 blueprint and execution plan explicitly lock the runtime stack to C++ and define Vulkan-first rendering.

## Decision
Use **C++** as the runtime/engine implementation language and adopt a **Vulkan-first** rendering direction for V1.

## Alternatives Considered
1. **C# runtime with DirectX-first rendering**
   - Pros:
     - Faster onboarding for existing .NET developers.
     - Mature tooling on Windows.
   - Cons:
     - Weaker alignment with Ubuntu parity for low-level graphics APIs.
     - Increased risk of Windows-first rendering assumptions.
2. **C++ runtime with OpenGL-first rendering**
   - Pros:
     - Broad legacy support and simpler initial bring-up.
   - Cons:
     - Misaligned with stated Vulkan-first roadmap.
     - Lower confidence for modern rendering feature growth.
3. **Rust runtime with Vulkan-first rendering**
   - Pros:
     - Strong safety properties.
   - Cons:
     - Conflicts with V1 stack lock.
     - Team velocity risk due to ecosystem and onboarding cost for current scope.

## Tradeoffs
- We optimize for runtime control, performance, and long-term rendering direction consistency.
- We accept higher implementation complexity versus higher-level managed runtimes.
- Vulkan-first may slow initial renderer ergonomics but reduces future re-platforming risk.

## Consequences
- Positive:
  - Clear low-level performance path for simulation-heavy RTS/sim + RPG workloads.
  - API and platform choices align to Windows/Ubuntu parity early.
- Negative:
  - More engine-level plumbing required in early milestones.
  - Higher initial complexity for contributors unfamiliar with C++/Vulkan.
- Neutral/Follow-up:
  - Additional ADRs can decide abstraction layers (render hardware interface, ECS, etc.).

## Scope and Non-Goals
- In scope:
  - Runtime language and rendering direction baseline for V1.
- Out of scope:
  - Specific graphics backend architecture details.
  - Multiplayer networking, marketplace, or cloud hosting implementation.

## Compliance Checks
- Local-first impact: Strong; runtime executes fully on local machine by default.
- No-code-first UX impact: Neutral direct impact; enables performant editor-generated gameplay execution.
- V1 scope boundaries (single-player, no marketplace, no first-party cloud): Aligned.
- Target OS impact (Windows + Ubuntu): Aligned through cross-platform C++ toolchains and Vulkan support.

## References
- Related ADRs: ADR-0002, ADR-0003, ADR-0004
- External references: GAMEFORGE_V1_BLUEPRINT.md, GAMEFORGE_EXECUTION_PLAN.md
