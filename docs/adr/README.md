# GameForge V1 Architecture Decisions (ADR Index)

This folder captures baseline architecture decisions for GameForge V1 so core stack choices are not re-decided repeatedly.

## V1 Decision Baseline
- [ADR 0001: Runtime Engine Language and Rendering Direction](./0001-runtime-engine-language-and-rendering-direction.md)
- [ADR 0002: Editor and Tooling UI Framework (C#)](./0002-editor-ui-framework-csharp.md)
- [ADR 0003: Project Data Format and Storage Baseline](./0003-project-data-format-and-storage.md)
- [ADR 0004: AI Orchestration Boundary and Contract](./0004-ai-orchestration-boundary-and-contract.md)

## Supporting Template
- [ADR 0000 Template](./0000-template.md)

## Architecture Overview (V1)
GameForge V1 architecture is organized into three locked stack zones:
1. **Runtime/Engine (C++ + Vulkan-first):** Executes game simulation and rendering locally.
2. **Editor/Tooling (C#):** Provides no-code-first visual workflows, project editing, and UX surfaces.
3. **AI Orchestration (Python):** Generates plans and controlled change requests through explicit APIs.

Projects are stored locally with JSON-based manifests/documents and file-referenced assets. This supports transparent save/load, scripted checks, and cross-platform portability across Windows and Ubuntu.

### Scope Guardrails
- Single-player only in V1.
- No marketplace in V1.
- No first-party cloud hosting in V1.
