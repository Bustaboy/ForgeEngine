import hashlib
import json
import shutil
from pathlib import Path

SAMPLE_PROJECT = Path("app/samples/generated-prototype/cozy-colony-tales")


def _json_files(root: Path) -> list[Path]:
    return sorted(path for path in root.rglob("*.json") if path.is_file())


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _snapshot_hashes(root: Path) -> dict[str, str]:
    return {str(path.relative_to(root)): _sha256(path) for path in _json_files(root)}


def _load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _write_json(path: Path, payload: dict) -> None:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def _assert_save_payload_integrity(payload: dict) -> None:
    assert payload["schema"] == "gameforge.save.v1"
    assert isinstance(payload["active_slot"], str) and payload["active_slot"]
    assert isinstance(payload["last_checkpoint"], str) and payload["last_checkpoint"]
    assert isinstance(payload["player_state"], dict)
    assert isinstance(payload["player_state"]["level"], int)
    assert isinstance(payload["player_state"]["xp"], int)


def test_at002_create_save_reopen_preserves_project_data(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    shutil.copytree(SAMPLE_PROJECT, project_root)

    baseline_hashes = _snapshot_hashes(project_root)

    save_path = project_root / "save" / "savegame_hook.json"
    save_payload = _load_json(save_path)
    _assert_save_payload_integrity(save_payload)

    save_payload["last_checkpoint"] = "baseline_scene:checkpoint_alpha"
    save_payload["player_state"]["xp"] = 42
    _write_json(save_path, save_payload)

    reopened_save_payload = _load_json(save_path)
    _assert_save_payload_integrity(reopened_save_payload)
    assert reopened_save_payload == save_payload

    reopened_hashes = _snapshot_hashes(project_root)
    changed_files = {
        relative_path
        for relative_path, digest in reopened_hashes.items()
        if baseline_hashes.get(relative_path) != digest
    }

    assert changed_files == {"save/savegame_hook.json"}


def test_at025_save_load_regression_loop_detects_corruption(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    shutil.copytree(SAMPLE_PROJECT, project_root)

    save_path = project_root / "save" / "savegame_hook.json"
    manifest_path = project_root / "prototype-manifest.json"

    baseline_manifest_hash = _sha256(manifest_path)
    expected_xp = 0

    for cycle in range(1, 31):
        payload = _load_json(save_path)
        _assert_save_payload_integrity(payload)

        expected_xp += cycle
        payload["last_checkpoint"] = f"baseline_scene:loop_{cycle:02d}"
        payload["player_state"]["xp"] = expected_xp
        _write_json(save_path, payload)

        reopened_payload = _load_json(save_path)
        _assert_save_payload_integrity(reopened_payload)

        assert reopened_payload["last_checkpoint"] == f"baseline_scene:loop_{cycle:02d}"
        assert reopened_payload["player_state"]["level"] == 1
        assert reopened_payload["player_state"]["xp"] == expected_xp
        assert reopened_payload == payload

    assert _sha256(manifest_path) == baseline_manifest_hash
