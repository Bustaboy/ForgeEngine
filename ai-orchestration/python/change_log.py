"""Structured change-log helpers for LLM-aware generation backtracing."""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

CHANGE_LOG_FILENAME = "changes.log.json"
MAX_LOG_ENTRIES = 200
MAX_PROMPT_CHARS = 280
MAX_SUMMARY_CHARS = 420


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _safe_read_json(path: Path, default: Any) -> Any:
    if not path.exists():
        return default
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return default


def _truncate(text: str, limit: int) -> str:
    normalized = " ".join(str(text).split())
    if len(normalized) <= limit:
        return normalized
    return f"{normalized[: max(0, limit - 1)].rstrip()}…"


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


def _infer_performance_delta(project_root: Path) -> dict[str, float] | None:
    history_path = project_root / "performance_history.json"
    payload = _safe_read_json(history_path, default={})
    if not isinstance(payload, dict):
        return None
    snapshots = payload.get("snapshots")
    if not isinstance(snapshots, list) or len(snapshots) < 2:
        return None

    latest = snapshots[-1].get("metrics", {}) if isinstance(snapshots[-1], dict) else {}
    previous = snapshots[-2].get("metrics", {}) if isinstance(snapshots[-2], dict) else {}
    if not isinstance(latest, dict) or not isinstance(previous, dict):
        return None

    deltas: dict[str, float] = {}
    for key in ("fps_avg", "fps_1pct_low", "update_time_ms", "draw_calls", "entity_count"):
        if key not in latest or key not in previous:
            continue
        try:
            deltas[key] = round(float(latest[key]) - float(previous[key]), 4)
        except (TypeError, ValueError):
            continue
    return deltas or None


def append_change_log_entry(
    scene_path: Path,
    action: str,
    prompt: str,
    affected_systems: list[str],
    performance_snapshot: dict[str, object] | None = None,
) -> dict[str, object]:
    """Append a structured generation/edit log entry at the project root."""

    resolved_scene = scene_path.resolve()
    project_root = _detect_project_root(resolved_scene)
    log_path = project_root / CHANGE_LOG_FILENAME

    payload = _safe_read_json(log_path, default={})
    if not isinstance(payload, dict):
        payload = {}

    entries = payload.get("entries")
    if not isinstance(entries, list):
        entries = []

    prompt_summary = _truncate(prompt, MAX_PROMPT_CHARS)
    normalized_systems = sorted({item.strip().lower() for item in affected_systems if str(item).strip()})
    search_tokens = sorted({
        action.strip().lower(),
        *normalized_systems,
        *[token.lower() for token in prompt_summary.split()[:14]],
    })
    search_tokens = [token for token in search_tokens if token]

    performance_delta = None
    if isinstance(performance_snapshot, dict):
        performance_delta = performance_snapshot
    else:
        performance_delta = _infer_performance_delta(project_root)

    summary_text = _truncate(
        f"[{action}] systems={','.join(normalized_systems) or 'unspecified'} prompt='{prompt_summary}'",
        MAX_SUMMARY_CHARS,
    )

    entry = {
        "timestamp": _utc_now_iso(),
        "session_name": action,
        "action_type": action,
        "scene_path": str(resolved_scene),
        "affected_systems": normalized_systems,
        "prompt_summary": prompt_summary,
        "summary_text": summary_text,
        "search_tokens": search_tokens,
        "performance_delta": performance_delta,
    }

    entries.append(entry)
    if len(entries) > MAX_LOG_ENTRIES:
        entries = entries[-MAX_LOG_ENTRIES:]

    payload.update(
        {
            "schema": "gameforge.change_log.v1",
            "project_root": str(project_root),
            "entries": entries,
        }
    )
    log_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    return {
        "logged": True,
        "log_path": str(log_path),
        "entry": entry,
        "total_entries": len(entries),
    }


def get_recent_changes(scene_path: Path, limit: int = 20) -> list[dict[str, object]]:
    """Return compact, LLM-friendly summaries of recent change-log entries."""

    project_root = _detect_project_root(scene_path.resolve())
    log_path = project_root / CHANGE_LOG_FILENAME
    payload = _safe_read_json(log_path, default={})
    if not isinstance(payload, dict):
        return []

    entries = payload.get("entries")
    if not isinstance(entries, list) or not entries:
        return []

    safe_limit = max(1, min(int(limit), 100))
    selected = entries[-safe_limit:]

    recent_changes: list[dict[str, object]] = []
    for entry in reversed(selected):
        if not isinstance(entry, dict):
            continue
        recent_changes.append(
            {
                "timestamp": str(entry.get("timestamp", "")),
                "action_type": str(entry.get("action_type", "")),
                "affected_systems": list(entry.get("affected_systems", [])),
                "summary": str(entry.get("summary_text", "")).strip(),
                "performance_delta": entry.get("performance_delta"),
            }
        )
    return recent_changes
