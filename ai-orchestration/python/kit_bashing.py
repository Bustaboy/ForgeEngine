#!/usr/bin/env python3
"""Data-driven 2D modular kit-bashing helpers for ForgeEngine V1."""

from __future__ import annotations

import hashlib
import json
import random
import re
from dataclasses import asdict, dataclass
from pathlib import Path

from art_bible import ArtBible, default_art_bible


@dataclass(frozen=True)
class KitModule:
    module_id: str
    type: str
    tags: list[str]
    compatible_with: list[str]
    art_bible_constraints: dict[str, object]
    default_asset_id: str
    default_size: dict[str, float]


@dataclass(frozen=True)
class KitBashInstance:
    module_id: str
    asset_id: str
    position: dict[str, float]
    size: dict[str, float]
    rotation_radians: float
    tint: dict[str, float]
    layer: int
    variation: dict[str, object]


def _load_kits_modules(kits_path: Path) -> list[KitModule]:
    payload = json.loads(kits_path.read_text(encoding="utf-8"))
    modules_payload = payload.get("modules", [])
    if not isinstance(modules_payload, list):
        raise ValueError("kits.json must contain a top-level 'modules' list")

    modules: list[KitModule] = []
    for item in modules_payload:
        if not isinstance(item, dict):
            continue
        module_id = str(item.get("module_id", "")).strip()
        if not module_id:
            continue
        modules.append(
            KitModule(
                module_id=module_id,
                type=str(item.get("type", "prop")).strip().lower(),
                tags=[str(tag).strip().lower() for tag in item.get("tags", []) if str(tag).strip()],
                compatible_with=[str(tag).strip().lower() for tag in item.get("compatible_with", []) if str(tag).strip()],
                art_bible_constraints=dict(item.get("art_bible_constraints", {})) if isinstance(item.get("art_bible_constraints"), dict) else {},
                default_asset_id=str(item.get("default_asset_id", module_id)).strip() or module_id,
                default_size={
                    "x": float(item.get("default_size", {}).get("x", 1.0)) if isinstance(item.get("default_size"), dict) else 1.0,
                    "y": float(item.get("default_size", {}).get("y", 1.0)) if isinstance(item.get("default_size"), dict) else 1.0,
                },
            )
        )
    return modules


def _scene_targets_from_prompt(prompt: str) -> set[str]:
    normalized = prompt.lower()
    targets: set[str] = set()
    if any(token in normalized for token in ["farmhouse", "farm", "barn", "rural"]):
        targets.update({"building", "roof", "foundation", "fence", "tree", "interior"})
    if any(token in normalized for token in ["interior", "cozy", "room", "inside"]):
        targets.update({"interior", "prop"})
    if any(token in normalized for token in ["forest", "nature", "grove", "autumn"]):
        targets.update({"tree", "nature", "ground"})
    if not targets:
        targets.update({"building", "prop", "tree"})
    return targets


def _seed_for_prompt(prompt: str, module_count: int) -> int:
    digest = hashlib.sha256(f"{prompt}|{module_count}".encode("utf-8")).digest()
    return int.from_bytes(digest[:8], byteorder="big", signed=False)


def bash_scene(prompt: str, art_bible: ArtBible, existing_modules: list[dict[str, object]] | None = None, kits_path: Path | None = None) -> dict[str, object]:
    clean_prompt = prompt.strip()
    if not clean_prompt:
        raise ValueError("prompt must be non-empty")

    existing_modules = existing_modules or []
    kits_path = kits_path or (Path.cwd() / "kits.json")
    modules = _load_kits_modules(kits_path)
    by_id = {module.module_id: module for module in modules}
    targets = _scene_targets_from_prompt(clean_prompt)

    selected = [
        module
        for module in modules
        if module.type in targets or any(tag in targets for tag in module.tags)
    ]
    if not selected:
        selected = modules[:6]

    rng = random.Random(_seed_for_prompt(clean_prompt, len(existing_modules)))
    palette_bias = 0.88 if any("muted" in token.lower() for token in art_bible.palette_keywords) else 1.0
    autumn_bias = 0.82 if re.search(r"\b(autumn|fall|melancholic|moody)\b", clean_prompt, flags=re.IGNORECASE) else 1.0

    placed_instances: list[KitBashInstance] = []
    missing_variant_requests: list[dict[str, object]] = []
    cursor_x = -6.0
    layer = 0

    for module in selected[:12]:
        place_count = 2 if module.type in {"fence", "tree", "prop"} else 1
        for _ in range(place_count):
            scale_jitter = 1.0 + rng.uniform(-0.08, 0.12)
            rotation = 0.0 if module.type in {"building", "foundation", "interior"} else rng.uniform(-0.08, 0.08)
            tint_value = max(0.65, min(1.15, palette_bias * autumn_bias + rng.uniform(-0.06, 0.05)))
            layer += 1
            instance = KitBashInstance(
                module_id=module.module_id,
                asset_id=module.default_asset_id,
                position={"x": round(cursor_x + rng.uniform(-0.35, 0.45), 3), "y": round(-1.0 + rng.uniform(-0.3, 0.2), 3)},
                size={"x": round(module.default_size["x"] * scale_jitter, 3), "y": round(module.default_size["y"] * scale_jitter, 3)},
                rotation_radians=round(rotation, 4),
                tint={"r": round(tint_value, 3), "g": round(tint_value * 0.98, 3), "b": round(tint_value * 0.94, 3), "a": 1.0},
                layer=layer,
                variation={
                    "scale_jitter": round(scale_jitter, 3),
                    "material_tweak": module.art_bible_constraints,
                },
            )
            cursor_x += module.default_size["x"] * 0.7
            placed_instances.append(instance)

            if module.type in {"roof", "tree", "interior"}:
                missing_variant_requests.append(
                    {
                        "module_id": module.module_id,
                        "asset_type": "sprite",
                        "prompt": art_bible.enhance_prompt(
                            f"{clean_prompt}: variant sprite for {module.module_id} with {module.art_bible_constraints.get('material_hint', 'matching style')}"
                        ),
                    }
                )

    referenced = [item.get("module_id") for item in existing_modules if isinstance(item, dict)]
    compatible_links = {
        module_id: by_id[module_id].compatible_with
        for module_id in sorted({str(module_id) for module_id in referenced if str(module_id) in by_id})
    }

    return {
        "schema": "gameforge.kit_bash_scene.v1",
        "prompt": clean_prompt,
        "targets": sorted(targets),
        "kits_path": str(kits_path),
        "module_instances": [asdict(item) for item in placed_instances],
        "missing_variant_requests": missing_variant_requests,
        "existing_compatibility": compatible_links,
    }


def apply_kit_bash_to_scene(scene_path: Path, prompt: str, art_bible_path: Path | None = None, kits_path: Path | None = None) -> dict[str, object]:
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    render_2d = scene_payload.get("render_2d")
    if not isinstance(render_2d, dict):
        render_2d = {"enabled": True, "sprites": [], "tilemaps": [], "entity_sprite_map": {}}
        scene_payload["render_2d"] = render_2d

    sprites = render_2d.get("sprites")
    if not isinstance(sprites, list):
        sprites = []
        render_2d["sprites"] = sprites

    if art_bible_path is not None and art_bible_path.exists():
        art_bible = ArtBible.from_json_file(art_bible_path)
    else:
        art_bible = default_art_bible(project_name=scene_path.parent.name or "GameForge Project")

    existing_modules = [
        {"module_id": str(item.get("module_id", ""))}
        for item in sprites
        if isinstance(item, dict) and item.get("module_id")
    ]
    result = bash_scene(prompt, art_bible, existing_modules=existing_modules, kits_path=kits_path)

    for instance in result["module_instances"]:
        sprite_node = {
            "asset_id": instance["asset_id"],
            "position": instance["position"],
            "size": instance["size"],
            "tint": instance["tint"],
            "rotation_radians": instance["rotation_radians"],
            "layer": instance["layer"],
            "entity_type": "kit_module",
            "module_id": instance["module_id"],
            "variation": instance["variation"],
        }
        sprites.append(sprite_node)

    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")

    result["scene_path"] = str(scene_path)
    result["sprites_added"] = len(result["module_instances"])
    return result
