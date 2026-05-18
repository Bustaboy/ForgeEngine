"""Safe partial regeneration of prototype files with optional lock protection."""

from __future__ import annotations

import re
from pathlib import Path

from orchestration.io_utils import _write_text
from orchestration.types import RegenerationConflict, RegenerationResult


def _normalize_lock_path(value: str) -> str:
    path = value.strip().replace("\\", "/").strip("/")
    return path


def _target_hits_lock(target_path: str, lock_path: str) -> bool:
    if not lock_path:
        return False
    normalized_target = _normalize_lock_path(target_path)
    normalized_lock = _normalize_lock_path(lock_path)
    return normalized_target == normalized_lock or normalized_target.startswith(f"{normalized_lock}/")


def _build_regeneration_conflict_prompt(conflicts: list[RegenerationConflict]) -> str:
    if not conflicts:
        return ""
    conflict_lines = [f"- {conflict.target_path} (locked by: {conflict.lock_path})" for conflict in conflicts]
    joined_lines = "\n".join(conflict_lines)
    return (
        "Regeneration requested changes in locked content.\n"
        "Review impacted targets:\n"
        f"{joined_lines}\n"
        "Confirm destructive regeneration to overwrite locked targets."
    )


def _resolve_safe_regeneration_destination(prototype_root: Path, target_path: str) -> Path:
    candidate_input = target_path.strip()
    if not candidate_input:
        raise ValueError("Regeneration target path cannot be blank.")

    normalized_input = candidate_input.replace("\\", "/")
    if re.match(r"^[a-zA-Z]:[\\/]", candidate_input):
        raise ValueError(f"Drive-qualified regeneration path is not allowed: {target_path}")
    if normalized_input.startswith("/") or normalized_input.startswith("//"):
        raise ValueError(f"Absolute regeneration path is not allowed: {target_path}")

    resolved_root = prototype_root.resolve()
    resolved_target = (resolved_root / Path(normalized_input)).resolve()
    try:
        resolved_target.relative_to(resolved_root)
    except ValueError as exc:
        raise ValueError(f"Regeneration target escapes prototype root: {target_path}") from exc
    return resolved_target


def apply_partial_regeneration(
    prototype_root: Path,
    updates: dict[str, str],
    locked_paths: list[str] | None = None,
    confirm_destructive: bool = False,
) -> RegenerationResult:
    locked_paths = locked_paths or []
    conflicts: list[RegenerationConflict] = []
    updated_files: list[str] = []
    skipped_locked_files: list[str] = []

    for target_path in sorted(updates.keys()):
        normalized_target = _normalize_lock_path(target_path)
        matching_lock = next((lock for lock in locked_paths if _target_hits_lock(normalized_target, lock)), None)
        if matching_lock and not confirm_destructive:
            skipped_locked_files.append(normalized_target)
            conflicts.append(
                RegenerationConflict(
                    target_path=normalized_target,
                    lock_path=_normalize_lock_path(matching_lock),
                    reason="locked-content-protection",
                )
            )
            continue

        destination = _resolve_safe_regeneration_destination(prototype_root, target_path)
        _write_text(destination, updates[target_path])
        updated_files.append(normalized_target)

    requires_confirmation = bool(conflicts)
    conflict_prompt = _build_regeneration_conflict_prompt(conflicts)
    return RegenerationResult(
        requires_confirmation=requires_confirmation,
        conflict_prompt=conflict_prompt,
        conflicts=conflicts,
        updated_files=updated_files,
        skipped_locked_files=skipped_locked_files,
    )
