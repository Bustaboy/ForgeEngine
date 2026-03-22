import json
import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
COLLECTOR = REPO_ROOT / "scripts" / "collect_readiness_metrics.py"
DECISIONS_LOCK = REPO_ROOT / "GAMEFORGE_DECISIONS_LOCK.md"
SPEC = REPO_ROOT / "docs" / "release" / "READINESS_METRICS_SPEC.md"


def test_collector_output_schema_and_required_keys(tmp_path: Path) -> None:
    output_path = tmp_path / "readiness_metrics.json"

    subprocess.run(
        ["python3", str(COLLECTOR), "--output", str(output_path)],
        check=True,
        capture_output=True,
        text=True,
    )

    payload = json.loads(output_path.read_text(encoding="utf-8"))

    assert payload["schema"] == "gameforge.readiness_metrics.v1"
    assert payload["collected_by"] == "scripts/collect_readiness_metrics.py"
    assert payload["input_source"] == "embedded_default_fixture"

    metrics = payload["metrics"]
    assert set(metrics) == {
        "crash_free_session_rate_percent",
        "sustained_fps_floor",
        "fps_60_compliance_percent",
        "initial_scene_load_seconds",
        "safe_save_pass_rate_percent",
    }

    assert "frame_time_p95_ms" in payload["supplemental_metrics"]
    assert payload["gate_evaluation"]["decision"] in {"ready", "blocked_by_critical", "requires_warning_ack"}


def test_threshold_docs_align_with_decisions_lock() -> None:
    lock_text = DECISIONS_LOCK.read_text(encoding="utf-8")
    spec_text = SPEC.read_text(encoding="utf-8")

    required_lock_phrases = [
        "Crash-free session rate target: >= 97%.",
        "Critical fail: sustained below 30 FPS",
        "Frame-time target: p95 < 33ms",
        "Initial scene load target: < 20 seconds",
        "Save/load regression integrity: 100% pass.",
    ]
    for phrase in required_lock_phrases:
        assert phrase in lock_text

    required_spec_phrases = [
        "AT-020",
        "`crash_free_session_rate_percent` | `>= 97.0`",
        "`sustained_fps_floor` | `>= 30.0`",
        "`frame_time_p95_ms` | `< 33.0`",
        "`initial_scene_load_seconds` | `< 20.0`",
        "`safe_save_pass_rate_percent` | `>= 100.0`",
    ]
    for phrase in required_spec_phrases:
        assert phrase in spec_text
