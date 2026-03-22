import subprocess
import importlib.util
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = REPO_ROOT / "scripts" / "validate_traceability.py"
SPEC = importlib.util.spec_from_file_location("validate_traceability", VALIDATOR)
assert SPEC is not None and SPEC.loader is not None
validate_traceability = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validate_traceability)


def test_traceability_validator_passes() -> None:
    result = subprocess.run(
        ["python3", str(VALIDATOR)],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0, result.stdout + result.stderr
    assert "Traceability validation PASSED" in result.stdout
    assert "31/31" in result.stdout


def test_markdown_parser_rejects_duplicate_at_ids() -> None:
    table = """
| AT ID | Priority | Status | Evidence strength | Release blocking | Owner | Target sprint/date | Last verified (UTC) |
|---|---|---|---|---|---|---|---|
| AT-001 | P0 | covered | strong-automated | true | Team A | RC freeze | 2026-03-22T00:00:00Z |
| AT-001 | P0 | partial | partial-automated | true | Team B | RC freeze | 2026-03-22T00:00:00Z |
""".strip()

    with pytest.raises(ValueError, match="Duplicate AT ID in markdown table: AT-001"):
        validate_traceability._parse_md_table(table)
