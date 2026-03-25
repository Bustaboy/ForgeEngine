from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PERF_PATH = REPO_ROOT / "ai-orchestration" / "python" / "benchmark" / "performance.py"

spec = importlib.util.spec_from_file_location("benchmark_performance", PERF_PATH)
benchmark_performance = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = benchmark_performance
assert spec.loader is not None
spec.loader.exec_module(benchmark_performance)


def test_record_performance_snapshot_creates_history(tmp_path: Path) -> None:
    project_root = tmp_path / "sample-project"
    scene_dir = project_root / "scene"
    scene_dir.mkdir(parents=True)
    (project_root / "prototype-manifest.json").write_text("{}", encoding="utf-8")
    scene_path = scene_dir / "scene_scaffold.json"
    scene_path.write_text(json.dumps({"entities": [{"id": "a"}, {"id": "b"}]}), encoding="utf-8")

    result = benchmark_performance.record_performance_snapshot(scene_path=scene_path, session_name="manual")

    history_path = project_root / "performance_history.json"
    assert result["recorded"] is True
    assert history_path.exists()

    payload = json.loads(history_path.read_text(encoding="utf-8"))
    assert payload["schema"] == "gameforge.performance_history.v1"
    assert len(payload["snapshots"]) == 1
    assert payload["snapshots"][0]["metrics"]["entity_count"] == 2
