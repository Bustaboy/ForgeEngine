"""Resolved filesystem anchors for the ai-orchestration Python tree.

Used so logic modules do not rely on ``__file__`` depth (which changes when code
lives under ``orchestration/`` instead of ``orchestrator.py``).
"""

from __future__ import annotations

from pathlib import Path

_PKG_DIR = Path(__file__).resolve().parent
PYTHON_ROOT = _PKG_DIR.parent
AI_ORCHESTRATION_ROOT = PYTHON_ROOT.parent
REPO_ROOT = AI_ORCHESTRATION_ROOT.parent
ORCHESTRATOR_SCRIPT_PATH = PYTHON_ROOT / "orchestrator.py"
