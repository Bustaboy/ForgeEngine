"""GPU and VRAM detection helpers for model assignment."""

from __future__ import annotations

import os
import subprocess
from dataclasses import dataclass


@dataclass(frozen=True)
class GpuInfo:
    name: str
    total_vram_gb: int


@dataclass(frozen=True)
class RuntimeSplit:
    gpu_layers: int
    cpu_threads: int
    offload_enabled: bool


def detect_primary_gpu() -> GpuInfo | None:
    """Detect NVIDIA GPU via nvidia-smi and return total VRAM in GB."""

    override_vram = os.getenv("FORGEENGINE_GPU_VRAM_GB", "").strip()
    if override_vram:
        try:
            return GpuInfo(name="override", total_vram_gb=max(0, int(override_vram)))
        except ValueError:
            pass

    try:
        proc = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=name,memory.total",
                "--format=csv,noheader,nounits",
            ],
            text=True,
            capture_output=True,
            check=False,
        )
    except FileNotFoundError:
        return None

    if proc.returncode != 0 or not proc.stdout.strip():
        return None

    first_line = proc.stdout.splitlines()[0]
    parts = [item.strip() for item in first_line.split(",")]
    if len(parts) != 2:
        return None

    try:
        vram_mb = int(parts[1])
    except ValueError:
        return None

    return GpuInfo(name=parts[0], total_vram_gb=max(0, round(vram_mb / 1024)))


def pick_runtime_split(total_vram_gb: int) -> RuntimeSplit:
    """Choose conservative llama.cpp style split by available VRAM."""

    cpu_threads = max(2, os.cpu_count() or 4)
    if total_vram_gb >= 16:
        return RuntimeSplit(gpu_layers=50, cpu_threads=cpu_threads, offload_enabled=True)
    if total_vram_gb >= 12:
        return RuntimeSplit(gpu_layers=36, cpu_threads=cpu_threads, offload_enabled=True)
    if total_vram_gb >= 8:
        return RuntimeSplit(gpu_layers=24, cpu_threads=cpu_threads, offload_enabled=True)
    if total_vram_gb >= 6:
        return RuntimeSplit(gpu_layers=12, cpu_threads=cpu_threads, offload_enabled=True)
    return RuntimeSplit(gpu_layers=0, cpu_threads=cpu_threads, offload_enabled=False)
