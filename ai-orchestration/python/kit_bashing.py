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


def _prompt_variation_context(prompt: str) -> dict[str, str]:
    normalized = prompt.lower()
    season = "neutral"
    mood = "neutral"
    if re.search(r"\b(autumn|fall)\b", normalized):
        season = "autumn"
    elif re.search(r"\b(winter|snow)\b", normalized):
        season = "winter"
    elif re.search(r"\b(spring|bloom)\b", normalized):
        season = "spring"
    elif re.search(r"\b(summer|sunny)\b", normalized):
        season = "summer"

    if re.search(r"\b(melancholic|moody|somber|sad)\b", normalized):
        mood = "melancholic"
    elif re.search(r"\b(cheerful|bright|happy|vibrant)\b", normalized):
        mood = "cheerful"
    elif re.search(r"\b(mysterious|tense|ominous)\b", normalized):
        mood = "mysterious"
    return {"season": season, "mood": mood}


def _clamp_color(value: float) -> float:
    return round(max(0.0, min(1.2, value)), 3)


def apply_variations(
    instances: list[dict[str, object]],
    prompt: str,
    art_bible: ArtBible,
    seed: int,
) -> list[dict[str, object]]:
    rng = random.Random(seed)
    context = _prompt_variation_context(prompt)
    has_muted_palette = any("muted" in token.lower() for token in art_bible.palette_keywords)
    character_suffixes = ["", "_hat_a", "_hat_b", "_scarf", "_cape"]
    clothing_tints = [
        {"r": 1.05, "g": 0.98, "b": 0.94},
        {"r": 0.92, "g": 1.01, "b": 1.06},
        {"r": 1.04, "g": 1.02, "b": 0.92},
    ]
    varied: list[dict[str, object]] = []

    for instance in instances:
        sprite = dict(instance)
        size = dict(sprite.get("size", {})) if isinstance(sprite.get("size"), dict) else {"x": 1.0, "y": 1.0}
        tint = dict(sprite.get("tint", {})) if isinstance(sprite.get("tint"), dict) else {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
        variation = dict(sprite.get("variation", {})) if isinstance(sprite.get("variation"), dict) else {}
        material_tweak = variation.get("material_tweak", {})
        module_id = str(sprite.get("module_id", "")).lower()
        module_type = str(variation.get("module_type", "")).lower()
        module_tags = [str(tag).lower() for tag in variation.get("module_tags", [])] if isinstance(variation.get("module_tags"), list) else []

        scale_delta = rng.uniform(-0.06, 0.08)
        size["x"] = round(float(size.get("x", 1.0)) * (1.0 + scale_delta), 3)
        size["y"] = round(float(size.get("y", 1.0)) * (1.0 + scale_delta), 3)
        sprite["size"] = size

        rotation = float(sprite.get("rotation_radians", 0.0))
        micro_rotation = rng.uniform(-0.03, 0.03)
        sprite["rotation_radians"] = round(rotation + micro_rotation, 4)

        tint_shift = {"r": 1.0, "g": 1.0, "b": 1.0}
        if context["season"] == "autumn":
            tint_shift = {"r": 1.02, "g": 0.92, "b": 0.85}
        elif context["season"] == "winter":
            tint_shift = {"r": 0.92, "g": 0.95, "b": 1.06}
        elif context["season"] == "spring":
            tint_shift = {"r": 1.03, "g": 1.06, "b": 0.97}
        if context["mood"] == "melancholic":
            tint_shift["r"] *= 0.9
            tint_shift["b"] *= 1.05
        elif context["mood"] == "cheerful":
            tint_shift["r"] *= 1.05
            tint_shift["g"] *= 1.03
        if has_muted_palette:
            tint_shift["r"] *= 0.97
            tint_shift["g"] *= 0.97
            tint_shift["b"] *= 0.97

        if isinstance(material_tweak, dict):
            material_hint = str(material_tweak.get("material_hint", "")).lower()
            if "wood" in material_hint:
                tint_shift["r"] *= 1.03
                tint_shift["g"] *= 0.98
            if "metal" in material_hint:
                tint_shift["b"] *= 1.03
            if "cloth" in material_hint:
                tint_shift["g"] *= 1.02

        for channel in ("r", "g", "b"):
            tint[channel] = _clamp_color(float(tint.get(channel, 1.0)) * tint_shift[channel] + rng.uniform(-0.015, 0.015))
        tint["a"] = float(tint.get("a", 1.0))
        sprite["tint"] = tint

        is_character = module_type == "character" or "character" in module_tags or "npc" in module_tags or "character" in module_id
        asset_id = str(sprite.get("asset_id", ""))
        character_variation: dict[str, object] | None = None
        if is_character:
            suffix = rng.choice(character_suffixes)
            if suffix and not asset_id.endswith(suffix):
                asset_id = f"{asset_id}{suffix}"
            sprite["asset_id"] = asset_id
            body_x = round(1.0 + rng.uniform(-0.04, 0.05), 3)
            body_y = round(1.0 + rng.uniform(-0.05, 0.04), 3)
            size["x"] = round(size["x"] * body_x, 3)
            size["y"] = round(size["y"] * body_y, 3)
            clothing_tint = rng.choice(clothing_tints)
            tint["r"] = _clamp_color(tint["r"] * clothing_tint["r"])
            tint["g"] = _clamp_color(tint["g"] * clothing_tint["g"])
            tint["b"] = _clamp_color(tint["b"] * clothing_tint["b"])
            character_variation = {
                "accessory_suffix": suffix or "none",
                "body_proportion_jitter": {"x": body_x, "y": body_y},
                "clothing_tint_multiplier": clothing_tint,
            }

        variation["procedural_v1"] = {
            "seed": seed,
            "season": context["season"],
            "mood": context["mood"],
            "scale_delta": round(scale_delta, 4),
            "rotation_micro_adjustment": round(micro_rotation, 4),
            "tint_shift": {channel: round(tint_shift[channel], 4) for channel in ("r", "g", "b")},
            "character": character_variation,
            "material_tweak": material_tweak,
            "reason": "prompt_mood_season_and_art_bible_constraints",
        }
        sprite["variation"] = variation
        varied.append(sprite)
    return varied


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
                    "module_type": module.type,
                    "module_tags": module.tags,
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
    variation_seed = _seed_for_prompt(prompt, len(existing_modules))
    varied_instances = apply_variations(result["module_instances"], prompt, art_bible, variation_seed)

    for instance in varied_instances:
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
    result["variation_seed"] = variation_seed
    result["module_instances"] = varied_instances
    result["sprites_added"] = len(varied_instances)
    return result


def apply_variations_to_scene(scene_path: Path, prompt: str, art_bible_path: Path | None = None) -> dict[str, object]:
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    render_2d = scene_payload.get("render_2d")
    if not isinstance(render_2d, dict):
        raise ValueError("Scene payload must contain render_2d before applying variations")

    sprites = render_2d.get("sprites")
    if not isinstance(sprites, list):
        raise ValueError("render_2d.sprites must be a list")

    if art_bible_path is not None and art_bible_path.exists():
        art_bible = ArtBible.from_json_file(art_bible_path)
    else:
        art_bible = default_art_bible(project_name=scene_path.parent.name or "GameForge Project")

    existing_modules = [
        str(item.get("module_id", ""))
        for item in sprites
        if isinstance(item, dict) and str(item.get("module_id", "")).strip()
    ]
    variation_seed = _seed_for_prompt(prompt, len(existing_modules))

    updated = 0
    target_instances: list[dict[str, object]] = []
    target_indexes: list[int] = []
    for idx, sprite in enumerate(sprites):
        if not isinstance(sprite, dict):
            continue
        if str(sprite.get("entity_type", "")) != "kit_module":
            continue
        target_instances.append(sprite)
        target_indexes.append(idx)

    varied_instances = apply_variations(target_instances, prompt, art_bible, variation_seed)
    for idx, varied in zip(target_indexes, varied_instances):
        sprites[idx] = varied
        updated += 1

    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")
    return {
        "scene_path": str(scene_path),
        "prompt": prompt,
        "variation_seed": variation_seed,
        "sprites_updated": updated,
    }
