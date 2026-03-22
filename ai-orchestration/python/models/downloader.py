"""Model download and checksum utilities."""

from __future__ import annotations

import hashlib
import urllib.request
from dataclasses import dataclass
from pathlib import Path

from .registry import ModelSpec


CHUNK_SIZE = 1024 * 1024


@dataclass(frozen=True)
class DownloadResult:
    model_id: str
    path: str
    downloaded: bool
    size_bytes: int


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(CHUNK_SIZE)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def ensure_model_file(spec: ModelSpec, models_root: Path) -> DownloadResult:
    """Download model if missing and validate checksum when provided."""

    models_root.mkdir(parents=True, exist_ok=True)
    target = models_root / spec.filename

    if not target.exists() or target.stat().st_size == 0:
        with urllib.request.urlopen(spec.source_url, timeout=120) as response:
            with target.open("wb") as output:
                while True:
                    block = response.read(CHUNK_SIZE)
                    if not block:
                        break
                    output.write(block)
        downloaded = True
    else:
        downloaded = False

    if spec.expected_sha256:
        actual = sha256_file(target)
        if actual.lower() != spec.expected_sha256.lower():
            target.unlink(missing_ok=True)
            raise ValueError(f"Checksum mismatch for {spec.model_id}: expected {spec.expected_sha256}, got {actual}")

    return DownloadResult(
        model_id=spec.model_id,
        path=str(target),
        downloaded=downloaded,
        size_bytes=target.stat().st_size,
    )
