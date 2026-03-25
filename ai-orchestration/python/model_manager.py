"""Local-first Hugging Face model download and registry for ForgeEngine."""

from __future__ import annotations

import json
import os
import re
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

try:
    from huggingface_hub import snapshot_download
    from huggingface_hub.errors import HfHubHTTPError
except ImportError:  # pragma: no cover - optional dependency at runtime
    snapshot_download = None

    class HfHubHTTPError(Exception):
        """Fallback error type when huggingface_hub is unavailable."""

DEFAULT_MODELS_JSON = "models.json"
DEFAULT_CACHE_ROOT = Path.home() / ".cache" / "forgeengine" / "models"
DEFAULT_QUANTIZATION = "Q4_K_M"

FRIENDLY_MODEL_REPOS: dict[str, str] = {
    "freewill": "bartowski/Llama-3.2-3B-Instruct-GGUF",
    "orchestrator": "bartowski/Qwen2.5-Coder-7B-Instruct-GGUF",
    "assetgen": "stabilityai/stable-diffusion-2-1-base",
}


@dataclass(frozen=True)
class ManagedModelRecord:
    friendly_name: str
    repo_id: str
    quantization: str
    path: str
    updated_at_unix: int


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
    return payload


def _save_models_config(config: dict[str, Any], models_json_path: Path | None = None) -> Path:
    path = _models_json_path(models_json_path)
    path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    return path


def _normalize_friendly_name(name: str) -> str:
    return str(name or "").strip().lower()


def _get_repo_id(friendly_name: str, repo_id: str | None = None) -> str:
    if repo_id:
        return repo_id
    normalized = _normalize_friendly_name(friendly_name)
    if normalized in FRIENDLY_MODEL_REPOS:
        return FRIENDLY_MODEL_REPOS[normalized]
    raise ValueError(
        f"Unknown friendly_name '{friendly_name}'. Supported names: {', '.join(sorted(FRIENDLY_MODEL_REPOS))}."
    )


def _choose_token(token: str | None = None, config: dict[str, Any] | None = None) -> str | None:
    if token:
        return token
    env_token = os.getenv("HF_TOKEN", "").strip() or os.getenv("HUGGINGFACE_TOKEN", "").strip()
    if env_token:
        return env_token
    if config and isinstance(config.get("hf_token"), str) and config["hf_token"].strip():
        return config["hf_token"].strip()
    return None


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


def download_model(
    friendly_name: str = "freewill",
    repo_id: str | None = None,
    quantization: str = DEFAULT_QUANTIZATION,
    token: str | None = None,
    models_json_path: Path | None = None,
    cache_root: Path | None = None,
) -> dict[str, Any]:
    """Download (or resume/cached fetch) a model and persist path in models.json."""

    if snapshot_download is None:
        raise RuntimeError(
            "huggingface_hub is required for /download_model. Install with: pip install huggingface_hub"
        )

    normalized_name = _normalize_friendly_name(friendly_name)
    resolved_repo_id = _get_repo_id(normalized_name, repo_id=repo_id)
    config = _load_models_config(models_json_path)
    hf_token = _choose_token(token=token, config=config)

    download_cache_root = cache_root or DEFAULT_CACHE_ROOT
    allow_patterns = [f"*{quantization}*.gguf", "*.json", "tokenizer*", "*.model", "*.txt"]

    for attempt in range(0, 6):
        try:
            snapshot_dir = snapshot_download(
                repo_id=resolved_repo_id,
                cache_dir=str(download_cache_root),
                resume_download=True,
                token=hf_token,
                local_files_only=False,
                allow_patterns=allow_patterns,
            )
            break
        except Exception as exc:  # noqa: BLE001
            delay = handle_rate_limit(exc, attempt=attempt)
            time.sleep(delay)
    else:
        raise RuntimeError("Model download failed after retries.")

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
    if hf_token:
        config["hf_token"] = hf_token

    config_path = _save_models_config(config, models_json_path=models_json_path)

    return {
        "friendly_name": record.friendly_name,
        "repo_id": record.repo_id,
        "quantization": record.quantization,
        "path": record.path,
        "models_json": str(config_path),
        "cache_root": str(download_cache_root),
        "used_token": bool(hf_token),
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
