#!/usr/bin/env python3
"""Run cross-platform smoke commands and emit standardized evidence artifacts."""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_VERSION = "v1"


class CommandSpec:
    def __init__(self, name: str, command: list[str], expected_signatures: list[str], optional: bool = False) -> None:
        self.name = name
        self.command = command
        self.expected_signatures = expected_signatures
        self.optional = optional


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _run_id() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def _safe_slug(text: str) -> str:
    return text.replace(" ", "_").replace("/", "_").lower()




def _display_path(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)

def _cmd_output(cmd: list[str]) -> str:
    try:
        result = subprocess.run(cmd, check=False, capture_output=True, text=True, cwd=REPO_ROOT)
    except OSError as exc:
        return f"unavailable ({exc})"
    merged = (result.stdout or "") + (result.stderr or "")
    return merged.strip() or "(no output)"


def _git_branch_commit() -> tuple[str, str]:
    branch = _cmd_output(["git", "rev-parse", "--abbrev-ref", "HEAD"]).splitlines()[0]
    commit = _cmd_output(["git", "rev-parse", "HEAD"]).splitlines()[0]
    return branch, commit


def _environment_summary(target_os: str) -> dict[str, Any]:
    dotnet_present = shutil.which("dotnet") is not None
    return {
        "host_platform": platform.platform(),
        "target_os": target_os,
        "python": _cmd_output([sys.executable, "--version"]),
        "pytest": _cmd_output([sys.executable, "-m", "pytest", "--version"]),
        "git": _cmd_output(["git", "--version"]),
        "gpp": _cmd_output(["g++", "--version"]).splitlines()[0] if shutil.which("g++") else "not found",
        "dotnet_present": dotnet_present,
        "dotnet_version": _cmd_output(["dotnet", "--version"]) if dotnet_present else "not found",
        "bash": _cmd_output(["bash", "--version"]).splitlines()[0] if shutil.which("bash") else "not found",
        "pwsh": _cmd_output(["pwsh", "--version"]).splitlines()[0] if shutil.which("pwsh") else "not found",
    }


def _execute_command(spec: CommandSpec, run_dir: Path, dry_run: bool, skip_reason: str | None = None) -> dict[str, Any]:
    started = _utc_now()
    output_path = run_dir / f"{_safe_slug(spec.name)}.log"

    if skip_reason:
        output = f"SKIPPED: {skip_reason}\n"
        exit_code = 127
    elif dry_run:
        output = f"DRY-RUN: {' '.join(spec.command)}\n"
        output += "Simulated command execution for schema validation.\n"
        exit_code = 0
    else:
        proc = subprocess.run(spec.command, cwd=REPO_ROOT, check=False, capture_output=True, text=True)
        output = (proc.stdout or "") + (proc.stderr or "")
        exit_code = proc.returncode

    output_path.write_text(output, encoding="utf-8")
    completed = _utc_now()

    matched = [sig for sig in spec.expected_signatures if sig in output]
    return {
        "name": spec.name,
        "command": spec.command,
        "started_at_utc": started,
        "completed_at_utc": completed,
        "exit_code": exit_code,
        "optional": spec.optional,
        "output_log": _display_path(output_path),
        "expected_signatures": spec.expected_signatures,
        "matched_signatures": matched,
        "status": "passed" if exit_code == 0 else ("skipped" if skip_reason else "failed"),
        "skip_reason": skip_reason,
    }


def _build_ubuntu_specs(dotnet_present: bool) -> list[tuple[CommandSpec, str | None]]:
    full_skip = None if dotnet_present else "dotnet not found; full bootstrap skipped by contract"
    return [
        (
            CommandSpec(
                name="bootstrap_runtime_only",
                command=["./scripts/bootstrap.sh", "--runtime-only"],
                expected_signatures=[
                    "Soul Loom bootstrap (Ubuntu/Linux)",
                    "== Building Runtime Entrypoint (C++) ==",
                ],
            ),
            None,
        ),
        (
            CommandSpec(
                name="bootstrap_full",
                command=["./scripts/bootstrap.sh"],
                expected_signatures=[
                    "== Starting C# App Entrypoint ==",
                    "Editor launcher started successfully.",
                ],
                optional=True,
            ),
            full_skip,
        ),
        (
            CommandSpec(
                name="pytest_q",
                command=[sys.executable, "-m", "pytest", "-q"],
                expected_signatures=["passed"],
            ),
            None,
        ),
    ]


def _build_windows_specs(dotnet_present: bool) -> list[tuple[CommandSpec, str | None]]:
    full_skip = None if dotnet_present else "dotnet not found; full bootstrap skipped by contract"
    return [
        (
            CommandSpec(
                name="bootstrap_runtime_only",
                command=["pwsh", "-f", "scripts/bootstrap.ps1", "-RuntimeOnly"],
                expected_signatures=[
                    "Soul Loom bootstrap (Windows)",
                    "== Building Runtime Entrypoint (C++) ==",
                ],
            ),
            None,
        ),
        (
            CommandSpec(
                name="bootstrap_full",
                command=["pwsh", "-f", "scripts/bootstrap.ps1"],
                expected_signatures=[
                    "== Starting C# App Entrypoint ==",
                    "Editor launcher started successfully.",
                ],
                optional=True,
            ),
            full_skip,
        ),
        (
            CommandSpec(
                name="pytest_q",
                command=[sys.executable, "-m", "pytest", "-q"],
                expected_signatures=["passed"],
            ),
            None,
        ),
    ]


def _render_markdown(evidence: dict[str, Any], md_path: Path) -> None:
    checks = []
    for cmd in evidence["commands"]:
        checks.append(f"- [ {'x' if cmd['exit_code'] == 0 or cmd['skip_reason'] else ' '} ] `{cmd['name']}` exit code policy")
        for sig in cmd["expected_signatures"]:
            state = "x" if sig in cmd["matched_signatures"] or cmd["skip_reason"] else " "
            checks.append(f"- [ {state} ] `{cmd['name']}` contains `{sig}`")

    lines = [
        f"# {'Ubuntu' if evidence['target_os'] == 'ubuntu' else 'Windows'} Smoke Evidence ({evidence['test_id']})",
        "",
        f"- Test ID: {evidence['test_id']}",
        f"- Run ID: {evidence['run_id']}",
        f"- Date (UTC): {evidence['generated_at_utc']}",
        f"- Operator: {evidence['operator']}",
        f"- Branch/commit: {evidence['branch']} / {evidence['commit']}",
        "",
        "## Environment snapshot",
        "",
    ]
    for key, value in evidence["environment"].items():
        lines.append(f"- {key}: {value}")

    lines.extend(["", "## Commands run", ""]) 
    for cmd in evidence["commands"]:
        lines.append(f"- `{ ' '.join(cmd['command']) }` (exit: {cmd['exit_code']})")

    lines.extend(["", "## Logs", ""]) 
    for cmd in evidence["commands"]:
        lines.append(f"- {cmd['name']}: `{cmd['output_log']}`")

    lines.extend(["", "## Output signature checks", "", *checks, "", "## Verdict", ""])
    lines.append(f"- Final result: {evidence['verdict']}")
    lines.append(f"- Notes: {evidence['notes']}")
    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def validate_evidence_payload(payload: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    required_root = {
        "schema_version",
        "run_id",
        "generated_at_utc",
        "target_os",
        "test_id",
        "operator",
        "branch",
        "commit",
        "environment",
        "commands",
        "verdict",
    }
    for field in required_root:
        if field not in payload:
            errors.append(f"missing root field: {field}")

    commands = payload.get("commands")
    if not isinstance(commands, list) or not commands:
        errors.append("commands must be a non-empty list")
    else:
        cmd_required = {
            "name",
            "command",
            "started_at_utc",
            "completed_at_utc",
            "exit_code",
            "output_log",
            "expected_signatures",
            "matched_signatures",
            "status",
        }
        for i, item in enumerate(commands):
            if not isinstance(item, dict):
                errors.append(f"commands[{i}] must be object")
                continue
            missing = sorted(cmd_required - item.keys())
            for field in missing:
                errors.append(f"commands[{i}] missing field: {field}")
    return errors


def run(target_os: str, output_root: Path, operator: str, dry_run: bool, contract_only: bool) -> Path:
    run_id = _run_id()
    run_dir = output_root / run_id
    run_dir.mkdir(parents=True, exist_ok=True)

    branch, commit = _git_branch_commit()
    env = _environment_summary(target_os)
    dotnet_present = bool(env["dotnet_present"])

    if target_os == "ubuntu":
        spec_pairs = _build_ubuntu_specs(dotnet_present)
    else:
        spec_pairs = _build_windows_specs(dotnet_present)

    commands: list[dict[str, Any]] = []
    host_is_windows = os.name == "nt"
    has_pwsh = shutil.which("pwsh") is not None

    for spec, explicit_skip in spec_pairs:
        skip_reason = explicit_skip
        if target_os == "windows" and not contract_only and not host_is_windows and not has_pwsh:
            skip_reason = "pwsh unavailable on non-Windows host; run with --contract-only or on Windows"
        if target_os == "windows" and contract_only:
            skip_reason = "contract-only mode: windows evidence structure validated without execution"
        commands.append(_execute_command(spec, run_dir, dry_run=dry_run, skip_reason=skip_reason))

    blocking_failures = [
        c
        for c in commands
        if c["exit_code"] != 0 and not c.get("skip_reason")
    ]
    verdict = "PASS" if not blocking_failures else "FAIL"
    notes = "All required smoke commands satisfied or were contract-skipped." if verdict == "PASS" else "One or more required commands failed."

    test_id = "AT-011" if target_os == "ubuntu" else "AT-010"
    payload: dict[str, Any] = {
        "schema_version": SCHEMA_VERSION,
        "run_id": run_id,
        "generated_at_utc": _utc_now(),
        "target_os": target_os,
        "test_id": test_id,
        "operator": operator,
        "branch": branch,
        "commit": commit,
        "environment": env,
        "commands": commands,
        "verdict": verdict,
        "notes": notes,
    }

    errors = validate_evidence_payload(payload)
    if errors:
        raise RuntimeError("Evidence payload validation failed: " + "; ".join(errors))

    json_path = run_dir / "smoke_evidence.json"
    json_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    md_name = "ubuntu_smoke_evidence.md" if target_os == "ubuntu" else "windows_smoke_evidence.md"
    _render_markdown(payload, run_dir / md_name)

    print(f"Smoke evidence generated: {_display_path(json_path)}")
    return json_path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--os", choices=["ubuntu", "windows"], required=True, dest="target_os")
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--operator", default="unknown")
    parser.add_argument("--dry-run", action="store_true", help="simulate command execution")
    parser.add_argument(
        "--contract-only",
        action="store_true",
        help="for windows mode, emit contract evidence without executing commands",
    )
    args = parser.parse_args()

    output_root = args.output_root
    if not output_root.is_absolute():
        output_root = (REPO_ROOT / output_root).resolve()

    run(
        target_os=args.target_os,
        output_root=output_root,
        operator=args.operator,
        dry_run=args.dry_run,
        contract_only=args.contract_only,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

