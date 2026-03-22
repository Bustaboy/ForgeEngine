"""Model registry definitions for local-first ForgeEngine orchestration."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ModelSpec:
    """Immutable model spec used for download and runtime allocation decisions."""

    model_id: str
    filename: str
    source_url: str
    expected_sha256: str
    approx_size_gb: float
    min_vram_gb: int
    role: str


DEFAULT_MODEL_SET: tuple[ModelSpec, ...] = (
    ModelSpec(
        model_id="llm-q4-k",
        filename="mistral-7b-instruct-v0.2.Q4_K_M.gguf",
        source_url="https://huggingface.co/TheBloke/Mistral-7B-Instruct-v0.2-GGUF/resolve/main/mistral-7b-instruct-v0.2.Q4_K_M.gguf",
        expected_sha256="",
        approx_size_gb=4.4,
        min_vram_gb=6,
        role="chat_planning",
    ),
    ModelSpec(
        model_id="code-q4-k",
        filename="deepseek-coder-6.7b-instruct.Q4_K_M.gguf",
        source_url="https://huggingface.co/TheBloke/deepseek-coder-6.7B-instruct-GGUF/resolve/main/deepseek-coder-6.7b-instruct.Q4_K_M.gguf",
        expected_sha256="",
        approx_size_gb=4.1,
        min_vram_gb=6,
        role="code_generation",
    ),
    ModelSpec(
        model_id="embed-small",
        filename="all-minilm-l6-v2.Q8_0.gguf",
        source_url="https://huggingface.co/CompendiumLabs/bge-base-en-v1.5-gguf/resolve/main/all-minilm-l6-v2.Q8_0.gguf",
        expected_sha256="",
        approx_size_gb=0.12,
        min_vram_gb=0,
        role="retrieval",
    ),
)
