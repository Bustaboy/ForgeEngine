import importlib.util
import json
import sys
import tempfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
PYTHON_ROOT = REPO_ROOT / "ai-orchestration" / "python"
KIT_BASHING_PATH = PYTHON_ROOT / "kit_bashing.py"

sys.path.insert(0, str(PYTHON_ROOT))

spec = importlib.util.spec_from_file_location("kit_bashing_for_tests", KIT_BASHING_PATH)
kit_bashing = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kit_bashing
assert spec.loader is not None
spec.loader.exec_module(kit_bashing)

from art_bible import default_art_bible


def _art_bible():
    return default_art_bible(project_name="ForgeEngine Tests")


def test_bash_scene_farmhouse_creates_coherent_cluster() -> None:
    result = kit_bashing.bash_scene(
        "build a farmhouse with fence and tree",
        _art_bible(),
        kits_path=REPO_ROOT / "kits.json",
    )

    by_id = {
        instance["module_id"]: instance
        for instance in result["module_instances"]
        if instance["module_id"] in {"stone_foundation", "wooden_wall", "thatched_roof", "oak_tree_trunk"}
    }

    assert "farmhouse" in result["cluster_templates"]
    assert {"stone_foundation", "wooden_wall", "thatched_roof", "farm_fence", "oak_tree_trunk"}.issubset(
        {instance["module_id"] for instance in result["module_instances"]}
    )
    assert by_id["stone_foundation"]["position"]["y"] < by_id["wooden_wall"]["position"]["y"] < by_id["thatched_roof"]["position"]["y"]
    assert all(not instance["asset_id"].startswith("approved://") for instance in result["module_instances"])


def test_bash_scene_cozy_interior_places_shelf_on_interior_anchor() -> None:
    result = kit_bashing.bash_scene(
        "cozy interior with shelf",
        _art_bible(),
        kits_path=REPO_ROOT / "kits.json",
    )

    by_id = {
        instance["module_id"]: instance
        for instance in result["module_instances"]
        if instance["module_id"] in {"cozy_interior_wall", "interior_shelf"}
    }

    assert "cozy_interior" in result["cluster_templates"]
    assert {"cozy_interior_wall", "interior_shelf"} == set(by_id.keys())
    assert by_id["interior_shelf"]["position"]["x"] > by_id["cozy_interior_wall"]["position"]["x"]
    assert "interior_shelf:missing_interior_anchor" not in result["skipped_requests"]


def test_bash_scene_autumn_grove_places_ground_tree_and_fence_accent() -> None:
    result = kit_bashing.bash_scene(
        "autumn grove with tree and fence",
        _art_bible(),
        kits_path=REPO_ROOT / "kits.json",
    )

    module_ids = {instance["module_id"] for instance in result["module_instances"]}
    assert "grove" in result["cluster_templates"]
    assert {"ground_cover_leafy", "oak_tree_trunk", "farm_fence"}.issubset(module_ids)


def test_bash_scene_skips_roof_without_anchor() -> None:
    result = kit_bashing.bash_scene(
        "roof only",
        _art_bible(),
        kits_path=REPO_ROOT / "kits.json",
    )

    assert result["cluster_templates"] == []
    assert all(instance["module_id"] != "thatched_roof" for instance in result["module_instances"])
    assert "thatched_roof:missing_wall_anchor" in result["skipped_requests"]


def test_apply_kit_bash_to_scene_writes_plain_asset_ids_to_render_sprites() -> None:
    scene_payload = {
        "schema_version": 2,
        "entities": [
            {
                "id": 1,
                "transform": {
                    "pos": {"x": -5.0, "y": 0.0, "z": 0.0},
                    "rot": {"x": 0.0, "y": 0.0, "z": 0.0},
                    "scale": {"x": 1.0, "y": 1.0, "z": 1.0},
                },
                "renderable": {"color": {"x": 1.0, "y": 1.0, "z": 1.0, "w": 1.0}},
                "sprite_asset_id": "villager",
            }
        ],
        "render_2d": {
            "enabled": True,
            "render_mode": "2D",
            "sprites": [],
            "entity_sprite_map": {"villager": "villager"},
        },
    }

    temp_root = REPO_ROOT / "build" / "test-temp"
    run_root = temp_root / f"kit-bash-{next(tempfile._get_candidate_names())}"
    run_root.mkdir(parents=True, exist_ok=True)

    try:
        scene_path = run_root / "scene_scaffold.json"
        scene_path.write_text(json.dumps(scene_payload), encoding="utf-8")

        result = kit_bashing.apply_kit_bash_to_scene(
            scene_path,
            "build a farmhouse with fence and tree",
            kits_path=REPO_ROOT / "kits.json",
        )

        updated_scene = json.loads(scene_path.read_text(encoding="utf-8"))
        sprites = [sprite for sprite in updated_scene["render_2d"]["sprites"] if sprite.get("entity_type") == "kit_module"]
    finally:
        if run_root.exists():
            for path in sorted(run_root.rglob("*"), reverse=True):
                if path.is_file():
                    path.unlink()
                elif path.is_dir():
                    path.rmdir()
            run_root.rmdir()

    assert result["sprites_added"] >= 5
    assert sprites
    assert all(not str(sprite["asset_id"]).startswith("approved://") for sprite in sprites)
    assert any(sprite["module_id"] == "thatched_roof" for sprite in sprites)
