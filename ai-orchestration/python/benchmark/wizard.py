"""First-run hardware benchmark wizard for local model recommendations."""

from __future__ import annotations

import json
import os
import importlib.util
from dataclasses import asdict, dataclass
from pathlib import Path

try:
    from models.gpu import detect_primary_gpu
    from models.manager import prepare_models_as_dict
    from models.registry import DEFAULT_MODEL_SET
except ModuleNotFoundError:
    _MODELS_ROOT = Path(__file__).resolve().parents[1] / "models"

    def _load_module(module_name: str, filename: str):
        spec = importlib.util.spec_from_file_location(module_name, _MODELS_ROOT / filename)
        module = importlib.util.module_from_spec(spec)
        assert spec is not None and spec.loader is not None
        spec.loader.exec_module(module)
        return module

    _gpu = _load_module("benchmark_models_gpu", "gpu.py")
    _manager = _load_module("benchmark_models_manager", "manager.py")
    _registry = _load_module("benchmark_models_registry", "registry.py")

    detect_primary_gpu = _gpu.detect_primary_gpu
    prepare_models_as_dict = _manager.prepare_models_as_dict
    DEFAULT_MODEL_SET = _registry.DEFAULT_MODEL_SET


@dataclass(frozen=True)
class HardwareSummary:
    gpu_name: str
    gpu_vram_gb: int
    cpu_cores: int


@dataclass(frozen=True)
class ModelRecommendation:
    model_id: str
    role: str
    min_vram_gb: int
    approx_size_gb: float
    recommended: bool
    reason: str


@dataclass(frozen=True)
class BenchmarkResult:
    benchmark_schema: str
    is_first_run: bool
    prepare_models_invoked: bool
    hardware: HardwareSummary
    recommendations: list[ModelRecommendation]
    prepared_models: dict[str, object] | None
    state_path: str


def _resolve_state_file(orchestrator_file: Path) -> Path:
    return orchestrator_file.parent / "models" / ".first-run-state.json"


def _resolve_legacy_state_file(orchestrator_file: Path) -> Path:
    return orchestrator_file.parent / "models" / ".benchmark-first-run.json"


def _build_recommendations(vram_gb: int) -> list[ModelRecommendation]:
    recommendations: list[ModelRecommendation] = []
    for spec in DEFAULT_MODEL_SET:
        recommended = vram_gb >= spec.min_vram_gb
        if recommended:
            reason = f"meets VRAM target ({vram_gb}GB >= {spec.min_vram_gb}GB)"
        elif vram_gb == 0:
            reason = "GPU not detected; recommend CPU-friendly fallback path"
        else:
            reason = f"below VRAM target ({vram_gb}GB < {spec.min_vram_gb}GB)"

        recommendations.append(
            ModelRecommendation(
                model_id=spec.model_id,
                role=spec.role,
                min_vram_gb=spec.min_vram_gb,
                approx_size_gb=spec.approx_size_gb,
                recommended=recommended,
                reason=reason,
            )
        )
    return recommendations


def run_benchmark(orchestrator_file: Path | None = None, auto_prepare_models: bool = True) -> BenchmarkResult:
    """Run hardware benchmark + first-run model preparation check for editor shell startup."""

    anchor = orchestrator_file or Path(__file__).resolve().parents[1] / "orchestrator.py"
    state_file = _resolve_state_file(anchor)
    legacy_state_file = _resolve_legacy_state_file(anchor)
    is_first_run = not state_file.exists() and not legacy_state_file.exists()

    gpu = detect_primary_gpu()
    vram_gb = gpu.total_vram_gb if gpu else 0
    recommendations = _build_recommendations(vram_gb)

    prepared_models: dict[str, object] | None = None
    prepare_invoked = False
    if is_first_run and auto_prepare_models:
        prepared_models = prepare_models_as_dict(orchestrator_file=anchor)
        prepare_invoked = True

    state_file.parent.mkdir(parents=True, exist_ok=True)
    state_file.write_text(
        json.dumps(
            {
                "benchmark_schema": "gameforge.benchmark.v1",
                "benchmark_completed": True,
                "models_prepared": prepare_invoked,
                "gpu_vram_gb": vram_gb,
                "prepare_models_invoked": prepare_invoked,
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    return BenchmarkResult(
        benchmark_schema="gameforge.benchmark.v1",
        is_first_run=is_first_run,
        prepare_models_invoked=prepare_invoked,
        hardware=HardwareSummary(
            gpu_name=gpu.name if gpu else "cpu-only",
            gpu_vram_gb=vram_gb,
            cpu_cores=max(1, os.cpu_count() or 1),
        ),
        recommendations=recommendations,
        prepared_models=prepared_models,
        state_path=str(state_file),
    )


def run_benchmark_as_dict(orchestrator_file: Path | None = None, auto_prepare_models: bool = True) -> dict[str, object]:
    return asdict(run_benchmark(orchestrator_file=orchestrator_file, auto_prepare_models=auto_prepare_models))
