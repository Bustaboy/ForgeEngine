# Soul Loom Architecture Overview

This page summarizes V1 architecture boundaries and links to the baseline ADR set.

## Baseline Decisions
- [ADR Index](./adr/README.md)
- [ADR 0001: Runtime Engine Language and Rendering Direction](./adr/0001-runtime-engine-language-and-rendering-direction.md)
- [ADR 0002: Editor and Tooling UI Framework (C#)](./adr/0002-editor-ui-framework-csharp.md)
- [ADR 0003: Project Data Format and Storage Baseline](./adr/0003-project-data-format-and-storage.md)
- [ADR 0004: AI Orchestration Boundary and Contract](./adr/0004-ai-orchestration-boundary-and-contract.md)

## V1 System Boundaries
- **Runtime/Engine (C++):** Owns game execution, simulation, and Vulkan-first rendering.
- **Editor/Tooling UI (C#):** Owns no-code-first authoring experience and user-facing workflows.
- **AI Orchestration (Python):** Owns AI planning/automation through explicit contracts with editor/runtime APIs.

## Data and Execution Model
- Local-first project storage using JSON-based manifests/documents and local asset files.
- Single-player workflows only for V1.
- Windows and Ubuntu are first-class targets.
- No marketplace and no first-party cloud hosting in V1.

