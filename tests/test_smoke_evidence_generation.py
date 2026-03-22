import json
import subprocess
import sys
from pathlib import Path

import importlib.util

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "run_smoke_and_capture_evidence.py"
SPEC = importlib.util.spec_from_file_location("run_smoke_and_capture_evidence", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
smoke_evidence = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(smoke_evidence)


def _latest_run(root: Path) -> Path:
    runs = sorted([p for p in root.iterdir() if p.is_dir()])
    assert runs, f"no runs found in {root}"
    return runs[-1]


def test_ubuntu_dry_run_generates_required_schema(tmp_path: Path) -> None:
    output_root = tmp_path / "runs"
    result = subprocess.run(
        [
            sys.executable,
            str(SCRIPT_PATH),
            "--os",
            "ubuntu",
            "--output-root",
            str(output_root),
            "--dry-run",
            "--operator",
            "pytest",
        ],
        check=False,
        capture_output=True,
        text=True,
        cwd=REPO_ROOT,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    run_dir = _latest_run(output_root)
    evidence_path = run_dir / "smoke_evidence.json"
    assert evidence_path.exists()

    payload = json.loads(evidence_path.read_text(encoding="utf-8"))
    errors = smoke_evidence.validate_evidence_payload(payload)
    assert not errors, errors
    assert payload["target_os"] == "ubuntu"
    assert payload["test_id"] == "AT-011"


def test_windows_contract_only_mode_marks_skips(tmp_path: Path) -> None:
    output_root = tmp_path / "runs"
    result = subprocess.run(
        [
            sys.executable,
            str(SCRIPT_PATH),
            "--os",
            "windows",
            "--output-root",
            str(output_root),
            "--contract-only",
            "--operator",
            "pytest",
        ],
        check=False,
        capture_output=True,
        text=True,
        cwd=REPO_ROOT,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    run_dir = _latest_run(output_root)
    payload = json.loads((run_dir / "smoke_evidence.json").read_text(encoding="utf-8"))
    errors = smoke_evidence.validate_evidence_payload(payload)
    assert not errors, errors
    assert payload["target_os"] == "windows"
    assert payload["test_id"] == "AT-010"
    assert all(cmd.get("skip_reason") for cmd in payload["commands"])


def test_validator_rejects_missing_required_fields() -> None:
    payload = {
        "schema_version": "v1",
        "run_id": "20260322T000000Z",
        "target_os": "ubuntu",
        "commands": [],
    }
    errors = smoke_evidence.validate_evidence_payload(payload)
    assert errors
    assert any("missing root field: verdict" in e for e in errors)
