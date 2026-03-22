# Policy Change Control Note

- **Date (UTC):** 2026-03-22
- **Owner:** GPT-5.2-Codex (implementation agent)
- **Milestone:** Milestone 8 hardening
- **Policy area:** Commercial revenue-share threshold

## Reason for change
Implementation and test messaging used an incorrect higher trigger, which conflicted with locked V1 policy docs that define revenue share as **5% after the first $1,000 gross revenue per game**.

## Source of truth
- `GAMEFORGE_DECISIONS_LOCK.md` (Commercial Policy section)
- `GAMEFORGE_EXECUTION_PLAN.md` (Milestone 8 deliverables)

## Change summary
Canonical threshold is set to **$1,000 USD** across implementation and evidence artifacts.

## Impacted files
- `editor/csharp/EditorShell/CommercialPolicy.cs`
- `editor/csharp/tests/SteamReadinessPolicyTests.cs`
- `docs/release/acceptance_traceability_v1.md`
- `docs/release/policy_change_note.md`

## Migration / impact note
- This is a policy-consistency correction.
- Runtime behavior now matches the locked policy docs and execution plan.
- No schema migration required.
- User-facing policy text now shows the canonical `$1,000` trigger.

## Auditability
- Traceability artifact updated to include explicit automated evidence for commercial threshold text behavior.
- Validation includes repository scan for conflicting threshold strings.
