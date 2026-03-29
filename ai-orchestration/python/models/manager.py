"""Local-first Soul Loom model manager for orchestrator integration."""

from __future__ import annotations

import os
from dataclasses import asdict, dataclass
from pathlib import Path

from .downloader import DownloadResult, ensure_model_file
from .env import load_dotenv, read_env_bool, read_env_int
from .gpu import detect_primary_gpu, pick_runtime_split
from .registry import DEFAULT_MODEL_SET, ModelSpec


@dataclass(frozen=True)
class ModelRuntimeConfig:
    model_id: str
    role: str
    path: str
    gpu_layers: int
    cpu_threads: int
    offload_enabled: bool


@dataclass(frozen=True)
class ModelPreparationResult:
    models_root: str
    gpu_name: str
    gpu_vram_gb: int
    downloads: list[DownloadResult]
    runtime_configs: list[ModelRuntimeConfig]


def _resolve_models_root(orchestrator_file: Path) -> Path:
    default_root = orchestrator_file.parent / "models" / "artifacts"
    configured = os.getenv("FORGEENGINE_MODELS_DIR", "").strip()
    if configured:
        configured_path = Path(configured)
        return configured_path if configured_path.is_absolute() else (orchestrator_file.parent / configured_path)
    return default_root


def _filter_models_by_vram(catalog: tuple[ModelSpec, ...], vram_gb: int, allow_cpu_fallback: bool) -> list[ModelSpec]:
    selected = [model for model in catalog if model.min_vram_gb <= vram_gb]
    if selected:
        return selected
    if allow_cpu_fallback and catalog:
        return [catalog[0]]
    return []


def prepare_models(orchestrator_file: Path | None = None) -> ModelPreparationResult:
    """Prepare local model files and runtime split on first run.

    Safe for subprocess calls from C# shell because output can be json-serialized.
    """

    anchor = orchestrator_file or Path(__file__).resolve().parents[1] / "orchestrator.py"
    load_dotenv(anchor.parent / ".env")
    models_root = _resolve_models_root(anchor)

    allow_cpu_fallback = read_env_bool("FORGEENGINE_ALLOW_CPU_FALLBACK", True)
    max_models = read_env_int("FORGEENGINE_MAX_MODELS", len(DEFAULT_MODEL_SET))

    gpu = detect_primary_gpu()
    total_vram = gpu.total_vram_gb if gpu else 0
    split = pick_runtime_split(total_vram)

    allowed = _filter_models_by_vram(DEFAULT_MODEL_SET, total_vram, allow_cpu_fallback)
    selected = allowed[: max(1, max_models)]

    downloads: list[DownloadResult] = []
    runtimes: list[ModelRuntimeConfig] = []
    for spec in selected:
        try:
            download = ensure_model_file(spec, models_root)
        except Exception as exc:
            raise RuntimeError(
                f"Failed to prepare model '{spec.model_id}' ({spec.filename}): {exc}"
            ) from exc
        downloads.append(download)
        runtimes.append(
            ModelRuntimeConfig(
                model_id=spec.model_id,
                role=spec.role,
                path=download.path,
                gpu_layers=split.gpu_layers,
                cpu_threads=split.cpu_threads,
                offload_enabled=split.offload_enabled,
            )
        )

    return ModelPreparationResult(
        models_root=str(models_root),
        gpu_name=gpu.name if gpu else "cpu-only",
        gpu_vram_gb=total_vram,
        downloads=downloads,
        runtime_configs=runtimes,
    )


def prepare_models_as_dict(orchestrator_file: Path | None = None) -> dict[str, object]:
    """JSON-friendly helper for orchestrator subprocess output."""

    result = prepare_models(orchestrator_file=orchestrator_file)
    payload = asdict(result)
    return payload

