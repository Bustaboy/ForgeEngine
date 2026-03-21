#!/usr/bin/env python3
"""GameForge V1 minimal local-first app entrypoint."""

from __future__ import annotations

import argparse
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]


def run() -> int:
    print("GameForge V1 bootstrap app")
    print("Mode: local-first, single-player, no-code-first")
    print("Target OS: Windows + Ubuntu")
    print("Rendering direction: Vulkan-first")
    print(f"Repository root: {REPO_ROOT}")

    module_paths = {
        "runtime (C++)": REPO_ROOT / "runtime" / "cpp",
        "editor (C#)": REPO_ROOT / "editor" / "csharp",
        "ai orchestration (Python)": REPO_ROOT / "ai-orchestration" / "python",
    }

    for module_name, module_path in module_paths.items():
        exists = "OK" if module_path.exists() else "MISSING"
        print(f"- {module_name}: {exists} ({module_path})")

    print("App started successfully.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Run minimal GameForge V1 app")
    parser.parse_args()
    return run()


if __name__ == "__main__":
    raise SystemExit(main())
