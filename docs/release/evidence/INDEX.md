# Soul Loom release evidence index

- **Date (UTC):** 2026-03-22
- **Prepared by:** Release Handoff Agent (GPT-5.2-Codex)
- **Primary RC handoff doc:** `docs/release/RC_HANDOFF_V1.md`
- **Baseline commit (short):** `3338203`
- **Owner (Release):** _TBD_

This index centralizes evidence artifacts used for V1 RC readiness review.

---

## 1) Acceptance traceability artifacts

- `docs/release/acceptance_traceability_v1.md` — human-readable acceptance matrix and status narrative.
- `docs/release/acceptance_traceability_v1.json` — source-of-truth machine-readable matrix (`AT-001..AT-031`).

---

## 2) Policy and acceptance baselines

- `SOUL_LOOM_ACCEPTANCE_TEST_MATRIX.md` — canonical AT definitions and RC rule.
- `SOUL_LOOM_EXECUTION_PLAN.md` — Milestone 8 target deliverables and exit criteria.
- `SOUL_LOOM_DECISIONS_LOCK.md` — locked V1 policy decisions and quality gates.
- `docs/release/policy_change_note.md` — policy consistency correction audit note.

---

## 3) Metrics and readiness gate artifacts

- `docs/release/READINESS_METRICS_SPEC.md` — collector spec, thresholds, schema.
- `scripts/collect_readiness_metrics.py` — deterministic readiness metrics collector.
- `docs/release/evidence/readiness_metrics_sample.json` — sample readiness metrics evidence artifact.

---

## 4) Cross-platform smoke artifacts (AT-010 / AT-011)

- `docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md` — executable smoke procedures and pass/fail criteria.
- `docs/release/evidence/windows_smoke_template.md` — Windows execution evidence template.
- `docs/release/evidence/ubuntu_smoke_template.md` — Ubuntu execution evidence template.
- `docs/release/evidence/SMOKE_EVIDENCE_SCHEMA.md` — required schema/field contract for generated smoke artifacts.
- `scripts/run_smoke_and_capture_evidence.py` — deterministic smoke runner + evidence generator for Ubuntu/Windows contract mode.
- `scripts/promote_smoke_acceptance.py` — promotes AT-010/AT-011 traceability after PASS archived bundles.

Local capture convention (gitignored under `runs/`):

- `docs/release/evidence/runs/<timestamp>/smoke_evidence.json`
- `docs/release/evidence/runs/<timestamp>/ubuntu_smoke_evidence.md` or `windows_smoke_evidence.md`
- `docs/release/evidence/runs/<timestamp>/*.log`

Archived (tracked) RC bundles: `docs/release/evidence/archived/README.md`

---

## 5) Validators and test suites referenced for RC review

- `scripts/validate_traceability.py` — parity/coverage validator for traceability docs.
- `pytest -q` — Python test suite execution used for repo-wide regression signal.
- `dotnet test editor/csharp/tests/Soul.Editor.Tests.csproj -v minimal` — C# editor suite (conditional on .NET SDK availability).

---

## 6) Open evidence gaps (current)

1. **AT-001 (P0): HUMAN REQUIRED** — fresh-install proof on clean Windows 11 + Ubuntu 22.04 VMs (not automatable without dedicated bare VMs).
2. **AT-011** — archived: `docs/release/evidence/archived/ubuntu/20260518T044344Z/` (traceability **covered**, 2026-05-18).
3. **AT-010** — archived: `docs/release/evidence/archived/windows/20260518T041200Z/` (traceability **covered**, 2026-05-18).

---

## 7) Ownership placeholders

- **Release owner:** _TBD_
- **QA owner:** _TBD_
- **Engineering owner:** _TBD_
- **Approver signatures/date:** _TBD_

