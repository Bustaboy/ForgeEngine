#!/usr/bin/env python3
"""Collect deterministic local readiness metrics for AT-020..AT-024.

This script is intentionally local-first: it reads plain JSON fixture inputs,
computes quality metrics, evaluates thresholds, and writes one evidence artifact.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

SCHEMA = "gameforge.readiness_metrics.v1"

THRESHOLDS: dict[str, dict[str, Any]] = {
    "crash_free_session_rate_percent": {"operator": ">=", "value": 97.0, "acceptance_id": "AT-020", "severity": "critical"},
    "fps_60_compliance_percent": {"operator": ">=", "value": 95.0, "acceptance_id": "AT-021", "severity": "warning"},
    "sustained_fps_floor": {"operator": ">=", "value": 30.0, "acceptance_id": "AT-022", "severity": "critical"},
    "initial_scene_load_seconds": {"operator": "<", "value": 20.0, "acceptance_id": "AT-024", "severity": "critical"},
    "safe_save_pass_rate_percent": {"operator": ">=", "value": 100.0, "acceptance_id": "AT-025", "severity": "critical"},
}

# Deterministic fallback fixture used when no input is supplied.
DEFAULT_INPUT: dict[str, Any] = {
    "sessions": [
        {"id": "s001", "crashed": False},
        {"id": "s002", "crashed": False},
        {"id": "s003", "crashed": False},
        {"id": "s004", "crashed": False},
        {"id": "s005", "crashed": False},
        {"id": "s006", "crashed": False},
        {"id": "s007", "crashed": False},
        {"id": "s008", "crashed": False},
        {"id": "s009", "crashed": False},
        {"id": "s010", "crashed": False},
    ],
    "fps_samples": [60.0, 59.0, 62.0, 60.0, 58.0, 61.0, 60.0, 57.0, 63.0, 60.0],
    "scene_load_seconds": [12.2, 11.8, 12.0],
    "save_checks": [True, True, True, True, True],
    "frame_times_ms": [16.4, 16.8, 17.1, 16.6, 17.3, 16.9, 16.2, 16.5, 17.0, 16.7],
}


def _read_input(path: Path | None) -> dict[str, Any]:
    if path is None:
        return DEFAULT_INPUT
    return json.loads(path.read_text(encoding="utf-8"))


def _pct(part: int, whole: int) -> float:
    return round((part / whole) * 100.0, 3) if whole else 0.0


def _p95(values: list[float]) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    index = max(0, min(len(ordered) - 1, int(round((len(ordered) - 1) * 0.95))))
    return round(float(ordered[index]), 3)


def collect_metrics(payload: dict[str, Any]) -> dict[str, float]:
    sessions = payload.get("sessions", [])
    fps_samples = payload.get("fps_samples", [])
    load_samples = payload.get("scene_load_seconds", [])
    save_checks = payload.get("save_checks", [])
    frame_times = payload.get("frame_times_ms", [])

    crash_free_sessions = sum(1 for s in sessions if not s.get("crashed", False))
    fps_at_or_above_60 = sum(1 for fps in fps_samples if float(fps) >= 60.0)
    passing_saves = sum(1 for ok in save_checks if bool(ok))

    return {
        "crash_free_session_rate_percent": _pct(crash_free_sessions, len(sessions)),
        "sustained_fps_floor": round(min((float(v) for v in fps_samples), default=0.0), 3),
        "fps_60_compliance_percent": _pct(fps_at_or_above_60, len(fps_samples)),
        "initial_scene_load_seconds": round(sum(float(v) for v in load_samples) / len(load_samples), 3) if load_samples else 0.0,
        "safe_save_pass_rate_percent": _pct(passing_saves, len(save_checks)),
        "frame_time_p95_ms": _p95([float(v) for v in frame_times]),
    }


def _evaluate_single(metric_value: float, operator: str, threshold_value: float) -> bool:
    if operator == ">=":
        return metric_value >= threshold_value
    if operator == "<":
        return metric_value < threshold_value
    raise ValueError(f"Unsupported operator: {operator}")


def evaluate(metrics: dict[str, float]) -> dict[str, Any]:
    checks: list[dict[str, Any]] = []
    critical_failures = 0
    warning_failures = 0

    for metric_name, threshold in THRESHOLDS.items():
        observed = metrics[metric_name]
        passed = _evaluate_single(observed, str(threshold["operator"]), float(threshold["value"]))
        checks.append(
            {
                "acceptance_id": threshold["acceptance_id"],
                "metric": metric_name,
                "severity": threshold["severity"],
                "threshold": f"{threshold['operator']} {threshold['value']}",
                "observed": observed,
                "passed": passed,
            }
        )
        if not passed:
            if threshold["severity"] == "critical":
                critical_failures += 1
            else:
                warning_failures += 1

    # AT-023 tracking is informational in this revision; the p95 metric is emitted for evidence.
    checks.append(
        {
            "acceptance_id": "AT-023",
            "metric": "frame_time_p95_ms",
            "severity": "warning",
            "threshold": "< 33.0",
            "observed": metrics["frame_time_p95_ms"],
            "passed": metrics["frame_time_p95_ms"] < 33.0,
            "note": "Informational readiness signal; publish gate currently follows SteamReadinessPolicy fields.",
        }
    )

    decision = "ready"
    if critical_failures > 0:
        decision = "blocked_by_critical"
    elif warning_failures > 0:
        decision = "requires_warning_ack"

    return {
        "decision": decision,
        "critical_failures": critical_failures,
        "warning_failures": warning_failures,
        "checks": checks,
    }


def build_artifact(metrics: dict[str, float], evaluation: dict[str, Any], input_path: Path | None) -> dict[str, Any]:
    ordered_metric_keys = [
        "crash_free_session_rate_percent",
        "sustained_fps_floor",
        "fps_60_compliance_percent",
        "initial_scene_load_seconds",
        "safe_save_pass_rate_percent",
    ]
    artifact = {
        "schema": SCHEMA,
        "collected_by": "scripts/collect_readiness_metrics.py",
        "input_source": str(input_path) if input_path else "embedded_default_fixture",
        "metrics": {k: metrics[k] for k in ordered_metric_keys},
        "supplemental_metrics": {"frame_time_p95_ms": metrics["frame_time_p95_ms"]},
        "gate_evaluation": evaluation,
    }
    # Keep SteamReadinessPolicy compatibility by flattening required gate inputs at the JSON root.
    for key in ordered_metric_keys:
        artifact[key] = metrics[key]

    return artifact


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect local readiness metrics and evaluate acceptance gates.")
    parser.add_argument("--input", type=Path, default=None, help="Optional input fixture JSON path.")
    parser.add_argument("--output", type=Path, required=True, help="Output JSON artifact path.")
    args = parser.parse_args()

    payload = _read_input(args.input)
    metrics = collect_metrics(payload)
    evaluation = evaluate(metrics)
    artifact = build_artifact(metrics, evaluation, args.input)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(artifact, indent=2) + "\n", encoding="utf-8")

    print(f"Wrote readiness metrics artifact: {args.output}")
    print(f"Decision: {evaluation['decision']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
