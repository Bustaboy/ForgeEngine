# Soul Loom RC Readiness Handoff

- **Date (UTC):** 2026-03-22
- **Prepared by:** Release Handoff Agent (GPT-5.2-Codex)
- **Owner (Engineering):** _TBD_
- **Owner (QA):** _TBD_
- **Owner (Release):** _TBD_
- **Baseline commit (short):** `3338203`
- **Baseline commit (full):** `33382034a14bee0d03f83a0769a484089a91b26c`
- **Milestone:** Milestone 8 (Steam Readiness + RC)

---

## 1) Overall readiness summary

Soul Loom is **not yet RC-ship ready** under the current acceptance policy because multiple **P0 acceptance tests are partial** and require additional production-evidence closure (especially cross-platform smoke and long-session/fresh-install validation).

Current P0 snapshot from the traceability source:

- **P0 total:** 14
- **Pass (covered):** 9
- **Partial:** 5
- **Missing:** 0

P0 partial IDs: **AT-001, AT-003, AT-010, AT-011, AT-026**.

Because the acceptance matrix requires **all P0 tests pass** before RC proceeds, current state is below release threshold.

---

## 2) P0 status table (pass / partial / missing)

| AT ID | Area | Current status | Classification | Evidence | Blocking note | Owner |
|---|---|---|---|---|---|---|
| AT-001 | Startup (fresh install) | partial | Partial | `tests/test_m1_skeleton.py::TestMilestone1Skeleton::test_runtime_cpp_compiles_and_runs`; `tests/test_m1_skeleton.py::TestMilestone1Skeleton::test_bootstrap_sh_default_mode_contract` | Needs true clean-machine Windows/Ubuntu evidence | Platform QA |
| AT-002 | Project lifecycle | covered | Pass | `tests/test_project_lifecycle.py::test_at002_create_save_reopen_preserves_project_data` | None | Editor/Core |
| AT-003 | Interview continuity | partial | Partial | `tests/test_interview_contract.py::TestInterviewContract::test_editor_csharp_owns_interview_session_persistence_contract`; `editor/csharp/tests/InterviewUncertaintyTests.cs::SaveAndLoad_RoundTripsSelectedOptionId` | Needs long-session restart continuity evidence | AI Orchestration |
| AT-006 | Prototype generation | covered | Pass | `tests/test_prototype_generation.py::TestPrototypeGeneration::test_orchestrator_generates_scaffold_from_brief`; `tests/test_prototype_generation.py::TestPrototypeGeneration::test_smoke_script_generation_and_launch` | None | AI Orchestration |
| AT-007 | Lock protection | covered | Pass | `tests/test_prototype_generation.py::TestPrototypeGeneration::test_partial_regeneration_skips_locked_paths_without_confirmation`; `tests/test_prototype_generation.py::TestPrototypeGeneration::test_partial_regeneration_overwrites_locked_paths_after_confirmation` | None | AI Orchestration |
| AT-009 | Undo/redo | covered | Pass | `editor/csharp/tests/EditorShellTests.cs::UndoRedoTimeline_SupportsMultiStepRollbackAndReapply` | None | Editor/Core |
| AT-010 | Windows smoke | partial | Partial | `docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md#4-windows-smoke-procedure-at-010`; `docs/release/evidence/windows_smoke_template.md` | Requires executed run + archived logs | Release Engineering |
| AT-011 | Ubuntu smoke | partial | Partial | `docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md#3-ubuntu-smoke-procedure-at-011`; `docs/release/evidence/ubuntu_smoke_template.md` | Requires executed run + archived logs | Release Engineering |
| AT-013 | Asset import | covered | Pass | `tests/test_asset_import_pipeline.py::test_imported_assets_are_auto_tagged_and_searchable`; `editor/csharp/tests/EditorShellTests.cs::AssetBrowserFilter_ReturnsTagAndCategoryMatches` | None | Content Pipeline |
| AT-014 | License allow-list | covered | Pass | `tests/test_asset_import_pipeline.py::test_blocked_or_unclear_license_returns_actionable_errors` | None | Content Pipeline |
| AT-016 | Readiness gate blocks critical | covered | Pass | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` | None | Compliance/Release |
| AT-020 | Crash-free target | covered | Pass | `scripts/collect_readiness_metrics.py`; `docs/release/evidence/readiness_metrics_sample.json`; `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` | Evidence currently fixture-driven, not target-hardware runtime capture | Quality/Telemetry |
| AT-021 | FPS target | covered | Pass | `scripts/collect_readiness_metrics.py`; `docs/release/evidence/readiness_metrics_sample.json`; `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_WarningsRequireAcknowledgement_AndAllowOverride` | Same fixture caveat as above | Runtime/Perf |
| AT-022 | FPS critical floor | covered | Pass | `scripts/collect_readiness_metrics.py`; `docs/release/evidence/readiness_metrics_sample.json`; `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` | Same fixture caveat as above | Runtime/Perf |
| AT-025 | Save/load integrity | covered | Pass | `tests/test_project_lifecycle.py::test_at025_save_load_regression_loop_detects_corruption` | None | Runtime + QA Automation |
| AT-026 | Quest dead-end blockers | partial | Partial | `tests/test_bot_playtesting.py::test_bot_playtest_passes_on_generated_sample`; `tests/test_prototype_generation.py::test_consequence_choice_updates_npc_world_and_branch_state` | Needs explicit dead-end blocker scanner + fail gate | AI Testing |

---

## 3) Unresolved blockers and environment limitations

### Release blockers (must close)

1. **AT-001 partial**: No fresh-install evidence from clean target OS images in evidence package.
2. **AT-003 partial**: No long-session resume evidence across restart.
3. **AT-010 partial**: Windows smoke template/runbook exists, but completed run artifact/log set is not attached.
4. **AT-011 partial**: Ubuntu smoke template/runbook exists, but completed run artifact/log set is not attached.
5. **AT-026 partial**: No deterministic dead-end blocker detection gate proving zero critical blockers.

### Environment limitations observed in this handoff run

- **`dotnet` not available in current execution environment**, so C# test command could not be executed here.
- Cross-platform smoke execution on native Windows and Ubuntu host targets is **not represented by this single environment** and remains pending evidence.

---

## 4) Evidence index (tests, artifacts, runbooks, validators)

Primary index: **`docs/release/evidence/INDEX.md`**.

Key governance and traceability sources:

- `docs/release/acceptance_traceability_v1.md`
- `docs/release/acceptance_traceability_v1.json`
- `GAMEFORGE_ACCEPTANCE_TEST_MATRIX.md`
- `GAMEFORGE_EXECUTION_PLAN.md` (Milestone 8)
- `GAMEFORGE_DECISIONS_LOCK.md`
- `docs/release/policy_change_note.md`
- `docs/release/READINESS_METRICS_SPEC.md`
- `docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md`

---

## 5) Recommendation (explicit go/no-go)

## **Recommendation: NO-GO**

### Rationale

- Release criteria require all P0 acceptance tests to pass.
- Current state has **5 P0 tests in partial status** (AT-001/003/010/011/026).
- Several P0 rows depend on manual/operational evidence not yet produced and archived.
- Environment validation is incomplete due to missing `dotnet` in this run context.

A **Conditional Go** could be re-evaluated after the required-before-ship checklist below is fully complete with evidence links and owner sign-off.

---

## 6) Required-before-ship checklist

- [ ] **AT-001 closure:** Fresh-install startup evidence captured on target Windows and Ubuntu environments.
- [ ] **AT-003 closure:** Long interview session persistence test with restart and continuity proof added and passing.
- [ ] **AT-010 closure:** Windows smoke run executed per runbook, logs archived, template completed with pass verdict.
- [ ] **AT-011 closure:** Ubuntu smoke run executed per runbook, logs archived, template completed with pass verdict.
- [ ] **AT-026 closure:** Deterministic quest dead-end blocker scan integrated with failing gate condition, evidence attached.
- [ ] **C# test validation:** `dotnet test editor/csharp/tests/GameForge.Editor.Tests.csproj -v minimal` executed in environment with .NET SDK and result attached.
- [ ] **Final RC review:** Engineering + QA + Release owners sign off on evidence index and no open P0 gaps.

---

## Appendix A — Validation commands run for this handoff (exact outputs + exit codes)

### 1) Traceability validator

**Command**

```bash
python3 scripts/validate_traceability.py; echo EXIT_CODE:$?
```

**Output**

```text
Traceability validation PASSED: 31/31 AT IDs complete and in parity
EXIT_CODE:0
```

### 2) Python test suite

**Command**

```bash
pytest -q; echo EXIT_CODE:$?
```

**Output**

```text
.............sssss.....................................                  [100%]
50 passed, 5 skipped in 7.07s
EXIT_CODE:0
```

### 3) C# tests (conditional)

**Command**

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test editor/csharp/tests/GameForge.Editor.Tests.csproj -v minimal; EC=$?; echo EXIT_CODE:$EC; else echo 'dotnet not found'; echo EXIT_CODE:127; fi
```

**Output**

```text
dotnet not found
EXIT_CODE:127
```

Interpretation: environment limitation, not a test-failure verdict.

---

## Appendix B — Commit and sign-off placeholders

- **Docs package commit:** _TBD after merge_
- **Release candidate tag:** _TBD_
- **QA sign-off (name/date):** _TBD_
- **Engineering sign-off (name/date):** _TBD_
- **Release sign-off (name/date):** _TBD_

