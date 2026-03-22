#!/usr/bin/env python3
"""Smoke test: generate a prototype from interview brief and launch it."""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"
SAMPLE_BRIEF = REPO_ROOT / "app" / "samples" / "interview-brief.sample.json"


def main() -> int:
    if not shutil.which("g++"):
        print("WARNING: g++ not found; prototype launch smoke test skipped.")
        return 2

    with tempfile.TemporaryDirectory() as temp_dir:
        output_dir = Path(temp_dir) / "generated"
        proc = subprocess.run(
            [
                sys.executable,
                str(ORCHESTRATOR),
                "--generate-prototype",
                str(SAMPLE_BRIEF),
                "--output",
                str(output_dir),
                "--launch",
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
        )

        print(proc.stdout, end="")
        if proc.returncode != 0:
            print(proc.stderr, end="")
            return proc.returncode

        required_messages = [
            "Generated prototype at:",
            "Scene scaffold loaded.",
            "RTS/sim template module loaded.",
            "RTS/sim scenario map loaded.",
            "RTS/sim balance config loaded.",
            "Player controller loaded.",
            "Basic UI loaded.",
            "Save/load hook loaded.",
            "Core loop check: units -> resources -> placement -> progression is intact.",
            "Prototype launch success.",
        ]

        for message in required_messages:
            if message not in proc.stdout:
                print(f"Missing expected output marker: {message}")
                return 3

    print("Prototype generation + launch smoke test passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
