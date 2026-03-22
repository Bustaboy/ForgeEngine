#!/usr/bin/env python3
"""Validate acceptance traceability parity and completeness for AT-001..AT-031."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.json"
MD_PATH = REPO_ROOT / "docs" / "release" / "acceptance_traceability_v1.md"

EXPECTED_IDS = [f"AT-{i:03d}" for i in range(1, 32)]
ALLOWED_STATUS = {"covered", "partial", "missing"}
ALLOWED_STRENGTH = {"strong-automated", "partial-automated", "manual-only", "missing"}
REQUIRED_FIELDS = {
    "evidence_strength",
    "release_blocking",
    "owner",
    "target_sprint_or_date",
    "last_verified_at_utc",
}
TABLE_HEADERS = [
    "AT ID",
    "Priority",
    "Status",
    "Evidence strength",
    "Release blocking",
    "Owner",
    "Target sprint/date",
    "Last verified (UTC)",
]


def _parse_md_table(md_text: str) -> dict[str, dict[str, str]]:
    lines = [line.strip() for line in md_text.splitlines()]
    header_index = None
    for idx, line in enumerate(lines):
        if line.startswith("| ") and "| AT ID |" in f" {line} ":
            header_index = idx
            break
    if header_index is None:
        raise ValueError("Markdown table header not found.")

    headers = [part.strip() for part in lines[header_index].strip("|").split("|")]
    for required in TABLE_HEADERS:
        if required not in headers:
            raise ValueError(f"Markdown table missing required column: {required}")

    rows: dict[str, dict[str, str]] = {}
    for line in lines[header_index + 2 :]:
        if not line.startswith("|"):
            break
        parts = [part.strip() for part in line.strip("|").split("|")]
        if len(parts) != len(headers):
            raise ValueError(f"Malformed markdown row: {line}")
        row = dict(zip(headers, parts))
        at_id = row["AT ID"]
        if not re.fullmatch(r"AT-\d{3}", at_id):
            continue
        if at_id in rows:
            raise ValueError(f"Duplicate AT ID in markdown table: {at_id}")
        rows[at_id] = row
    return rows


def _require_nonempty_str(value: Any, field_name: str, at_id: str, errors: list[str]) -> None:
    if not isinstance(value, str) or not value.strip():
        errors.append(f"{at_id}: field '{field_name}' must be a non-empty string")


def validate() -> list[str]:
    errors: list[str] = []

    payload = json.loads(JSON_PATH.read_text(encoding="utf-8"))
    items = payload.get("items")
    if not isinstance(items, list):
        return ["JSON 'items' must be a list"]

    json_by_id: dict[str, dict[str, Any]] = {}
    for item in items:
        at_id = item.get("id")
        if not isinstance(at_id, str):
            errors.append(f"Invalid item id type: {at_id!r}")
            continue
        if at_id in json_by_id:
            errors.append(f"Duplicate AT ID in JSON: {at_id}")
            continue
        json_by_id[at_id] = item

    json_ids = set(json_by_id)
    expected_ids = set(EXPECTED_IDS)
    missing_in_json = sorted(expected_ids - json_ids)
    extra_in_json = sorted(json_ids - expected_ids)
    if missing_in_json:
        errors.append(f"Missing AT IDs in JSON: {', '.join(missing_in_json)}")
    if extra_in_json:
        errors.append(f"Unexpected AT IDs in JSON: {', '.join(extra_in_json)}")

    for at_id in EXPECTED_IDS:
        item = json_by_id.get(at_id)
        if item is None:
            continue

        status = item.get("status")
        if status not in ALLOWED_STATUS:
            errors.append(f"{at_id}: invalid status '{status}'")

        for field in REQUIRED_FIELDS:
            if field not in item:
                errors.append(f"{at_id}: missing required field '{field}'")

        strength = item.get("evidence_strength")
        if strength not in ALLOWED_STRENGTH:
            errors.append(f"{at_id}: invalid evidence_strength '{strength}'")

        if not isinstance(item.get("release_blocking"), bool):
            errors.append(f"{at_id}: release_blocking must be boolean")

        _require_nonempty_str(item.get("owner"), "owner", at_id, errors)
        _require_nonempty_str(item.get("target_sprint_or_date"), "target_sprint_or_date", at_id, errors)

        verified = item.get("last_verified_at_utc")
        if not isinstance(verified, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", verified):
            errors.append(f"{at_id}: last_verified_at_utc must match YYYY-MM-DDTHH:MM:SSZ")

    try:
        md_rows = _parse_md_table(MD_PATH.read_text(encoding="utf-8"))
    except ValueError as exc:
        errors.append(str(exc))
        return errors
    md_ids = set(md_rows)
    missing_in_md = sorted(expected_ids - md_ids)
    extra_in_md = sorted(md_ids - expected_ids)
    if missing_in_md:
        errors.append(f"Missing AT IDs in markdown table: {', '.join(missing_in_md)}")
    if extra_in_md:
        errors.append(f"Unexpected AT IDs in markdown table: {', '.join(extra_in_md)}")

    for at_id in EXPECTED_IDS:
        item = json_by_id.get(at_id)
        row = md_rows.get(at_id)
        if item is None or row is None:
            continue

        parity_pairs = [
            ("Status", str(item["status"])),
            ("Evidence strength", str(item["evidence_strength"])),
            ("Release blocking", "true" if item["release_blocking"] else "false"),
            ("Owner", str(item["owner"])),
            ("Target sprint/date", str(item["target_sprint_or_date"])),
            ("Last verified (UTC)", str(item["last_verified_at_utc"])),
        ]
        for col, expected in parity_pairs:
            actual = row[col]
            if actual != expected:
                errors.append(f"{at_id}: markdown/json mismatch for {col}: md='{actual}' json='{expected}'")

    return errors


def main() -> int:
    errors = validate()
    if errors:
        print("Traceability validation FAILED")
        for err in errors:
            print(f"- {err}")
        return 1

    print(f"Traceability validation PASSED: {len(EXPECTED_IDS)}/{len(EXPECTED_IDS)} AT IDs complete and in parity")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
