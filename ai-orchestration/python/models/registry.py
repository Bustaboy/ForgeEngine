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
        model_id="llama-3.1-8b-q4-k-m",
        filename="Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        source_url="https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        expected_sha256="7b064f5842bf9532c91456deda288a1b672397a54fa729aa665952863033557c",
        approx_size_gb=4.92,
        min_vram_gb=8,
        role="primary_planning_code",
    ),
    ModelSpec(
        model_id="llm-q4-k",
        filename="mistral-7b-instruct-v0.2.Q4_K_M.gguf",
        source_url="https://huggingface.co/TheBloke/Mistral-7B-Instruct-v0.2-GGUF/resolve/main/mistral-7b-instruct-v0.2.Q4_K_M.gguf",
        expected_sha256="3e0039fd0273fcbebb49228943b17831aadd55cbcbf56f0af00499be2040ccf9",
        approx_size_gb=4.4,
        min_vram_gb=6,
        role="chat_planning",
    ),
    ModelSpec(
        model_id="code-q4-k",
        filename="deepseek-coder-6.7b-instruct.Q4_K_M.gguf",
        source_url="https://huggingface.co/TheBloke/deepseek-coder-6.7B-instruct-GGUF/resolve/main/deepseek-coder-6.7b-instruct.Q4_K_M.gguf",
        expected_sha256="92da6238854f2fa902d8b2ad79d548536af1d3ab06821f323bd5bbcea2013276",
        approx_size_gb=4.1,
        min_vram_gb=6,
        role="code_generation",
    ),
    ModelSpec(
        model_id="embed-small",
        filename="bge-base-en-v1.5-q8_0.gguf",
        source_url="https://huggingface.co/CompendiumLabs/bge-base-en-v1.5-gguf/resolve/main/bge-base-en-v1.5-q8_0.gguf",
        expected_sha256="ad1afe72cd6654a558667a3db10878b049a75bfd72912e1dabb91310d671173c",
        approx_size_gb=0.12,
        min_vram_gb=0,
        role="retrieval",
    ),
)
