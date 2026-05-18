import importlib.util
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
PROMOTE_SCRIPT = REPO_ROOT / "scripts" / "promote_smoke_acceptance.py"
SPEC = importlib.util.spec_from_file_location("promote_smoke_acceptance_script", PROMOTE_SCRIPT)
assert SPEC is not None and SPEC.loader is not None
promote_mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(promote_mod)


def test_last_verified_preserves_smoke_timestamp() -> None:
    evidence = {"generated_at_utc": "2026-05-18T04:12:22Z"}
    assert (
        promote_mod._last_verified_from_evidence(evidence)
        == "2026-05-18T04:12:22Z"
    )


def test_last_verified_rejects_broken_z_replacement_shape() -> None:
    with pytest.raises(ValueError, match="YYYY-MM-DDTHH:MM:SSZ"):
        promote_mod._last_verified_from_evidence(
            {"generated_at_utc": "2026-05-18T04:12:22:00Z"}
        )


def test_last_verified_rejects_empty() -> None:
    with pytest.raises(ValueError, match="YYYY-MM-DDTHH:MM:SSZ"):
        promote_mod._last_verified_from_evidence({})


def test_next_action_uses_directory_path_without_trailing_dot() -> None:
    archive = (
        REPO_ROOT
        / "docs/release/evidence/archived/ubuntu/20260518T044344Z"
    )
    text = promote_mod._next_action("ubuntu", archive)
    assert text.endswith("docs/release/evidence/archived/ubuntu/20260518T044344Z/")
    assert "/." not in text
    assert "//" not in text.split("refresh ", 1)[1]


def test_merge_automation_refs_accumulates_archive_bundles() -> None:
    existing = [
        "docs/release/evidence/archived/ubuntu/20260518T042622Z/smoke_evidence.json",
        "docs/release/evidence/archived/ubuntu/20260518T042622Z/ubuntu_smoke_evidence.md",
        ".github/workflows/ubuntu-smoke-evidence.yml",
    ]
    merged = promote_mod._merge_automation_refs(existing, "ubuntu", "20260518T044344Z")
    assert any("20260518T042622Z" in ref for ref in merged)
    assert any("20260518T044344Z" in ref for ref in merged)
    assert ".github/workflows/ubuntu-smoke-evidence.yml" not in merged
    assert "docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md#3-ubuntu-smoke-procedure-at-011" in merged


def test_archive_bundle_refs_for_single_run() -> None:
    refs = promote_mod._archive_bundle_refs("ubuntu", "20260518T044344Z")
    assert len(refs) == 2
    assert all("20260518T044344Z" in ref for ref in refs)
