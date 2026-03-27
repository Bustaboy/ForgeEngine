from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR_PATH = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"

spec = importlib.util.spec_from_file_location("orchestrator_for_orchestration_tests", ORCHESTRATOR_PATH)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
assert spec.loader is not None
spec.loader.exec_module(orchestrator)


def _write_scene(tmp_path: Path) -> Path:
    project_root = tmp_path / "sample-project"
    scene_dir = project_root / "scene"
    scene_dir.mkdir(parents=True)
    (project_root / "prototype-manifest.json").write_text("{}", encoding="utf-8")
    scene_path = scene_dir / "scene_scaffold.json"
    scene_path.write_text(
        json.dumps(
            {
                "npcs": [
                    {
                        "id": "npc_01",
                        "scripted_behavior": {"current_state": "patrol"},
                        "needs": {"hunger": 22, "energy": 48, "social": 55, "fun": 34},
                    },
                    {"id": "npc_02", "scripted_behavior": {"current_state": "rest"}},
                ],
                "entities": [{"id": "e_01"}],
                "recent_actions": ["built workshop"],
                "relationships": {"npc_01": {"trust": 61, "respect": 44, "grudge": 8, "loyalty": 52}},
                "free_will": {"enabled": True, "llm_enabled": False, "last_spark_line_by_npc": {"npc_01": "Shared supplies at dawn."}},
                "rag": {"enabled": True},
            }
        ),
        encoding="utf-8",
    )
    return scene_path


def test_orchestrate_narrative_checkpoint_returns_structured_result(tmp_path: Path) -> None:
    scene_path = _write_scene(tmp_path)
    result = orchestrator._build_orchestration_result(
        orchestration_type="narrative_checkpoint",
        scene_path=scene_path,
        target="major_story_beat",
    )

    assert result.source == "hybrid"
    assert result.orchestration_type == "narrative_checkpoint"
    assert result.target == "major_story_beat"
    assert result.suggested_scene_patch
    assert all(op["op"] in {"set", "add", "remove"} for op in result.suggested_scene_patch)


def test_orchestrate_npc_day_returns_hybrid_patch_with_npc_summary(tmp_path: Path) -> None:
    scene_path = _write_scene(tmp_path)
    result = orchestrator._build_orchestration_result(
        orchestration_type="npc_day",
        scene_path=scene_path,
        target="market_day",
    )

    assert result.source == "hybrid"
    assert result.orchestration_type == "npc_day"
    assert any(op["path"] == "/free_will/orchestration/day_plan_npc_count" for op in result.suggested_scene_patch)
    assert any(op["path"] == "/free_will/orchestration/day_plan_latest_summary" for op in result.suggested_scene_patch)


def test_orchestrate_scene_review_returns_safe_patch(tmp_path: Path) -> None:
    scene_path = _write_scene(tmp_path)
    result = orchestrator._build_orchestration_result(
        orchestration_type="scene_review",
        scene_path=scene_path,
        target=None,
    )

    assert result.source == "scripted"
    assert result.orchestration_type == "scene_review"
    assert all(str(op["path"]).startswith("/") for op in result.suggested_scene_patch)
