# ForgeEngine Test Policy

This document defines the minimum testing standard for current and future code in ForgeEngine.

## Goals

- Catch regressions before merge.
- Match test depth to change risk instead of chasing blanket coverage numbers.
- Keep fast pull-request feedback separate from slower release-confidence checks.
- Make acceptance gaps explicit and trackable.

## Required test layers

Every change must add or update tests at the right layer:

- **Unit tests** for pure logic in Python, C#, or C++.
- **Integration tests** for contracts between modules, file formats, persistence, CLI modes, and editor/orchestrator boundaries.
- **Smoke tests** for user-visible workflows such as bootstrap, runtime launch, editor launch, and generation flows.
- **Platform/release validation** for real Windows and Ubuntu behavior, packaging, and release evidence.

## Rules by change type

- Logic change: add or update at least one unit test.
- Cross-module behavior change: add or update at least one integration test.
- User workflow change: add or update a smoke or end-to-end test.
- Bug fix: add a regression test that fails before the fix and passes after it.
- Native runtime/rendering/build change: validate through CMake/runtime build coverage and the relevant smoke path.

## CI gates

### Fast PR gate

The enforced pull-request gate should start with stable, deterministic suites and expand as legacy failures are eliminated.

Every pull request must pass:

- Acceptance traceability validation
- Stable Python contract tests under `tests/`
- Stable C# editor contract tests under `editor/csharp/tests/`

Current enforced gate in this repository:

- `python scripts/validate_traceability.py`
- `python -m pytest -q tests/test_traceability_validator.py tests/test_m1_skeleton.py -p no:cacheprovider`
- `dotnet test editor/csharp/tests/GameForge.Editor.Tests.csproj --filter "FullyQualifiedName~GameForge.Editor.Tests.PlaytestReportViewerTests|FullyQualifiedName~GameForge.Editor.Tests.InterviewLongSessionContinuityTests|FullyQualifiedName~GameForge.Editor.Tests.InterviewUncertaintyTests|FullyQualifiedName~GameForge.Editor.Tests.SteamReadinessPolicyTests" --no-restore -v minimal`

Expansion target after the known-red suites are repaired:

- full Python test suite under `tests/`
- full C# editor test suite under `editor/csharp/tests/`

These gate checks should stay deterministic and reasonably fast.

### Slower confidence gates

These may run on schedule, pre-merge, or release workflows:

- Native runtime build
- Bootstrap smoke
- Cross-platform smoke evidence generation
- Packaging workflows

## Environment-sensitive tests

Tests must distinguish unsupported hosts from true regressions.

- Skip when required host capabilities are absent, such as Vulkan, GLFW, or a usable C++ toolchain.
- Fail when the environment should support the feature and behavior is wrong.
- Keep skip conditions narrow and tied to explicit prerequisites.

## Coverage expectations

ForgeEngine does not require a vanity global coverage number as the sole merge gate.

Instead:

- changed behavior must be tested
- critical paths must have regression coverage
- acceptance gaps must be tracked explicitly in `docs/release/acceptance_traceability_v1.json`

## Acceptance traceability

Acceptance coverage is tracked in:

- `docs/release/acceptance_traceability_v1.json`
- `docs/release/acceptance_traceability_v1.md`

Rows marked `missing` or `partial` must include a concrete next action. New features should update traceability when they affect acceptance scope.

## Commands

Typical local validation commands:

```bash
python3 scripts/validate_traceability.py
python3 -m pytest -q tests/test_traceability_validator.py tests/test_m1_skeleton.py -p no:cacheprovider
dotnet test editor/csharp/tests/GameForge.Editor.Tests.csproj --filter "FullyQualifiedName~GameForge.Editor.Tests.PlaytestReportViewerTests|FullyQualifiedName~GameForge.Editor.Tests.InterviewLongSessionContinuityTests|FullyQualifiedName~GameForge.Editor.Tests.InterviewUncertaintyTests|FullyQualifiedName~GameForge.Editor.Tests.SteamReadinessPolicyTests" --no-restore -v minimal
```

Bootstrap smoke examples:

```bash
./scripts/bootstrap.sh --launcher-smoke
pwsh -f scripts/bootstrap.ps1 -LauncherSmoke
```

## Merge policy

Changes should not merge when:

- required tests for the changed behavior are missing
- a bug fix lacks regression coverage
- traceability metadata is stale for affected acceptance scope
- PR CI is red without an approved, documented exception
