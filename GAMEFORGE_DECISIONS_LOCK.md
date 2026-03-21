# GameForge V1 Decisions Lock

This file is the single-source lock for non-negotiable V1 decisions.
If another document conflicts with this file, this file wins unless explicitly superseded in a dated update.

## Product and Scope
- Single-player only in V1.
- Local-first desktop app.
- No first-party cloud hosting in V1.
- No marketplace in V1.
- Modding support deferred to later versions.

## Platform and Rendering
- Native Windows support required.
- Native Linux support required, with Ubuntu as first supported distro target.
- Vulkan-first rendering direction.

## Tech Stack Lock
- Core runtime/engine systems: C++.
- Editor and tooling UI: C#.
- AI orchestration and automation tooling: Python.

## User Experience and Safety
- No-code-first UX is mandatory.
- Major AI changes require user confirmation.
- Locked content cannot be overwritten silently.
- If AI fails the same requested operation 5 times in sequence, offer guided manual mode and preserve a retry option.

## Project Defaults
- Git integration is optional.
- Default for new projects: Git OFF.
- Provide explicit “Enable Git” action in the app.

## Publishing and Compliance
- Steam-first publishing flow.
- Critical policy/readiness issues block publish.
- Warnings can be user-overridden with explicit acknowledgment.
- Generate publish audit trail automatically.
- External audit submission requires explicit user confirmation.

## Asset Licensing Allow-List (V1)
Allowed:
- CC0 / Public Domain
- CC-BY 4.0 (with attribution export)
- User-provided assets with explicit rights confirmation

Blocked:
- CC-BY-SA
- CC-BY-NC
- Any source with missing or unclear license metadata

## Commercial Policy
- Non-commercial usage: free.
- Commercial trigger: paid games or MTX.
- Revenue share: 5% after first $1,000 gross revenue per game.

## Quality Gates (V1 Defaults)
- Crash-free session rate target: >= 97%.
- Performance target: 60 FPS in core gameplay scenes.
- Critical fail: sustained below 30 FPS on target-spec validation scenes.
- Frame-time target: p95 < 33ms in core gameplay flows.
- Initial scene load target: < 20 seconds on target hardware.
- Save/load regression integrity: 100% pass.
- Quest/progression critical dead-end blockers: 0 at release candidate.

## Target Creator Hardware (Baseline)
- CPU: Intel i5-class
- GPU: RTX 2070-class
- RAM: 16GB

## Change Control
- Any change to this file requires:
  1) reason for change,
  2) affected docs list,
  3) migration/impact note,
  4) updated date and owner.
