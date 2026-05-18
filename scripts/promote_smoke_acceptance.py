#!/usr/bin/env python3
"""Promote AT-010/AT-011 traceability after a PASS archived smoke_evidence.json bundle."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.json"
MD_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.md"

AT_BY_OS = {"windows": "AT-010", "ubuntu": "AT-011"}


def _archive_dir(target_os: str, run_id: str) -> Path:
    return REPO_ROOT / "docs" / "release" / "evidence" / "archived" / target_os / run_id


def _automation_refs(target_os: str, run_id: str) -> list[str]:
    prefix = f"docs/release/evidence/archived/{target_os}/{run_id}"
    refs = [
        f"{prefix}/smoke_evidence.json",
        f"{prefix}/{'ubuntu' if target_os == 'ubuntu' else 'windows'}_smoke_evidence.md",
        "scripts/run_smoke_and_capture_evidence.py",
        ".github/workflows/ubuntu-smoke-evidence.yml" if target_os == "ubuntu" else ".github/workflows/pr-validation.yml",
    ]
    return refs


def _replace_md_row(at_id: str, item: dict[str, object]) -> None:
    automation = "; ".join(f"`{ref}`" for ref in item.get("automation", []))
    manual = item.get("manual_procedure")
    manual_cell = "n/a" if manual is None else str(manual)
    row = (
        f"| {at_id} | {item['priority']} | {item['status']} | {item['evidence_strength']} | "
        f"{'true' if item['release_blocking'] else 'false'} | {item['owner']} | "
        f"{item['target_sprint_or_date']} | {item['last_verified_at_utc']} | {automation} | "
        f"{manual_cell} | {item['next_action']} |"
    )
    text = MD_PATH.read_text(encoding="utf-8")
    pattern = rf"^\| {re.escape(at_id)} \|.*$"
    if not re.search(pattern, text, flags=re.MULTILINE):
        raise ValueError(f"Markdown row not found for {at_id}")
    updated = re.sub(pattern, row, text, count=1, flags=re.MULTILINE)
    MD_PATH.write_text(updated, encoding="utf-8")


def promote(target_os: str, run_id: str) -> None:
    at_id = AT_BY_OS[target_os]
    archive = _archive_dir(target_os, run_id)
    evidence_path = archive / "smoke_evidence.json"
    if not evidence_path.exists():
        raise FileNotFoundError(f"Missing archived evidence: {evidence_path}")

    evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
    if evidence.get("verdict") != "PASS":
        raise RuntimeError(f"{evidence_path}: verdict is not PASS")

    verified = str(evidence.get("generated_at_utc", "")).replace("Z", ":00Z")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", verified):
        verified = "2026-05-18T00:00:00Z"

    payload = json.loads(JSON_PATH.read_text(encoding="utf-8"))
    item = next(row for row in payload["items"] if row.get("id") == at_id)

    existing = set(item.get("automation", []))
    item["automation"] = sorted(existing | set(_automation_refs(target_os, run_id)))
    item["status"] = "covered"
    item["evidence_strength"] = "strong-automated"
    item["manual_procedure"] = None
    item["last_verified_at_utc"] = verified
    item["next_action"] = (
        f"Re-run {target_os} smoke when bootstrap or launcher paths change; refresh {archive.relative_to(REPO_ROOT).as_posix()}/."
    )

    JSON_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    _replace_md_row(at_id, item)
    print(f"Promoted {at_id} to covered (archive {target_os}/{run_id}).")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--os", choices=sorted(AT_BY_OS), required=True, dest="target_os")
    parser.add_argument("--run-id", required=True, help="UTC run folder name, e.g. 20260518T041200Z")
    args = parser.parse_args()
    promote(args.target_os, args.run_id)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
