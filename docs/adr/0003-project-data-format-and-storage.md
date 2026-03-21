# ADR 0003: Project Data Format and Storage Baseline

- **Status:** Accepted
- **Date:** 2026-03-21
- **Owners:** Architecture + Tools
- **Deciders:** GameForge V1 maintainers

## Context
V1 needs a durable project representation for local save/load, AI-assisted edits, diffability in source control, and recovery-friendly workflows. The system must stay local-first and understandable for non-coder workflows while supporting future growth.

## Decision
Adopt a **JSON-based project manifest and content document format** for V1 metadata/configuration, with assets stored as local files and stable IDs referencing them.

## Alternatives Considered
1. **Binary-only project bundle format**
   - Pros:
     - Potentially faster load/write for large scenes.
     - Easier single-file packaging.
   - Cons:
     - Harder human inspection and debugging.
     - Poor diff/merge transparency, which weakens trust and recovery workflows.
2. **SQLite-first monolithic project database**
   - Pros:
     - Transactional integrity and query capabilities.
   - Cons:
     - Harder manual inspection/editing.
     - Additional migration complexity early in V1.
3. **YAML for all project metadata**
   - Pros:
     - Human-readable and flexible.
   - Cons:
     - Greater parsing ambiguity and formatting variance.
     - Less strict structure by default than JSON for tool contracts.

## Tradeoffs
- We optimize for readability, interoperability, and AI/editor contract clarity.
- We accept potential size/performance inefficiency compared with binary/database-first strategies.
- We constrain format flexibility to preserve deterministic tooling behavior.

## Consequences
- Positive:
  - Easier debugging, diffs, and scripted validation checks.
  - Clear contract for C#, C++, and Python components.
- Negative:
  - Need schema/versioning discipline to avoid format drift.
  - May need compaction/performance tactics for large projects.
- Neutral/Follow-up:
  - Future ADR may introduce hybrid storage (JSON manifests + optimized caches).

## Scope and Non-Goals
- In scope:
  - V1 baseline project data representation and local storage posture.
- Out of scope:
  - Final schema definitions for every gameplay subsystem.
  - Cloud synchronization or hosted storage.

## Compliance Checks
- Local-first impact: Strong; all project state persists locally.
- No-code-first UX impact: Positive; transparent saves improve user confidence and recoverability.
- V1 scope boundaries (single-player, no marketplace, no first-party cloud): Aligned.
- Target OS impact (Windows + Ubuntu): Aligned via portable file-system based storage.

## References
- Related ADRs: ADR-0001, ADR-0002, ADR-0004
- External references: GAMEFORGE_V1_BLUEPRINT.md, GAMEFORGE_EXECUTION_PLAN.md
