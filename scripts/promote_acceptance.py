#!/usr/bin/env python3
"""Promote a single AT row in acceptance traceability JSON + markdown."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.json"
MD_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.md"
_TRACEABILITY_UTC_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


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
    MD_PATH.write_text(re.sub(pattern, row, text, count=1, flags=re.MULTILINE), encoding="utf-8")


def promote(
    at_id: str,
    status: str,
    evidence_strength: str,
    automation: list[str],
    *,
    next_action: str | None,
    manual_procedure: str | None,
    verified_at: str | None,
    append_automation: bool,
) -> None:
    payload = json.loads(JSON_PATH.read_text(encoding="utf-8"))
    item = next(row for row in payload["items"] if row.get("id") == at_id)

    merged = list(item.get("automation", [])) if append_automation else []
    for ref in automation:
        if ref not in merged:
            merged.append(ref)
    if not append_automation:
        merged = sorted(automation)

    item["automation"] = sorted(merged)
    item["status"] = status
    item["evidence_strength"] = evidence_strength
    item["next_action"] = next_action or "—"
    item["manual_procedure"] = manual_procedure
    verified = verified_at or _utc_now()
    if not _TRACEABILITY_UTC_RE.fullmatch(verified):
        raise ValueError("last_verified_at_utc must match YYYY-MM-DDTHH:MM:SSZ")
    item["last_verified_at_utc"] = verified

    JSON_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    _replace_md_row(at_id, item)

    result = subprocess.run(
        [sys.executable, str(REPO_ROOT / "scripts" / "validate_traceability.py")],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stdout + result.stderr)
    print(f"Promoted {at_id} to {status} ({evidence_strength}).")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--at", required=True, dest="at_id")
    parser.add_argument("--status", default="covered")
    parser.add_argument("--evidence-strength", default="strong-automated")
    parser.add_argument("--automation", action="append", default=[])
    parser.add_argument("--archive", action="append", default=[], help="Evidence path to add to automation[]")
    parser.add_argument("--next-action")
    parser.add_argument("--manual-procedure")
    parser.add_argument("--verified-at")
    parser.add_argument("--append-automation", action="store_true")
    args = parser.parse_args()

    automation = list(args.automation)
    for archive in args.archive:
        rel = Path(archive)
        if rel.is_absolute():
            try:
                rel = rel.relative_to(REPO_ROOT)
            except ValueError:
                rel = Path(archive)
        automation.append(rel.as_posix())

    promote(
        args.at_id,
        args.status,
        args.evidence_strength,
        automation,
        next_action=args.next_action,
        manual_procedure=args.manual_procedure,
        verified_at=args.verified_at,
        append_automation=args.append_automation,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
