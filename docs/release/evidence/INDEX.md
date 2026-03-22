# GameForge V1 Evidence Index

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

- `GAMEFORGE_ACCEPTANCE_TEST_MATRIX.md` — canonical AT definitions and RC rule.
- `GAMEFORGE_EXECUTION_PLAN.md` — Milestone 8 target deliverables and exit criteria.
- `GAMEFORGE_DECISIONS_LOCK.md` — locked V1 policy decisions and quality gates.
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

Expected per-run log artifact locations (when runs are executed):

- `docs/release/evidence/logs/windows/`
- `docs/release/evidence/logs/ubuntu/`

---

## 5) Validators and test suites referenced for RC review

- `scripts/validate_traceability.py` — parity/coverage validator for traceability docs.
- `pytest -q` — Python test suite execution used for repo-wide regression signal.
- `dotnet test editor/csharp/tests/GameForge.Editor.Tests.csproj -v minimal` — C# editor suite (conditional on .NET SDK availability).

---

## 6) Open evidence gaps (current)

1. Completed Windows smoke execution package (logs + filled template) is not yet present.
2. Completed Ubuntu smoke execution package (logs + filled template) is not yet present.
3. Dotnet-based C# test execution evidence is missing from this environment due to unavailable SDK.
4. P0 partial acceptance rows (AT-001, AT-003, AT-026) require additional targeted proof beyond current automation.

---

## 7) Ownership placeholders

- **Release owner:** _TBD_
- **QA owner:** _TBD_
- **Engineering owner:** _TBD_
- **Approver signatures/date:** _TBD_
