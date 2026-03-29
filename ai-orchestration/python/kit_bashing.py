#!/usr/bin/env python3
"""Data-driven 2D modular kit-bashing helpers for Soul Loom."""

from __future__ import annotations

import hashlib
import json
import random
import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from art_bible import ArtBible, default_art_bible
from consistency import batch_generate, consistency_score


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


def _loot_seed(prompt: str, template_type: str, seed: int | None) -> int:
    if seed is not None:
        return int(seed)
    digest = hashlib.sha256(f"loot|{template_type}|{prompt}".encode("utf-8")).digest()
    return int.from_bytes(digest[:8], byteorder="big", signed=False)


def _load_item_templates(templates_path: Path | None = None) -> dict[str, list[dict[str, Any]]]:
    resolved = templates_path or (Path.cwd() / "items.json")
    payload = json.loads(resolved.read_text(encoding="utf-8"))
    templates = payload.get("templates")
    if not isinstance(templates, dict):
        raise ValueError("items.json must contain a top-level 'templates' object")
    loaded: dict[str, list[dict[str, Any]]] = {}
    for template_type, rows in templates.items():
        if not isinstance(rows, list):
            continue
        loaded[str(template_type).strip().lower()] = [row for row in rows if isinstance(row, dict)]
    return loaded


def _coerce_stats(template_stats: object) -> dict[str, float]:
    if not isinstance(template_stats, dict):
        return {}
    stats: dict[str, float] = {}
    for key, value in template_stats.items():
        if isinstance(value, (int, float)):
            stats[str(key)] = float(value)
    return stats


def generate_loot(
    prompt: str,
    art_bible: ArtBible,
    template_type: str = "weapon",
    count: int = 1,
    seed: int | None = None,
    templates_path: Path | None = None,
) -> dict[str, object]:
    clean_prompt = prompt.strip()
    if not clean_prompt:
        raise ValueError("prompt must be non-empty")
    safe_count = max(1, int(count))
    normalized_type = template_type.strip().lower()
    if not normalized_type:
        normalized_type = "weapon"

    templates_by_type = _load_item_templates(templates_path=templates_path)
    templates = templates_by_type.get(normalized_type)
    if not templates:
        supported = ", ".join(sorted(templates_by_type.keys()))
        raise ValueError(f"Unknown template_type '{normalized_type}'. Supported: {supported}")

    used_seed = _loot_seed(clean_prompt, normalized_type, seed)
    rng = random.Random(used_seed)
    context = _prompt_variation_context(clean_prompt)
    muted_palette = any("muted" in token.lower() for token in art_bible.palette_keywords)
    quality_suffixes = {
        "common": ["", "Worn", "Field"],
        "uncommon": ["Reliable", "Refined", "Crafted"],
        "rare": ["Masterwork", "Gilded", "Stormforged"],
        "epic": ["Legendary", "Mythic", "Dawnmarked"],
    }
    rarity_weights = [("common", 58), ("uncommon", 28), ("rare", 11), ("epic", 3)]
    season_material_shift = {
        "autumn": "oak",
        "winter": "steel",
        "spring": "woven",
        "summer": "sunbaked",
        "neutral": "tempered",
    }

    generated: list[dict[str, object]] = []
    for index in range(safe_count):
        template = dict(rng.choice(templates))
        base_name = str(template.get("name", f"{normalized_type.title()} {index + 1}")).strip()
        base_asset = str(template.get("asset_id", f"{normalized_type}_asset")).strip()
        icon_asset = str(template.get("icon_asset_id", f"{base_asset}_icon")).strip()
        base_value = int(template.get("base_value", 5))
        base_stats = _coerce_stats(template.get("stats"))
        candidate_rarities = [entry[0] for entry in rarity_weights]
        rarity = rng.choices(candidate_rarities, weights=[entry[1] for entry in rarity_weights], k=1)[0]
        if context["mood"] in {"mysterious", "melancholic"} and rarity == "common" and rng.random() > 0.65:
            rarity = "uncommon"

        jitter_mult = {"common": 0.06, "uncommon": 0.1, "rare": 0.16, "epic": 0.22}[rarity]
        adjusted_stats: dict[str, float] = {}
        for stat_name, stat_value in base_stats.items():
            jitter = 1.0 + rng.uniform(-jitter_mult, jitter_mult)
            adjusted_stats[stat_name] = round(max(0.0, stat_value * jitter), 3)

        tint_base = {"r": 1.0, "g": 1.0, "b": 1.0}
        if context["season"] == "autumn":
            tint_base = {"r": 1.04, "g": 0.92, "b": 0.86}
        elif context["season"] == "winter":
            tint_base = {"r": 0.91, "g": 0.95, "b": 1.07}
        elif context["season"] == "spring":
            tint_base = {"r": 1.03, "g": 1.06, "b": 0.98}
        elif context["season"] == "summer":
            tint_base = {"r": 1.06, "g": 1.01, "b": 0.94}
        if muted_palette:
            tint_base = {channel: round(multiplier * 0.97, 3) for channel, multiplier in tint_base.items()}
        tint = {channel: _clamp_color(multiplier + rng.uniform(-0.02, 0.02)) for channel, multiplier in tint_base.items()}

        quality_options = quality_suffixes.get(rarity, [""])
        quality_suffix = rng.choice(quality_options)
        name = base_name if not quality_suffix else f"{base_name} {quality_suffix}"
        material_variant = f"{template.get('material_hint', 'alloy')}_{season_material_shift[context['season']]}"
        rarity_value_mult = {"common": 1.0, "uncommon": 1.4, "rare": 2.15, "epic": 3.4}[rarity]
        value = int(round(base_value * rarity_value_mult * (1.0 + rng.uniform(-0.06, 0.08))))
        item_slug = re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_") or f"{normalized_type}_{index + 1}"
        item_id = f"{normalized_type}_{item_slug}_{index + 1}"

        generated.append(
            {
                "item_id": item_id,
                "template_type": normalized_type,
                "name": name,
                "asset_id": base_asset,
                "icon_asset_id": icon_asset,
                "stats": adjusted_stats,
                "value": max(1, value),
                "rarity": rarity,
                "variation": {
                    "seed": used_seed,
                    "index": index,
                    "season": context["season"],
                    "mood": context["mood"],
                    "quality_suffix": quality_suffix or "none",
                    "material_variant": material_variant,
                    "tint": tint,
                    "art_bible_style": art_bible.art_direction,
                },
            }
        )

    return {
        "schema": "gameforge.loot_generation.v1",
        "prompt": clean_prompt,
        "seed": used_seed,
        "template_type": normalized_type,
        "count": safe_count,
        "items": generated,
    }




def quality_score(payload: dict[str, Any], art_bible: ArtBible | None = None) -> dict[str, Any]:
    """Estimate content quality and light performance cost for generated assets/scenes."""
    if not isinstance(payload, dict):
        return {
            "score": 0,
            "components": {},
            "estimated_vram_mb": 0.0,
            "warnings": ["payload_not_object"],
        }

    render_2d = payload.get("render_2d") if isinstance(payload.get("render_2d"), dict) else {}
    sprites = render_2d.get("sprites") if isinstance(render_2d.get("sprites"), list) else []
    tilemaps = render_2d.get("tilemaps") if isinstance(render_2d.get("tilemaps"), list) else []

    sprite_count = len([row for row in sprites if isinstance(row, dict)])
    tilemap_count = len([row for row in tilemaps if isinstance(row, dict)])

    resolution_score = 50.0
    dimensions = payload.get("dimensions") if isinstance(payload.get("dimensions"), dict) else {}
    width = int(dimensions.get("width", 1024)) if isinstance(dimensions.get("width"), (int, float)) else 1024
    height = int(dimensions.get("height", 1024)) if isinstance(dimensions.get("height"), (int, float)) else 1024
    pixels = max(1, width * height)
    if pixels >= 2048 * 2048:
        resolution_score = 100.0
    elif pixels >= 1024 * 1024:
        resolution_score = 84.0
    elif pixels >= 512 * 512:
        resolution_score = 62.0

    complexity_raw = sprite_count + (tilemap_count * 4)
    complexity_score = max(40.0, min(100.0, 100.0 - max(0.0, complexity_raw - 64.0) * 0.55))

    art_score = 72.0
    if art_bible is not None:
        prompt_blob = " ".join([
            str(payload.get("prompt", "")),
            str(payload.get("enhanced_prompt", "")),
            str(payload.get("world_style_guide", "")),
        ]).lower()
        palette_hits = sum(1 for token in art_bible.palette_keywords if token.lower() in prompt_blob)
        light_hits = sum(1 for token in art_bible.lighting_guidance if token.lower() in prompt_blob)
        style_hit = 1 if art_bible.art_direction.lower() in prompt_blob else 0
        art_score = min(100.0, 60.0 + palette_hits * 6.5 + light_hits * 5.5 + style_hit * 8.0)

    base_texture_bytes = float(width * height * 4)
    sprite_bytes = float(sprite_count) * 256.0 * 256.0 * 4.0
    tilemap_bytes = float(tilemap_count) * 512.0 * 512.0 * 4.0
    estimated_vram_mb = round((base_texture_bytes + sprite_bytes + tilemap_bytes) / (1024.0 * 1024.0), 2)
    vram_score = max(35.0, min(100.0, 100.0 - max(0.0, estimated_vram_mb - 256.0) * 0.22))

    consistency = consistency_score(payload, art_bible=art_bible) if art_bible is not None else {"score": 72.0, "components": {}}
    consistency_numeric = float(consistency.get("score", 72.0))

    weighted = (
        resolution_score * 0.25
        + complexity_score * 0.2
        + art_score * 0.25
        + vram_score * 0.15
        + consistency_numeric * 0.15
    )
    final_score = int(round(max(0.0, min(100.0, weighted))))

    warnings: list[str] = []
    if estimated_vram_mb > 768.0:
        warnings.append("estimated_vram_high")
    if sprite_count > 280:
        warnings.append("sprite_count_high")

    return {
        "score": final_score,
        "components": {
            "resolution": round(resolution_score, 2),
            "complexity": round(complexity_score, 2),
            "art_bible_adherence": round(art_score, 2),
            "vram_efficiency": round(vram_score, 2),
            "consistency_with_art_bible": round(consistency_numeric, 2),
        },
        "estimated_vram_mb": estimated_vram_mb,
        "sprite_count": sprite_count,
        "warnings": warnings,
        "consistency_score": round(consistency_numeric, 2),
        "consistency_components": consistency.get("components", {}),
        "consistency_details": consistency,
    }


def _upsert_scene_quality_metadata(scene_payload: dict[str, Any], quality: dict[str, Any], source: str, prompt: str | None = None) -> None:
    quality_node = scene_payload.get("quality_metadata")
    if not isinstance(quality_node, dict):
        quality_node = {}
        scene_payload["quality_metadata"] = quality_node

    quality_node["schema"] = "gameforge.scene_quality_metadata.v1"
    quality_node["score"] = int(quality.get("score", 0))
    quality_node["components"] = quality.get("components", {})
    quality_node["estimated_vram_mb"] = float(quality.get("estimated_vram_mb", 0.0))
    quality_node["sprite_count"] = int(quality.get("sprite_count", 0))
    quality_node["warnings"] = quality.get("warnings", [])
    quality_node["consistency_score"] = float(quality.get("consistency_score", 0.0))
    quality_node["consistency_components"] = quality.get("consistency_components", {})
    quality_node["source"] = source
    if prompt:
        quality_node["prompt"] = prompt


def apply_generated_loot_to_scene(
    scene_path: Path,
    prompt: str,
    template_type: str = "weapon",
    count: int = 1,
    target_inventory: str = "player",
    seed: int | None = None,
    art_bible_path: Path | None = None,
    templates_path: Path | None = None,
) -> dict[str, object]:
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    if art_bible_path is not None and art_bible_path.exists():
        art_bible = ArtBible.from_json_file(art_bible_path)
    else:
        art_bible = default_art_bible(project_name=scene_path.parent.name or "Soul Loom Project")

    generated = generate_loot(
        prompt=prompt,
        art_bible=art_bible,
        template_type=template_type,
        count=count,
        seed=seed,
        templates_path=templates_path,
    )
    items = generated["items"] if isinstance(generated.get("items"), list) else []
    destination = (target_inventory or "player").strip().lower()

    player_inventory = scene_payload.get("player_inventory")
    if not isinstance(player_inventory, dict):
        player_inventory = {}
        scene_payload["player_inventory"] = player_inventory

    generated_inventory = scene_payload.get("generated_inventory")
    if not isinstance(generated_inventory, dict):
        generated_inventory = {"player": []}
        scene_payload["generated_inventory"] = generated_inventory
    if not isinstance(generated_inventory.get("player"), list):
        generated_inventory["player"] = []

    npc_inventories = scene_payload.get("npc_inventories")
    if not isinstance(npc_inventories, dict):
        npc_inventories = {}
        scene_payload["npc_inventories"] = npc_inventories

    target_stack: dict[str, object]
    target_generated: list[object]
    if destination == "player":
        target_stack = player_inventory
        target_generated = generated_inventory["player"]
    else:
        if not isinstance(npc_inventories.get(destination), dict):
            npc_inventories[destination] = {}
        if not isinstance(generated_inventory.get("npcs"), dict):
            generated_inventory["npcs"] = {}
        npcs_generated = generated_inventory["npcs"]
        if not isinstance(npcs_generated, dict):
            npcs_generated = {}
            generated_inventory["npcs"] = npcs_generated
        if not isinstance(npcs_generated.get(destination), list):
            npcs_generated[destination] = []
        target_stack = npc_inventories[destination]
        target_generated = npcs_generated[destination]

    for item in items:
        if not isinstance(item, dict):
            continue
        item_id = str(item.get("item_id", "")).strip()
        if not item_id:
            continue
        quantity_value = target_stack.get(item_id, 0)
        quantity = int(quantity_value) if isinstance(quantity_value, (int, float)) else 0
        target_stack[item_id] = quantity + 1
        target_generated.append(item)

    economy = scene_payload.get("economy")
    if not isinstance(economy, dict):
        economy = {}
        scene_payload["economy"] = economy
    price_table = economy.get("price_table")
    if not isinstance(price_table, dict):
        price_table = {}
        economy["price_table"] = price_table
    for item in items:
        if not isinstance(item, dict):
            continue
        item_id = str(item.get("item_id", "")).strip()
        item_value = item.get("value")
        if item_id and isinstance(item_value, (int, float)):
            price_table[item_id] = int(item_value)

    render_2d = scene_payload.get("render_2d")
    if not isinstance(render_2d, dict):
        render_2d = {"enabled": True, "sprites": [], "tilemaps": [], "entity_sprite_map": {}}
        scene_payload["render_2d"] = render_2d
    entity_sprite_map = render_2d.get("entity_sprite_map")
    if not isinstance(entity_sprite_map, dict):
        entity_sprite_map = {}
        render_2d["entity_sprite_map"] = entity_sprite_map
    for item in items:
        if not isinstance(item, dict):
            continue
        item_id = str(item.get("item_id", "")).strip()
        icon_asset_id = str(item.get("icon_asset_id", "")).strip()
        if item_id and icon_asset_id:
            entity_sprite_map[item_id] = icon_asset_id

    quality = quality_score(scene_payload, art_bible=art_bible)
    _upsert_scene_quality_metadata(scene_payload, quality, source="generate-loot", prompt=prompt)


    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")
    return {
        "scene_path": str(scene_path),
        "target_inventory": destination,
        "applied_count": len(items),
        "generated": generated,
    }


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


def apply_kit_bash_to_scene(
    scene_path: Path,
    prompt: str,
    art_bible_path: Path | None = None,
    kits_path: Path | None = None,
    variant_count: int = 1,
) -> dict[str, object]:
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
        art_bible = default_art_bible(project_name=scene_path.parent.name or "Soul Loom Project")

    existing_modules = [
        {"module_id": str(item.get("module_id", ""))}
        for item in sprites
        if isinstance(item, dict) and item.get("module_id")
    ]
    result = bash_scene(prompt, art_bible, existing_modules=existing_modules, kits_path=kits_path)
    variation_seed = _seed_for_prompt(prompt, len(existing_modules))
    varied_instances = apply_variations(result["module_instances"], prompt, art_bible, variation_seed)
    safe_variant_count = max(1, int(variant_count))

    variant_batch = batch_generate([prompt], art_bible=art_bible, count=safe_variant_count)
    scene_variants = scene_payload.get("generated_asset_variants")
    if not isinstance(scene_variants, list):
        scene_variants = []
        scene_payload["generated_asset_variants"] = scene_variants
    scene_variants.append(
        {
            "schema": "gameforge.scene_asset_variants.v1",
            "source": "kit-bash-scene",
            "prompt": prompt,
            "variant_count": safe_variant_count,
            "shared_seed": variant_batch.get("shared_seed", variation_seed),
            "control_profile": variant_batch.get("control_profile", {}),
            "hooks": variant_batch.get("hooks", {}),
            "generated_at_utc": variant_batch.get("generated_at_utc"),
            "variants": variant_batch.get("items", []),
        }
    )

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

    quality = quality_score(scene_payload, art_bible=art_bible)
    _upsert_scene_quality_metadata(scene_payload, quality, source="kit-bash-scene", prompt=prompt)

    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")

    result["scene_path"] = str(scene_path)
    result["variation_seed"] = variation_seed
    result["module_instances"] = varied_instances
    result["sprites_added"] = len(varied_instances)
    result["variant_batch"] = variant_batch
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
        art_bible = default_art_bible(project_name=scene_path.parent.name or "Soul Loom Project")

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

    quality = quality_score(scene_payload, art_bible=art_bible)
    _upsert_scene_quality_metadata(scene_payload, quality, source="apply-variations", prompt=prompt)

    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")
    return {
        "scene_path": str(scene_path),
        "prompt": prompt,
        "variation_seed": variation_seed,
        "sprites_updated": updated,
    }

