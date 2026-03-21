#!/usr/bin/env python3
"""Local bootstrap for GameForge V1 repository skeleton."""

from __future__ import annotations

import argparse
import platform
import shutil
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
REQUIRED_PATHS = [
    REPO_ROOT / "app",
    REPO_ROOT / "editor" / "csharp",
    REPO_ROOT / "runtime" / "cpp",
    REPO_ROOT / "ai-orchestration" / "python",
    REPO_ROOT / "docs",
    REPO_ROOT / "scripts",
]


def check_environment() -> None:
    print("== Environment ==")
    print(f"Python: {sys.version.split()[0]}")
    print(f"Platform: {platform.system()} {platform.release()}")

    python_ok = shutil.which("python3") or shutil.which("python")
    print(f"python available: {'yes' if python_ok else 'no'}")


def check_structure() -> bool:
    print("== Repository Structure ==")
    all_ok = True
    for path in REQUIRED_PATHS:
        exists = path.exists()
        print(f"{'OK' if exists else 'MISSING'} - {path.relative_to(REPO_ROOT)}")
        if not exists:
            all_ok = False
    return all_ok


def run_minimal_app() -> int:
    print("== Starting Minimal App ==")
    cmd = [sys.executable, str(REPO_ROOT / "app" / "main.py")]
    return subprocess.call(cmd)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Bootstrap GameForge V1 locally")
    parser.add_argument(
        "--skip-run",
        action="store_true",
        help="Only validate structure and environment, do not start app",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    print("GameForge V1 local bootstrap")
    check_environment()

    structure_ok = check_structure()
    if not structure_ok:
        print("Bootstrap failed: required folders are missing.")
        return 1

    if args.skip_run:
        print("Bootstrap validation passed (app run skipped).")
        return 0

    code = run_minimal_app()
    if code == 0:
        print("Bootstrap completed successfully.")
    else:
        print(f"Bootstrap failed while starting app (exit code {code}).")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
