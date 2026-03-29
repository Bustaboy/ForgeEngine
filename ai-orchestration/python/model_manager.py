"""Local-first Hugging Face model download and registry for Soul Loom."""

from __future__ import annotations

import json
import os
import re
import shutil
import socket
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError, URLError

PYTHON_ROOT = Path(__file__).resolve().parent
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from benchmark import run_benchmark_as_dict

try:
    from huggingface_hub import snapshot_download
    from huggingface_hub.errors import HfHubHTTPError
    from tqdm import tqdm as tqdm_base
except ImportError:  # pragma: no cover - optional dependency at runtime
    snapshot_download = None
    tqdm_base = None

    class HfHubHTTPError(Exception):
        """Fallback error type when huggingface_hub is unavailable."""

DEFAULT_MODELS_JSON = "models.json"
DEFAULT_CACHE_ROOT = Path.home() / ".cache" / "soul-loom" / "models"
DEFAULT_QUANTIZATION = "Q4_K_M"
TOKEN_ENV_KEYS = ("HF_TOKEN", "HUGGINGFACE_TOKEN")
TOKEN_SESSION_MESSAGE = "Token used from environment only for this session. Not saved for security."

FRIENDLY_NAME_ALIASES: dict[str, str] = {
    "coder": "coding",
    "free-will": "freewill",
    "freewill-spark": "freewill",
    "forge-guard": "forgeguard",
    "orchestrator": "coding",
}

FRIENDLY_MODEL_REPOS: dict[str, str] = {
    "freewill": "bartowski/Llama-3.2-3B-Instruct-GGUF",
    "coding": "bartowski/Qwen2.5-Coder-7B-Instruct-GGUF",
    "assetgen": "stabilityai/stable-diffusion-2-1-base",
    "forgeguard": "bartowski/Phi-3-mini-4k-instruct-GGUF",
}

ONBOARDING_KEEP_MESSAGE = (
    "ForgeGuard (tiny helper model) has been installed. "
    "It will be used later for code critique, guardrails, and optimization suggestions. "
    "You can remove it anytime from the Model Manager if you prefer."
)


@dataclass(frozen=True)
class ManagedModelRecord:
    friendly_name: str
    repo_id: str
    quantization: str
    path: str
    updated_at_unix: int


class DownloadCancelledError(RuntimeError):
    """Raised when a model download is cancelled by user request."""


class ManagedModelDownloadError(RuntimeError):
    """Raised when model download fails with a classified user-facing error."""

    def __init__(
        self,
        *,
        code: str,
        message: str,
        user_message: str,
        suggested_action: str,
        retryable: bool,
        retry_after_seconds: int | None = None,
        missing_disk_mb: int | None = None,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.user_message = user_message
        self.suggested_action = suggested_action
        self.retryable = retryable
        self.retry_after_seconds = retry_after_seconds
        self.missing_disk_mb = missing_disk_mb

    def to_payload(self) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "code": self.code,
            "message": str(self),
            "user_message": self.user_message,
            "suggested_action": self.suggested_action,
            "retryable": self.retryable,
        }
        if self.retry_after_seconds is not None:
            payload["retry_after_seconds"] = self.retry_after_seconds
        if self.missing_disk_mb is not None:
            payload["missing_disk_mb"] = self.missing_disk_mb
        return payload


def _safe_float(value: Any) -> float | None:
    try:
        if value is None:
            return None
        return float(value)
    except (TypeError, ValueError):
        return None


def _safe_int(value: Any) -> int | None:
    try:
        if value is None:
            return None
        return int(value)
    except (TypeError, ValueError):
        return None


def _create_snapshot_progress_tqdm(
    progress_callback: Callable[[dict[str, Any]], None] | None,
    cancel_check: Callable[[], bool] | None,
) -> type | None:
    if tqdm_base is None:
        return None

    class SnapshotProgressTqdm(tqdm_base):  # type: ignore[misc, valid-type]
        _last_emit_monotonic: float = 0.0

        def _emit(self) -> None:
            if progress_callback is None:
                return
            if cancel_check and cancel_check():
                raise DownloadCancelledError("Download cancelled by user.")

            now = time.monotonic()
            if now - self._last_emit_monotonic < 0.2:
                return
            self._last_emit_monotonic = now

            total = _safe_float(getattr(self, "total", None))
            downloaded = _safe_float(getattr(self, "n", None))
            progress = 0.0
            if total and total > 0 and downloaded is not None:
                progress = max(0.0, min(100.0, (downloaded / total) * 100.0))

            fmt = getattr(self, "format_dict", {}) or {}
            speed_bps = _safe_float(fmt.get("rate"))
            remaining_seconds = _safe_float(fmt.get("remaining"))
            current_file = str(getattr(self, "desc", "")).strip()
            if current_file.lower().startswith("fetching"):
                current_file = current_file.split(":", 1)[-1].strip()

            progress_callback(
                {
                    "event": "download_progress",
                    "progress_percent": progress,
                    "downloaded_mb": round((downloaded or 0.0) / (1024 * 1024), 2),
                    "total_mb": round((total or 0.0) / (1024 * 1024), 2) if total else None,
                    "speed_mbps": round((speed_bps or 0.0) / (1024 * 1024), 3) if speed_bps else None,
                    "eta_seconds": _safe_int(remaining_seconds),
                    "current_file": current_file or None,
                }
            )

        def update(self, n: int = 1) -> bool | None:  # type: ignore[override]
            result = super().update(n)
            self._emit()
            return result

        def refresh(self, *args: Any, **kwargs: Any) -> None:  # type: ignore[override]
            super().refresh(*args, **kwargs)
            self._emit()

        def close(self) -> None:  # type: ignore[override]
            self._emit()
            super().close()

    return SnapshotProgressTqdm


def _models_json_path(models_json_path: Path | None = None) -> Path:
    return models_json_path or (Path.cwd() / DEFAULT_MODELS_JSON)


def _load_models_config(models_json_path: Path | None = None) -> dict[str, Any]:
    path = _models_json_path(models_json_path)
    if not path.exists():
        return {"models": {}}
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        return {"models": {}}
    payload.setdefault("models", {})
    if not isinstance(payload["models"], dict):
        payload["models"] = {}
    return _canonicalize_models_config(payload)


def _save_models_config(config: dict[str, Any], models_json_path: Path | None = None) -> Path:
    path = _models_json_path(models_json_path)
    config.pop("hf_token", None)
    path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    return path


def _normalize_friendly_name(name: str) -> str:
    normalized = str(name or "").strip().lower()
    normalized = re.sub(r"\s+", "-", normalized)
    return FRIENDLY_NAME_ALIASES.get(normalized, normalized)


def _canonicalize_name_map(payload: dict[str, Any]) -> dict[str, Any]:
    canonicalized: dict[str, Any] = {}
    for key, value in payload.items():
        normalized = _normalize_friendly_name(key)
        if normalized == key:
            canonicalized[normalized] = value
    for key, value in payload.items():
        normalized = _normalize_friendly_name(key)
        canonicalized.setdefault(normalized, value)
    return canonicalized


def _canonicalize_models_config(config: dict[str, Any]) -> dict[str, Any]:
    models = config.get("models")
    if isinstance(models, dict):
        config["models"] = _canonicalize_name_map(models)

    onboarding = config.get("onboarding")
    if isinstance(onboarding, dict):
        recommendations = onboarding.get("recommendations")
        if isinstance(recommendations, dict):
            onboarding["recommendations"] = _canonicalize_name_map(recommendations)

    return config


def _get_repo_id(friendly_name: str, repo_id: str | None = None) -> str:
    if repo_id:
        return repo_id
    normalized = _normalize_friendly_name(friendly_name)
    if normalized in FRIENDLY_MODEL_REPOS:
        return FRIENDLY_MODEL_REPOS[normalized]
    raise ValueError(
        f"Unknown friendly_name '{friendly_name}'. Supported names: {', '.join(sorted(FRIENDLY_MODEL_REPOS))}."
    )


def _choose_token(token: str | None = None, config: dict[str, Any] | None = None) -> tuple[str | None, str]:
    if token:
        return token, "argument"
    env_token = ""
    for env_key in TOKEN_ENV_KEYS:
        env_token = os.getenv(env_key, "").strip()
        if env_token:
            break
    if env_token:
        return env_token, "environment"
    return None, "none"


def _find_quantized_file(snapshot_path: Path, quantization: str) -> Path | None:
    if not snapshot_path.exists():
        return None
    pattern = re.compile(rf"\b{re.escape(quantization)}\b", re.IGNORECASE)
    gguf_files = sorted(snapshot_path.rglob("*.gguf"))
    for gguf_file in gguf_files:
        if pattern.search(gguf_file.name):
            return gguf_file
    return gguf_files[0] if gguf_files else None


def handle_rate_limit(
    error: Exception,
    attempt: int,
    max_retries: int = 5,
    base_delay_seconds: int = 2,
) -> float:
    """Handle Hugging Face rate limits with exponential backoff.

    Returns wait time in seconds if retryable, otherwise raises.
    """

    if not isinstance(error, HfHubHTTPError):
        raise error

    status_code = getattr(error.response, "status_code", None)
    if status_code != 429:
        raise error

    if attempt >= max_retries:
        raise RuntimeError(
            "Hugging Face rate limit retries exceeded. "
            "Set HF_TOKEN for higher limits or try again later."
        ) from error

    retry_after_header = None
    if getattr(error, "response", None) is not None and getattr(error.response, "headers", None):
        retry_after_header = error.response.headers.get("Retry-After")

    if retry_after_header and retry_after_header.isdigit():
        delay = float(retry_after_header)
    else:
        delay = float(base_delay_seconds * (2 ** attempt))

    print(f"Hugging Face is rate-limiting downloads. Retrying in {int(delay)} seconds...")
    return delay


def _classify_download_error(error: Exception, cache_root: Path) -> ManagedModelDownloadError:
    text = str(error or "").strip()
    lowered = text.lower()
    response = getattr(error, "response", None)
    status_code = getattr(response, "status_code", None)
    headers = getattr(response, "headers", {}) or {}

    if isinstance(error, HfHubHTTPError):
        if status_code == 401:
            return ManagedModelDownloadError(
                code="invalid_token",
                message=text or "Unauthorized token while downloading model.",
                user_message="Your Hugging Face token is invalid or expired.",
                suggested_action="Update HF_TOKEN in Settings or environment, then retry.",
                retryable=False,
            )
        if status_code == 429:
            retry_after = headers.get("Retry-After")
            retry_after_seconds = int(retry_after) if isinstance(retry_after, str) and retry_after.isdigit() else 15
            return ManagedModelDownloadError(
                code="rate_limited",
                message=text or "Rate limited by Hugging Face.",
                user_message="Hugging Face is temporarily rate-limiting downloads.",
                suggested_action=f"Wait about {retry_after_seconds} seconds and retry.",
                retryable=True,
                retry_after_seconds=retry_after_seconds,
            )
        if status_code == 403:
            return ManagedModelDownloadError(
                code="access_denied",
                message=text or "Access denied while downloading model.",
                user_message="This model download is not allowed for your account.",
                suggested_action="Confirm model access on Hugging Face and verify token permissions.",
                retryable=False,
            )
        if status_code == 404:
            return ManagedModelDownloadError(
                code="not_found",
                message=text or "Model repository not found.",
                user_message="The selected model repository could not be found.",
                suggested_action="Refresh recommendations and try downloading again.",
                retryable=False,
            )
        if isinstance(status_code, int) and status_code >= 500:
            return ManagedModelDownloadError(
                code="http_temporary",
                message=text or f"Upstream HTTP error: {status_code}",
                user_message="The model server is temporarily unavailable.",
                suggested_action="Retry in a few seconds.",
                retryable=True,
                retry_after_seconds=8,
            )

    if isinstance(error, (TimeoutError, socket.timeout, URLError, ConnectionError)):
        return ManagedModelDownloadError(
            code="network_unavailable",
            message=text or "Network request failed during model download.",
            user_message="Your network connection appears unstable right now.",
            suggested_action="Check internet connection and retry.",
            retryable=True,
            retry_after_seconds=8,
        )

    if isinstance(error, HTTPError):
        if 500 <= int(error.code) <= 599:
            return ManagedModelDownloadError(
                code="http_temporary",
                message=text or f"HTTP {error.code} while downloading model.",
                user_message="The model server is temporarily unavailable.",
                suggested_action="Retry in a few seconds.",
                retryable=True,
                retry_after_seconds=8,
            )
        if error.code == 401:
            return ManagedModelDownloadError(
                code="invalid_token",
                message=text or "Unauthorized token while downloading model.",
                user_message="Your Hugging Face token is invalid or expired.",
                suggested_action="Update HF_TOKEN in Settings or environment, then retry.",
                retryable=False,
            )

    if isinstance(error, OSError):
        if error.errno == 28 or "no space left" in lowered or "disk full" in lowered:
            disk_probe = cache_root if cache_root.exists() else cache_root.parent
            usage = shutil.disk_usage(disk_probe)
            missing_mb = max(1, int((512 * 1024 * 1024 - usage.free) / (1024 * 1024)))
            return ManagedModelDownloadError(
                code="disk_full",
                message=text or "Not enough disk space for model download.",
                user_message="There is not enough disk space to finish this download.",
                suggested_action=f"Free at least {missing_mb} MB in your model cache drive, then retry.",
                retryable=False,
                missing_disk_mb=missing_mb,
            )

    if ("sha" in lowered and "mismatch" in lowered) or "checksum" in lowered or "corrupt" in lowered:
        return ManagedModelDownloadError(
            code="corrupted_cache",
            message=text or "Corrupted download cache detected.",
            user_message="A cached model file looks corrupted.",
            suggested_action="Remove the model cache folder for this model, then retry.",
            retryable=False,
        )

    return ManagedModelDownloadError(
        code="download_failed",
        message=text or "Unknown model download failure.",
        user_message="The model download could not be completed.",
        suggested_action="Retry. If this keeps happening, refresh model settings and check local disk/network access.",
        retryable=False,
    )


def download_model(
    friendly_name: str = "freewill",
    repo_id: str | None = None,
    quantization: str = DEFAULT_QUANTIZATION,
    token: str | None = None,
    models_json_path: Path | None = None,
    cache_root: Path | None = None,
    progress_callback: Callable[[dict[str, Any]], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
) -> dict[str, Any]:
    """Download (or resume/cached fetch) a model and persist path in models.json."""

    if snapshot_download is None:
        raise RuntimeError(
            "huggingface_hub is required for /download_model. Install with: pip install huggingface_hub"
        )

    normalized_name = _normalize_friendly_name(friendly_name)
    resolved_repo_id = _get_repo_id(normalized_name, repo_id=repo_id)
    config = _load_models_config(models_json_path)
    hf_token, token_source = _choose_token(token=token, config=config)

    download_cache_root = cache_root or DEFAULT_CACHE_ROOT
    allow_patterns = [f"*{quantization}*.gguf", "*.json", "tokenizer*", "*.model", "*.txt"]
    progress_tqdm = _create_snapshot_progress_tqdm(progress_callback, cancel_check)

    if progress_callback:
        progress_callback(
            {
                "event": "download_started",
                "friendly_name": normalized_name,
                "repo_id": resolved_repo_id,
                "quantization": quantization,
            }
        )
        if token_source == "environment":
            progress_callback(
                {
                    "event": "token_notice",
                    "friendly_name": normalized_name,
                    "message": TOKEN_SESSION_MESSAGE,
                }
            )

    max_attempts = 6
    for attempt in range(0, max_attempts):
        try:
            if cancel_check and cancel_check():
                raise DownloadCancelledError("Download cancelled by user.")
            snapshot_dir = snapshot_download(
                repo_id=resolved_repo_id,
                cache_dir=str(download_cache_root),
                resume_download=True,
                token=hf_token,
                local_files_only=False,
                allow_patterns=allow_patterns,
                tqdm_class=progress_tqdm,
            )
            break
        except DownloadCancelledError:
            raise
        except Exception as exc:  # noqa: BLE001
            classified = _classify_download_error(exc, cache_root=download_cache_root)
            retry_delay: float | None = None
            if classified.retryable:
                if classified.code == "rate_limited":
                    retry_delay = handle_rate_limit(exc, attempt=attempt, max_retries=max_attempts - 1)
                elif attempt < (max_attempts - 1):
                    retry_delay = float(classified.retry_after_seconds or (2 * (2**attempt)))
            if retry_delay is None:
                raise classified from exc
            if progress_callback:
                progress_callback(
                    {
                        "event": "retry_scheduled",
                        "friendly_name": normalized_name,
                        "attempt": attempt + 1,
                        "max_attempts": max_attempts,
                        "retry_in_seconds": int(retry_delay),
                        "error": classified.to_payload(),
                    }
                )
            time.sleep(retry_delay)
    else:
        raise ManagedModelDownloadError(
            code="retry_exhausted",
            message="Model download failed after retries.",
            user_message="We couldn't finish the download after multiple retry attempts.",
            suggested_action="Wait a minute, then retry. If it persists, check network stability and model token.",
            retryable=False,
        )

    snapshot_path = Path(snapshot_dir)
    selected_path = _find_quantized_file(snapshot_path, quantization) or snapshot_path

    record = ManagedModelRecord(
        friendly_name=normalized_name,
        repo_id=resolved_repo_id,
        quantization=quantization,
        path=str(selected_path),
        updated_at_unix=int(time.time()),
    )
    models = config.setdefault("models", {})
    models[normalized_name] = {
        "repo_id": record.repo_id,
        "quantization": record.quantization,
        "path": record.path,
        "updated_at_unix": record.updated_at_unix,
    }
    config_path = _save_models_config(config, models_json_path=models_json_path)

    if progress_callback:
        progress_callback(
            {
                "event": "download_completed",
                "friendly_name": record.friendly_name,
                "repo_id": record.repo_id,
                "progress_percent": 100.0,
                "path": record.path,
            }
        )

    return {
        "friendly_name": record.friendly_name,
        "repo_id": record.repo_id,
        "quantization": record.quantization,
        "path": record.path,
        "models_json": str(config_path),
        "cache_root": str(download_cache_root),
        "used_token": bool(hf_token),
        "token_message": TOKEN_SESSION_MESSAGE if token_source == "environment" else "",
        "token_source": token_source,
    }


def list_installed_models(models_json_path: Path | None = None) -> list[dict[str, Any]]:
    """List models from models.json with existence checks."""

    config = _load_models_config(models_json_path=models_json_path)
    models = config.get("models", {})
    result: list[dict[str, Any]] = []
    for friendly_name, payload in sorted(models.items()):
        path = str(payload.get("path", ""))
        exists = bool(path) and Path(path).exists()
        result.append(
            {
                "friendly_name": friendly_name,
                "repo_id": payload.get("repo_id", ""),
                "quantization": payload.get("quantization", ""),
                "path": path,
                "exists": exists,
                "updated_at_unix": payload.get("updated_at_unix"),
            }
        )
    return result


def get_model_path(friendly_name: str, models_json_path: Path | None = None) -> str | None:
    """Resolve configured model path, preserving manual-path compatibility."""

    normalized_name = _normalize_friendly_name(friendly_name)
    config = _load_models_config(models_json_path=models_json_path)
    payload = config.get("models", {}).get(normalized_name)
    if isinstance(payload, dict) and isinstance(payload.get("path"), str) and payload["path"].strip():
        return payload["path"].strip()

    env_var_name = f"FORGEENGINE_{normalized_name.upper()}_MODEL_PATH"
    env_path = os.getenv(env_var_name, "").strip()
    if env_path:
        return env_path
    return None


def _recommendation_size_band(vram_gb: int) -> str:
    if vram_gb >= 12:
        return "large"
    if vram_gb >= 8:
        return "medium"
    return "small"


def _extract_onboarding_recommendation(config: dict[str, Any], friendly_name: str) -> dict[str, Any]:
    onboarding = config.get("onboarding", {})
    if not isinstance(onboarding, dict):
        return {}
    recommendations = onboarding.get("recommendations", {})
    if not isinstance(recommendations, dict):
        return {}
    recommendation = recommendations.get(friendly_name, {})
    return recommendation if isinstance(recommendation, dict) else {}


def get_recommended_freewill_model(
    models_json_path: Path | None = None,
    orchestrator_file: Path | None = None,
    auto_prepare_models: bool = True,
) -> dict[str, Any]:
    """Return the best small/fit Free-Will model from onboarding + current hardware profile."""

    config = _load_models_config(models_json_path=models_json_path)
    recommendation = _extract_onboarding_recommendation(config, "freewill")
    if isinstance(recommendation.get("repo_id"), str) and recommendation["repo_id"].strip():
        return {
            "friendly_name": "freewill",
            "repo_id": recommendation["repo_id"].strip(),
            "estimated_size": recommendation.get("estimated_size", ""),
            "reason": recommendation.get("reason", "Recommended from onboarding profile."),
            "source": "onboarding",
        }

    benchmark = run_benchmark_as_dict(orchestrator_file=orchestrator_file, auto_prepare_models=auto_prepare_models)
    previous_answers = config.get("onboarding", {}).get("answers", {}) if isinstance(config.get("onboarding"), dict) else {}
    defaults = {
        "game_type": "hybrid",
        "npc_importance": "medium",
        "code_vs_asset": "balanced",
        "target_profile": "balanced",
    }
    answers = {key: str(previous_answers.get(key, default)).strip().lower() for key, default in defaults.items()}
    generated = generate_recommendations(hardware=benchmark.get("hardware", {}), answers=answers).get("freewill", {})
    return {
        "friendly_name": "freewill",
        "repo_id": str(generated.get("repo_id", FRIENDLY_MODEL_REPOS["freewill"])).strip(),
        "estimated_size": generated.get("estimated_size", ""),
        "reason": generated.get("reason", "Recommended from hardware profile."),
        "source": "hardware",
    }


def _resolve_scene_file(scene_path: Path | None = None) -> Path | None:
    if scene_path:
        return scene_path
    candidates = [
        Path.cwd() / "scene" / "scene_scaffold.json",
        Path.cwd() / "scene_scaffold.json",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def _write_freewill_path_to_scene(scene_path: Path, model_path: str) -> bool:
    if not scene_path.exists():
        return False
    payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        return False
    free_will = payload.get("free_will", {})
    if not isinstance(free_will, dict):
        free_will = {}
    free_will["model_path"] = model_path
    free_will["llm_enabled"] = True
    payload["free_will"] = free_will
    scene_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return True


def ensure_freewill_model(
    models_json_path: Path | None = None,
    scene_path: Path | None = None,
    quantization: str = DEFAULT_QUANTIZATION,
    orchestrator_file: Path | None = None,
    auto_prepare_models: bool = True,
) -> dict[str, Any]:
    """Ensure recommended Free-Will model is available and persist Scene.free_will.model_path."""

    recommendation = get_recommended_freewill_model(
        models_json_path=models_json_path,
        orchestrator_file=orchestrator_file,
        auto_prepare_models=auto_prepare_models,
    )
    repo_id = str(recommendation.get("repo_id", "")).strip()
    if not repo_id:
        raise RuntimeError("Unable to determine recommended Free-Will model repo_id.")

    current_path = get_model_path("freewill", models_json_path=models_json_path)
    if current_path and Path(current_path).exists():
        record = {
            "friendly_name": "freewill",
            "repo_id": repo_id,
            "quantization": quantization,
            "path": current_path,
            "downloaded": False,
        }
    else:
        record = download_model(
            friendly_name="freewill",
            repo_id=repo_id,
            quantization=quantization,
            models_json_path=models_json_path,
        )
        record["downloaded"] = True

    resolved_scene = _resolve_scene_file(scene_path)
    scene_updated = False
    if resolved_scene is not None:
        scene_updated = _write_freewill_path_to_scene(resolved_scene, record["path"])

    return {
        "recommended": recommendation,
        "freewill": record,
        "scene_path": str(resolved_scene) if resolved_scene else None,
        "scene_updated": scene_updated,
    }


def remove_model(friendly_name: str, models_json_path: Path | None = None) -> dict[str, Any]:
    """Remove a managed model entry from models.json."""

    normalized_name = _normalize_friendly_name(friendly_name)
    config = _load_models_config(models_json_path=models_json_path)
    models = config.setdefault("models", {})
    removed = models.pop(normalized_name, None)
    config_path = _save_models_config(config, models_json_path=models_json_path)
    return {
        "friendly_name": normalized_name,
        "removed": removed is not None,
        "models_json": str(config_path),
    }


def generate_recommendations(
    hardware: dict[str, Any],
    answers: dict[str, str],
) -> dict[str, dict[str, Any]]:
    """Generate onboarding recommendations for Free-Will, Coding, and Asset-Gen."""

    vram_gb = int(hardware.get("gpu_vram_gb", 0) or 0)
    size_band = _recommendation_size_band(vram_gb)
    game_type = answers.get("game_type", "hybrid")
    npc_importance = answers.get("npc_importance", "medium")
    code_vs_asset = answers.get("code_vs_asset", "balanced")
    target = answers.get("target_profile", "balanced")

    if size_band == "large":
        freewill_repo = "bartowski/Llama-3.2-8B-Instruct-GGUF"
        coding_repo = "bartowski/Qwen2.5-Coder-7B-Instruct-GGUF"
        freewill_size = "4.8GB (Q4)"
        coding_size = "4.5GB (Q4)"
    elif size_band == "medium":
        freewill_repo = "bartowski/Llama-3.2-3B-Instruct-GGUF"
        coding_repo = "bartowski/DeepSeek-Coder-V2-Lite-Instruct-GGUF"
        freewill_size = "2.0GB (Q4)"
        coding_size = "2.4GB (Q4)"
    else:
        freewill_repo = "bartowski/Qwen2.5-1.5B-Instruct-GGUF"
        coding_repo = "bartowski/Qwen2.5-Coder-1.5B-Instruct-GGUF"
        freewill_size = "1.1GB (Q4)"
        coding_size = "1.2GB (Q4)"

    assetgen_repo = "stabilityai/stable-diffusion-2-1-base"
    assetgen_size = "5.2GB fp16"
    if code_vs_asset == "asset-heavy":
        assetgen_reason = "You prioritized asset generation, so a stronger local diffusion baseline is recommended."
    else:
        assetgen_reason = "Balanced asset generation baseline suitable for V1 local-first workflows."

    return {
        "freewill": {
            "repo_id": freewill_repo,
            "estimated_size": freewill_size,
            "reason": (
                f"Selected for {game_type} gameplay with {npc_importance} NPC focus "
                f"on a {target} profile with ~{vram_gb}GB VRAM."
            ),
        },
        "coding": {
            "repo_id": coding_repo,
            "estimated_size": coding_size,
            "reason": (
                f"Matched to {code_vs_asset} production emphasis and {size_band} hardware tier "
                f"(~{vram_gb}GB VRAM)."
            ),
        },
        "assetgen": {
            "repo_id": assetgen_repo,
            "estimated_size": assetgen_size,
            "reason": assetgen_reason,
        },
        "forgeguard": {
            "repo_id": FRIENDLY_MODEL_REPOS["forgeguard"],
            "estimated_size": "2.2GB (Q4)",
            "reason": "Tiny helper model used for onboarding plus future guardrail and critique passes.",
            "kept_installed": True,
        },
    }


def run_onboarding(
    orchestrator_file: Path | None = None,
    models_json_path: Path | None = None,
    auto_prepare_models: bool = True,
    input_fn: Callable[[str], str] = input,
    progress_callback: Callable[[dict[str, Any]], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
    mark_completed: bool = True,
) -> dict[str, Any]:
    """Run first-run onboarding with benchmark + ForgeGuard Q&A + persisted recommendations."""

    benchmark = run_benchmark_as_dict(orchestrator_file=orchestrator_file, auto_prepare_models=auto_prepare_models)
    if progress_callback:
        progress_callback({"event": "onboarding_stage", "stage": "benchmark_complete"})
    config = _load_models_config(models_json_path=models_json_path)
    resolved_models_path = _models_json_path(models_json_path)

    print("ForgeGuard onboarding started. Answer 4 quick questions.")
    questions = [
        ("game_type", "Primary game type (rts/sim/rpg/hybrid): ", "hybrid"),
        ("npc_importance", "How important are NPC depth and dialogue? (low/medium/high): ", "medium"),
        ("code_vs_asset", "What do you need more right now? (code-heavy/asset-heavy/balanced): ", "balanced"),
        ("target_profile", "Target profile (quality/balanced/performance): ", "balanced"),
    ]
    answers: dict[str, str] = {}
    non_interactive = not sys.stdin.isatty()
    previous_answers = config.get("onboarding", {}).get("answers", {}) if isinstance(config.get("onboarding"), dict) else {}
    for key, prompt, default in questions:
        if non_interactive:
            prior = previous_answers.get(key, "") if isinstance(previous_answers, dict) else ""
            answers[key] = str(prior or default).strip().lower()
            continue
        value = str(input_fn(prompt) or "").strip().lower()
        answers[key] = value or default

    forgeguard_record = download_model(
        friendly_name="forgeguard",
        quantization=DEFAULT_QUANTIZATION,
        models_json_path=resolved_models_path,
        progress_callback=progress_callback,
        cancel_check=cancel_check,
    )
    config = _load_models_config(models_json_path=resolved_models_path)

    recommendations = generate_recommendations(hardware=benchmark.get("hardware", {}), answers=answers)
    completed_at = int(time.time()) if mark_completed else None
    config["onboarding"] = {
        "completed": mark_completed,
        "completed_at_unix": completed_at,
        "schema": "gameforge.onboarding.v1",
        "benchmark": benchmark,
        "answers": answers,
        "recommendations": recommendations,
        "forgeguard_keep_message": ONBOARDING_KEEP_MESSAGE,
    }
    _save_models_config(config, models_json_path=resolved_models_path)

    print(ONBOARDING_KEEP_MESSAGE)
    return {
        "benchmark": benchmark,
        "answers": answers,
        "recommendations": recommendations,
        "forgeguard": forgeguard_record,
        "message": ONBOARDING_KEEP_MESSAGE,
        "completed": mark_completed,
        "models_json": str(resolved_models_path),
    }


def run_quick_setup(
    orchestrator_file: Path | None = None,
    models_json_path: Path | None = None,
    auto_prepare_models: bool = True,
    input_fn: Callable[[str], str] = input,
    progress_callback: Callable[[dict[str, Any]], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
) -> dict[str, Any]:
    """Run first-launch setup for the guided prototype path: ForgeGuard + Free-Will + Coding."""

    onboarding_result = run_onboarding(
        orchestrator_file=orchestrator_file,
        models_json_path=models_json_path,
        auto_prepare_models=auto_prepare_models,
        input_fn=input_fn,
        progress_callback=progress_callback,
        cancel_check=cancel_check,
        mark_completed=False,
    )
    resolved_models_path = _models_json_path(models_json_path)
    freewill_recommendation = onboarding_result.get("recommendations", {}).get("freewill", {})
    freewill_repo = str(freewill_recommendation.get("repo_id", "")).strip() or FRIENDLY_MODEL_REPOS["freewill"]
    freewill_record = download_model(
        friendly_name="freewill",
        repo_id=freewill_repo,
        quantization=DEFAULT_QUANTIZATION,
        models_json_path=resolved_models_path,
        progress_callback=progress_callback,
        cancel_check=cancel_check,
    )
    coding_recommendation = onboarding_result.get("recommendations", {}).get("coding", {})
    coding_repo = str(coding_recommendation.get("repo_id", "")).strip() or FRIENDLY_MODEL_REPOS["coding"]
    coding_record = download_model(
        friendly_name="coding",
        repo_id=coding_repo,
        quantization=DEFAULT_QUANTIZATION,
        models_json_path=resolved_models_path,
        progress_callback=progress_callback,
        cancel_check=cancel_check,
    )

    config = _load_models_config(models_json_path=resolved_models_path)
    onboarding_payload = config.get("onboarding", {}) if isinstance(config.get("onboarding"), dict) else {}
    onboarding_payload["completed"] = True
    onboarding_payload["completed_at_unix"] = int(time.time())
    config["onboarding"] = onboarding_payload
    _save_models_config(config, models_json_path=resolved_models_path)

    summary = [
        "ForgeGuard installed for guardrails, critique, and lightweight decisions.",
        "Free-Will model installed for local NPC/dialog + gameplay reasoning.",
        "Coding model installed for local prototype/code generation.",
        "Next step: open New Project in the editor and click Generate & Play for your first prototype.",
        "Optional later: install Asset-Gen from Models & LLM if you also want local asset generation.",
    ]

    return {
        "onboarding": onboarding_result,
        "forgeguard": onboarding_result.get("forgeguard", {}),
        "freewill": freewill_record,
        "coding": coding_record,
        "summary": summary,
        "models_json": str(resolved_models_path),
        "completed": True,
    }
