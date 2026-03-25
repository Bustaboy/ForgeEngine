#!/usr/bin/env python3
"""Consistency helpers for graphics generation workflows."""

from __future__ import annotations

import hashlib
import os
from datetime import datetime, timezone

from art_bible import ArtBible


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _shared_seed(prompts: list[str], art_bible: ArtBible) -> int:
    digest = hashlib.sha256(f"{'|'.join(prompts)}|{art_bible.art_direction}".encode("utf-8")).digest()
    return int.from_bytes(digest[:8], byteorder="big", signed=False)


def _control_hooks_from_env() -> dict[str, object]:
    return {
        "ip_adapter": {
            "enabled": bool(os.environ.get("GAMEFORGE_COMFYUI_IP_ADAPTER_WORKFLOW", "").strip()),
            "workflow_path": os.environ.get("GAMEFORGE_COMFYUI_IP_ADAPTER_WORKFLOW", "").strip(),
            # Placeholder: wire IP-Adapter reference image node in ComfyUI workflow JSON.
        },
        "controlnet": {
            "enabled": bool(os.environ.get("GAMEFORGE_COMFYUI_CONTROLNET_WORKFLOW", "").strip()),
            "workflow_path": os.environ.get("GAMEFORGE_COMFYUI_CONTROLNET_WORKFLOW", "").strip(),
            # Placeholder: wire ControlNet conditioning input in ComfyUI workflow JSON.
        },
        "lora": {
            "enabled": bool(os.environ.get("GAMEFORGE_COMFYUI_LORA_STACK", "").strip()),
            "stack": [token.strip() for token in os.environ.get("GAMEFORGE_COMFYUI_LORA_STACK", "").split(",") if token.strip()],
            # Placeholder: append LoRA stack entries into checkpoint loader chain.
        },
    }


def consistency_score(payload: dict[str, object], art_bible: ArtBible | None = None) -> dict[str, object]:
    if art_bible is None:
        return {
            "score": 0,
            "components": {},
            "matched_keywords": [],
            "missing_keywords": [],
            "warnings": ["art_bible_missing"],
        }

    blob = " ".join(
        [
            str(payload.get("prompt", "")),
            str(payload.get("enhanced_prompt", "")),
            str(payload.get("world_style_guide", "")),
        ]
    ).lower()

    style_terms = [art_bible.art_direction, *art_bible.rendering_keywords]
    palette_terms = art_bible.palette_keywords
    composition_terms = art_bible.composition_guidance

    def _ratio(terms: list[str]) -> tuple[float, list[str]]:
        valid = [term for term in terms if term.strip()]
        if not valid:
            return (0.0, [])
        hits = [term for term in valid if term.lower() in blob]
        return (len(hits) / max(1, len(valid)), hits)

    style_ratio, style_hits = _ratio(style_terms)
    palette_ratio, palette_hits = _ratio(palette_terms)
    composition_ratio, composition_hits = _ratio(composition_terms)

    score = max(0.0, min(100.0, (style_ratio * 0.45 + palette_ratio * 0.3 + composition_ratio * 0.25) * 100.0))
    all_terms = [*style_terms, *palette_terms, *composition_terms]
    matched = {item for item in [*style_hits, *palette_hits, *composition_hits]}
    missing = [term for term in all_terms if term and term not in matched]
    warnings: list[str] = []
    if score < 55.0:
        warnings.append("style_drift_risk")

    return {
        "score": round(score, 2),
        "components": {
            "style_keyword_alignment": round(style_ratio * 100.0, 2),
            "palette_alignment": round(palette_ratio * 100.0, 2),
            "composition_alignment": round(composition_ratio * 100.0, 2),
        },
        "matched_keywords": sorted(matched),
        "missing_keywords": missing,
        "warnings": warnings,
    }


def batch_generate(prompts: list[str], art_bible: ArtBible, count: int = 4) -> dict[str, object]:
    clean_prompts = [prompt.strip() for prompt in prompts if prompt and prompt.strip()]
    if not clean_prompts:
        raise ValueError("prompts must include at least one non-empty prompt")
    safe_count = max(1, int(count))

    shared_seed = int(os.environ.get("GAMEFORGE_GRAPHICS_SHARED_SEED", "0")) or _shared_seed(clean_prompts, art_bible)
    hooks = _control_hooks_from_env()

    items: list[dict[str, object]] = []
    for prompt_index, prompt in enumerate(clean_prompts):
        enhanced = art_bible.enhance_prompt(prompt)
        variant_set: list[dict[str, object]] = []
        for variant_index in range(safe_count):
            variant_seed = (shared_seed + (prompt_index * 1009) + (variant_index * 131)) % 2_147_483_647
            score = consistency_score({"prompt": prompt, "enhanced_prompt": enhanced}, art_bible=art_bible)
            variant_set.append(
                {
                    "variant_index": variant_index,
                    "seed": variant_seed,
                    "prompt": prompt,
                    "enhanced_prompt": enhanced,
                    "consistency_score": score.get("score", 0),
                    "consistency_components": score.get("components", {}),
                    "hooks": hooks,
                    "control_signature": f"{shared_seed}:{prompt_index}:{variant_index}",
                    "generated_at_utc": _utc_now_iso(),
                }
            )
        items.append({"prompt": prompt, "variants": variant_set})

    return {
        "schema": "gameforge.graphics_batch_generation.v1",
        "count": safe_count,
        "shared_seed": shared_seed,
        "control_profile": {
            "mode": os.environ.get("GAMEFORGE_GRAPHICS_CONSISTENCY_MODE", "shared-seed-control"),
            "comfyui_endpoint": os.environ.get("GAMEFORGE_COMFYUI_ENDPOINT", "http://127.0.0.1:8188"),
        },
        "hooks": hooks,
        "generated_at_utc": _utc_now_iso(),
        "items": items,
    }
