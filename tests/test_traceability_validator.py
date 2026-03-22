import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = REPO_ROOT / "scripts" / "validate_traceability.py"


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
