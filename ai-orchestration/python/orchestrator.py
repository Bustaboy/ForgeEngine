#!/usr/bin/env python3
"""Soul Loom AI orchestration entrypoint and interview helpers.

The implementation is organized under :mod:`orchestration` (see :mod:`orchestration.core`).
This file stays at ``ai-orchestration/python/orchestrator.py`` so subprocess callers,
benchmarks, and importlib-based tests that load this path continue to work unchanged.
"""

from __future__ import annotations

import sys
from pathlib import Path

PYTHON_ROOT = Path(__file__).resolve().parent
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

import orchestration.core as _core

for _name in dir(_core):
    if _name.startswith("__"):
        continue
    globals()[_name] = getattr(_core, _name)

if __name__ == "__main__":
    raise SystemExit(main())
