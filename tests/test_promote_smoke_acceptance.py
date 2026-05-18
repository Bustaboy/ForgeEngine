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


def test_canonical_automation_refs_single_archive_bundle() -> None:
    refs = promote_mod._canonical_automation_refs("ubuntu", "20260518T044344Z")
    archive_hits = [r for r in refs if r.startswith("docs/release/evidence/archived/ubuntu/")]
    assert len(archive_hits) == 2
    assert all("20260518T044344Z" in r for r in archive_hits)
    assert not any("042622Z" in r or "043249Z" in r for r in refs)
    assert ".github/workflows/ubuntu-smoke-evidence.yml" not in refs
