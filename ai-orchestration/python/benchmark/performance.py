"""Lightweight runtime performance snapshot helpers for background benchmarking."""

from __future__ import annotations

import json
import os
import statistics
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


IDLE_STATE_FILENAME = ".benchmark-idle-state.json"
PERFORMANCE_HISTORY_FILENAME = "performance_history.json"


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _coerce_float(value: object, fallback: float) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return fallback


def _coerce_int(value: object, fallback: int) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return fallback


def _safe_read_json(path: Path, default: Any) -> Any:
    if not path.exists():
        return default
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return default


def _detect_project_root(scene_path: Path) -> Path:
    resolved = scene_path.resolve()
    if resolved.is_dir():
        candidate_dirs = [resolved]
    else:
        candidate_dirs = [resolved.parent]

    scene_parent = resolved.parent if resolved.is_file() else resolved
    candidate_dirs.extend([scene_parent, *scene_parent.parents])

    for candidate in candidate_dirs:
        if (candidate / "prototype-manifest.json").exists():
            return candidate
        if (candidate / "scene").exists() and (candidate / "config").exists():
            return candidate

    if resolved.is_file() and resolved.parent.name == "scene":
        return resolved.parent.parent
    if resolved.name == "scene":
        return resolved.parent
    return scene_parent


def _extract_entity_count(scene_path: Path) -> int:
    payload = _safe_read_json(scene_path, default={})
    if not isinstance(payload, dict):
        return 0

    for key in ("entities", "npcs", "objects", "actors"):
        value = payload.get(key)
        if isinstance(value, list):
            return len(value)

    return _coerce_int(payload.get("entity_count"), 0)


def _read_target_profile(project_root: Path) -> str:
    models_config = _safe_read_json(project_root / "models.json", default={})
    if not isinstance(models_config, dict):
        return "unknown"
    onboarding = models_config.get("onboarding", {})
    if not isinstance(onboarding, dict):
        return "unknown"
    answers = onboarding.get("answers", {})
    if not isinstance(answers, dict):
        return "unknown"
    target = str(answers.get("target_profile", "")).strip().lower()
    return target or "unknown"


def run_light_benchmark(scene_path: Path) -> dict[str, object]:
    """Collect ultra-lightweight perf metrics with env overrides for runtime integrations."""

    samples: list[float] = []
    for _ in range(12):
        start = time.perf_counter()
        _ = sum(range(64))
        elapsed_ms = (time.perf_counter() - start) * 1000.0
        samples.append(max(elapsed_ms, 0.0001))

    avg_update_ms = statistics.fmean(samples)
    sorted_samples = sorted(samples)
    percentile_index = max(0, int(len(sorted_samples) * 0.99) - 1)
    p99_update_ms = sorted_samples[percentile_index]

    estimated_avg_fps = min(240.0, max(15.0, 1000.0 / max(avg_update_ms, 0.0001)))
    estimated_fps_1pct_low = min(estimated_avg_fps, max(10.0, 1000.0 / max(p99_update_ms, 0.0001)))

    entity_count = _extract_entity_count(scene_path)

    metrics = {
        "fps_avg": round(_coerce_float(os.environ.get("GAMEFORGE_METRIC_FPS_AVG"), estimated_avg_fps), 2),
        "fps_1pct_low": round(_coerce_float(os.environ.get("GAMEFORGE_METRIC_FPS_1PCT_LOW"), estimated_fps_1pct_low), 2),
        "vram_usage_mb": round(_coerce_float(os.environ.get("GAMEFORGE_METRIC_VRAM_MB"), 0.0), 2),
        "draw_calls": _coerce_int(os.environ.get("GAMEFORGE_METRIC_DRAW_CALLS"), 0),
        "entity_count": _coerce_int(os.environ.get("GAMEFORGE_METRIC_ENTITY_COUNT"), entity_count),
        "update_time_ms": round(_coerce_float(os.environ.get("GAMEFORGE_METRIC_UPDATE_MS"), max(avg_update_ms, 0.1)), 4),
        "sample_count": len(samples),
    }
    return metrics


def record_performance_snapshot(scene_path: Path, session_name: str) -> dict[str, object]:
    """Append a performance snapshot to project-local performance_history.json."""

    resolved_scene = scene_path.resolve()
    project_root = _detect_project_root(resolved_scene)
    history_path = project_root / PERFORMANCE_HISTORY_FILENAME

    history_payload = _safe_read_json(history_path, default={})
    if not isinstance(history_payload, dict):
        history_payload = {}

    snapshots = history_payload.get("snapshots")
    if not isinstance(snapshots, list):
        snapshots = []

    snapshot = {
        "timestamp_utc": _utc_now_iso(),
        "session_name": session_name,
        "scene_path": str(resolved_scene),
        "target_hardware_profile": _read_target_profile(project_root),
        "metrics": run_light_benchmark(resolved_scene),
    }

    snapshots.append(snapshot)
    history_payload.update(
        {
            "schema": "gameforge.performance_history.v1",
            "project_root": str(project_root),
            "snapshots": snapshots,
        }
    )

    history_path.write_text(json.dumps(history_payload, indent=2), encoding="utf-8")

    return {
        "recorded": True,
        "history_path": str(history_path),
        "snapshot": snapshot,
        "total_snapshots": len(snapshots),
    }


def should_run_idle_benchmark(scene_path: Path, interval_minutes: int = 10) -> bool:
    """Return True when enough time has elapsed since the last idle benchmark."""

    project_root = _detect_project_root(scene_path)
    state_path = project_root / IDLE_STATE_FILENAME
    state = _safe_read_json(state_path, default={})
    if not isinstance(state, dict):
        state = {}

    now = time.time()
    last_ts = _coerce_float(state.get("last_idle_benchmark_unix"), 0.0)
    due = (now - last_ts) >= max(60, interval_minutes * 60)

    if due:
        state["last_idle_benchmark_unix"] = now
        state_path.write_text(json.dumps(state, indent=2), encoding="utf-8")

    return due
