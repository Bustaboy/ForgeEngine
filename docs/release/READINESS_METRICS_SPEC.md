# Readiness Metrics Collection Spec (Milestone 8 hardening)

## Purpose
`collect_readiness_metrics.py` provides deterministic local evidence for readiness gates tied to acceptance items **AT-020..AT-025**.

## Command
```bash
python3 scripts/collect_readiness_metrics.py --output docs/release/evidence/readiness_metrics_sample.json
```

Optional input fixture:
```bash
python3 scripts/collect_readiness_metrics.py \
  --input path/to/readiness_input.json \
  --output docs/release/evidence/readiness_metrics_sample.json
```

## Input fixture schema
If `--input` is omitted, the script uses a deterministic embedded fixture.

```json
{
  "sessions": [{"id": "s001", "crashed": false}],
  "fps_samples": [60.0, 58.0],
  "scene_load_seconds": [12.1, 11.9],
  "save_checks": [true, true, false],
  "frame_times_ms": [16.4, 17.1, 16.8]
}
```

## Output schema (`gameforge.readiness_metrics.v1`)
- `schema`: fixed schema id.
- `collected_by`: collector script path.
- `input_source`: input fixture path or `embedded_default_fixture`.
- flattened root-level gate input keys (for `SteamReadinessPolicy.LoadMetrics` compatibility):
  - `crash_free_session_rate_percent`
  - `sustained_fps_floor`
  - `fps_60_compliance_percent`
  - `frame_time_p95_ms`
  - `initial_scene_load_seconds`
  - `safe_save_pass_rate_percent`
- `metrics` (gate inputs aligned to `SteamQualityMetrics` naming):
  - `crash_free_session_rate_percent`
  - `sustained_fps_floor`
  - `fps_60_compliance_percent`
  - `frame_time_p95_ms`
  - `initial_scene_load_seconds`
  - `safe_save_pass_rate_percent`
- `gate_evaluation`:
  - `decision`: `ready | blocked_by_critical | requires_warning_ack`
  - `critical_failures`, `warning_failures`
  - `checks[]`: per-acceptance threshold check details

## Threshold mapping
This mapping is aligned with `GAMEFORGE_DECISIONS_LOCK.md` quality gates and existing `SteamReadinessPolicy` gate behavior.

| Acceptance ID | Metric key | Threshold | Severity | Source alignment |
|---|---|---|---|---|
| AT-020 | `crash_free_session_rate_percent` | `>= 97.0` | critical | Decisions lock crash-free target |
| AT-021 | `fps_60_compliance_percent` | `>= 95.0` | warning | Existing Steam readiness warning threshold for 60 FPS coverage |
| AT-022 | `sustained_fps_floor` | `>= 30.0` | critical | Decisions lock critical floor |
| AT-023 | `frame_time_p95_ms` | `< 33.0` | warning | Decisions lock frame-time target + Steam readiness warning gate |
| AT-024 | `initial_scene_load_seconds` | `< 20.0` | critical | Decisions lock initial load target |
| AT-025 (supporting reliability gate) | `safe_save_pass_rate_percent` | `>= 100.0` | critical | Decisions lock save/load integrity |

## Determinism guarantees
- No timestamps or random values are written into the output artifact.
- Keys are emitted in stable order.
- Default fixture values are embedded and static.
- Numeric output is rounded deterministically to three decimals.

## Wiring to existing readiness gate
The output `metrics` object is intentionally field-compatible with `SteamQualityMetrics` JSON loading (`SteamReadinessPolicy.LoadMetrics`) and can be fed directly to:

```bash
dotnet run --project editor/csharp/GameForge.Editor.csproj -- \
  --steam-readiness docs/release/evidence/readiness_metrics_sample.json
```

This keeps gate decision authority in the C# readiness flow while letting local collection remain script-driven and reproducible.

