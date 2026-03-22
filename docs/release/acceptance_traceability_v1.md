# GameForge V1 Acceptance Traceability Matrix (Milestone 8 hardening)

This artifact maps **AT-001..AT-031** to current automated evidence in-repo, fallback manual procedures, and identified gaps.

Source references used:
- `GAMEFORGE_ACCEPTANCE_TEST_MATRIX.md`
- `GAMEFORGE_EXECUTION_PLAN.md`
- `CODEX_PROMPT_PLAYBOOK.md`
- current Python and C# test suites under `tests/` and `editor/csharp/tests/`

## Status legend
- **covered**: direct automated test coverage exists for the stated acceptance intent.
- **partial**: some automation exists but does not fully satisfy the end-to-end acceptance intent or environment matrix.
- **missing**: no automated evidence found in the current repo.

## P0-first traceability table

| AT ID | Priority | Status | Automated evidence (current repo) | Manual procedure (if no automation) | Gap / next action (required for missing P0) |
|---|---:|---|---|---|---|
| AT-001 | P0 | partial | `tests/test_m1_skeleton.py::TestMilestone1Skeleton::test_runtime_cpp_compiles_and_runs`; `tests/test_m1_skeleton.py::TestMilestone1Skeleton::test_bootstrap_sh_default_mode_contract` | Run clean-machine install/start flow on target OS; verify first launch has no crash dialog and reaches shell. | Expand to true fresh-install smoke in CI images for Windows+Ubuntu (Owner: Platform QA). |
| AT-002 | P0 | missing | None found. | Create new project, add minimal content, save, close app, reopen project, diff persisted files + verify UI state. | **Add `tests/test_project_lifecycle.py` for create/save/reopen integrity and data hash check** (Owner: Editor/Core). |
| AT-003 | P0 | partial | `tests/test_interview_contract.py::TestInterviewContract::test_editor_csharp_owns_interview_session_persistence_contract`; `editor/csharp/tests/InterviewUncertaintyTests.cs::SaveAndLoad_RoundTripsSelectedOptionId` | Execute long interview (>30 turns), restart app mid-session, confirm full state restoration incl. selected options and question planner continuation. | Add long-session resume integration test with multi-module interview transcript fixture (Owner: AI Orchestration). |
| AT-006 | P0 | covered | `tests/test_prototype_generation.py::TestPrototypeGeneration::test_orchestrator_generates_scaffold_from_brief`; `tests/test_prototype_generation.py::TestPrototypeGeneration::test_smoke_script_generation_and_launch` | n/a | — |
| AT-007 | P0 | covered | `tests/test_prototype_generation.py::TestPrototypeGeneration::test_partial_regeneration_skips_locked_paths_without_confirmation`; `tests/test_prototype_generation.py::TestPrototypeGeneration::test_partial_regeneration_overwrites_locked_paths_after_confirmation` | n/a | — |
| AT-009 | P0 | covered | `editor/csharp/tests/EditorShellTests.cs::UndoRedoTimeline_SupportsMultiStepRollbackAndReapply` | n/a | — |
| AT-010 | P0 | missing | No Windows-specific smoke automation found. | Run end-to-end flow on Windows: Home → Interview → Prototype → Editor → Testing → Publish readiness; record pass/fail checklist. | **Add CI Windows smoke job invoking bootstrap + prototype launch + readiness checks** (Owner: Release Engineering). |
| AT-011 | P0 | missing | No Ubuntu-specific smoke automation found. | Run same end-to-end smoke checklist on Ubuntu target spec; store run artifact logs. | **Add CI Ubuntu smoke job mirroring Windows smoke path** (Owner: Release Engineering). |
| AT-013 | P0 | covered | `tests/test_asset_import_pipeline.py::test_imported_assets_are_auto_tagged_and_searchable`; `editor/csharp/tests/EditorShellTests.cs::AssetBrowserFilter_ReturnsTagAndCategoryMatches` | n/a | — |
| AT-014 | P0 | covered | `tests/test_asset_import_pipeline.py::test_blocked_or_unclear_license_returns_actionable_errors` | n/a | — |
| AT-016 | P0 | covered | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` | n/a | — |
| AT-020 | P0 | partial | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` validates threshold behavior, not live crash telemetry collection. | Run representative session set, calculate crash-free rate, verify gate blocks below 97%. | Add telemetry-to-metric aggregation test using recorded session fixtures (Owner: Quality/Telemetry). |
| AT-021 | P0 | partial | `editor/csharp/tests/SteamReadinessPolicyTests.cs` covers policy fields but no runtime FPS benchmark harness. | Run validation scenes on target hardware and capture achieved 60 FPS compliance. | Add automated perf capture harness feeding `SteamQualityMetrics` from measured frame data (Owner: Runtime/Perf). |
| AT-022 | P0 | partial | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` checks critical fail path for low FPS floor. | Stress scene run with sustained floor measurement and publish-gate assertion. | Add sustained-FPS-floor detector integration test with time-windowed trace input (Owner: Runtime/Perf). |
| AT-025 | P0 | missing | No core save/load regression loop test for gameplay saves found. | Execute scripted loop of save/load cycles across generated prototype scenarios; require 100% consistency. | **Add automated save/load regression loop test over sample prototype save hook** (Owner: Runtime + QA Automation). |
| AT-026 | P0 | partial | `tests/test_bot_playtesting.py` and `tests/test_prototype_generation.py::test_consequence_choice_updates_npc_world_and_branch_state` provide dead-end-related signals but no strict “0 critical blockers” gate assertion. | Run bot playtest and branch traversal for all quest nodes; verify no unresolvable critical dead-end states. | Add dead-end blocker scanner with fail-on-critical count >0 in CI (Owner: AI Testing). |
| AT-004 | P1 | covered | `tests/test_uncertainty_options.py::test_ambiguous_unknown_input_returns_exactly_three_options`; `editor/csharp/tests/InterviewUncertaintyTests.cs::SaveAsync_RejectsDecisionWithoutExactlyThreeOptions` | n/a | — |
| AT-005 | P1 | partial | `tests/test_uncertainty_options.py::test_think_for_me_mode_returns_three_directional_concepts`; `editor/csharp/tests/InterviewUncertaintyTests.cs::ThinkForMeEnvelope_MapsConfirmationGateAndThreeProposals` | Verify generated options are coherent for at least 5 concept prompts; confirm explicit confirmation gate before committing choice. | Add semantic quality rubric test fixture for proposal coherence scoring. |
| AT-008 | P1 | missing | None found for “5 sequential failures → guided manual mode”. | Trigger five consecutive failing AI operations and verify guided manual fallback UI appears. | Add failure-counter integration test in orchestrator/editor boundary. |
| AT-012 | P1 | partial | Rendering direction assertions exist in `tests/test_prototype_generation.py` and `editor/csharp/tests/EditorShellTests.cs` (`vulkan-first` value), but no Vulkan startup path init test. | Launch runtime/editor on Vulkan-capable machine; verify Vulkan backend selected at startup. | Add startup backend probe test that asserts Vulkan renderer initialization logs. |
| AT-015 | P1 | missing | No attribution export tests found. | Export project containing CC-BY assets; verify attribution bundle file generation and contents. | Add attribution bundle generator tests against mixed-license catalog fixtures. |
| AT-017 | P1 | covered | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_WarningsRequireAcknowledgement_AndAllowOverride` | n/a | — |
| AT-018 | P1 | covered | `editor/csharp/tests/SteamReadinessPolicyTests.cs::BuildAuditTrail_GeneratesHashSignature_AndRequiresUploadConsent`; `AuditWriteAndUpload_SupportFilenameOnlyPaths` | n/a | — |
| AT-019 | P1 | covered | `editor/csharp/tests/SteamReadinessPolicyTests.cs::BuildAuditTrail_GeneratesHashSignature_AndRequiresUploadConsent` | n/a | — |
| AT-023 | P1 | missing | No frame-time p95 capture/assert tests found. | Run frame capture on validation scene; compute p95 frametime and compare with <33ms criterion. | Add perf parser test consuming frame trace and asserting p95 threshold policy. |
| AT-024 | P1 | partial | `editor/csharp/tests/SteamReadinessPolicyTests.cs::Evaluate_CriticalFailure_BlocksPublish` includes load-time threshold logic, but no measured startup/load instrumentation test. | Measure first scene load on target hardware; validate threshold under 20s and gate behavior. | Add automated load-time probe integrated with readiness metric pipeline. |
| AT-027 | P2 | missing | No tests found for Git default-off behavior. | Create project and inspect VCS state/settings to confirm Git disabled by default. | Add project creation default-settings test in editor shell suite. |
| AT-028 | P2 | missing | No tests found for Git opt-in repository initialization. | Enable Git in project settings and verify `.git/` initialized + first status clean. | Add opt-in Git init integration test (may require env guard for Git availability). |
| AT-029 | P1 | missing | No automated discoverability/navigation flow test found. | Conduct usability walkthrough (Project Home → Interview → Prototype → Editor → Testing → Publish) without docs; record blockers. | Add UI navigation smoke test with panel/route availability assertions. |
| AT-030 | P1 | partial | `editor/csharp/tests/EditorShellTests.cs::Selection_UpdatesInspectorAndAiContext` validates simple inspector section exists; does not verify advanced controls collapsed by default. | Open inspector for selected object; confirm simple controls visible first and advanced collapsed but accessible. | Add inspector state test asserting default collapsed advanced pane and toggle behavior. |
| AT-031 | P1 | missing | No automated plain-language label comprehension tests found. | Review core dialogs/buttons with non-technical reviewer checklist; log unclear terms and rewrite candidates. | Add UX copy lint/checklist artifact plus snapshot assertions for key labels. |

## Gap summary (grouped by priority)

### P0
- **Covered:** 6 (`AT-006`, `AT-007`, `AT-009`, `AT-013`, `AT-014`, `AT-016`)
- **Partial:** 6 (`AT-001`, `AT-003`, `AT-020`, `AT-021`, `AT-022`, `AT-026`)
- **Missing:** 4 (**`AT-002`, `AT-010`, `AT-011`, `AT-025`**)
- **Immediate hardening focus:** implement missing P0 automation first, then convert partial P0 perf/reliability checks from policy-only validation to measured integration inputs.

### P1
- **Covered:** 4 (`AT-004`, `AT-017`, `AT-018`, `AT-019`)
- **Partial:** 4 (`AT-005`, `AT-012`, `AT-024`, `AT-030`)
- **Missing:** 5 (`AT-008`, `AT-015`, `AT-023`, `AT-029`, `AT-031`)

### P2
- **Covered:** 0
- **Partial:** 0
- **Missing:** 2 (`AT-027`, `AT-028`)

## Notes and limitations
- Coverage statuses are based only on repository-visible automated tests and documents at time of analysis.
- Several performance and cross-platform acceptance rows are currently represented by policy logic tests, not measured runtime traces on target hardware/OS.
- Manual procedures are provided where automation is absent to keep release verification executable while gaps are being closed.
