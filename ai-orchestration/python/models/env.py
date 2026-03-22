"""Tiny .env loader without third-party dependencies."""

from __future__ import annotations

import os
from pathlib import Path


TRUE_VALUES = {"1", "true", "yes", "on"}


def load_dotenv(dotenv_path: Path) -> None:
    """Load KEY=VALUE pairs into os.environ without overriding existing keys."""

    if not dotenv_path.exists():
        return

    for raw_line in dotenv_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        normalized_key = key.strip()
        normalized_value = value.strip().strip('"').strip("'")
        if normalized_key and normalized_key not in os.environ:
            os.environ[normalized_key] = normalized_value


def read_env_bool(key: str, default: bool) -> bool:
    value = os.getenv(key)
    if value is None:
        return default
    return value.strip().lower() in TRUE_VALUES


def read_env_int(key: str, default: int) -> int:
    value = os.getenv(key)
    if value is None:
        return default
    try:
        return int(value.strip())
    except ValueError:
        return default
