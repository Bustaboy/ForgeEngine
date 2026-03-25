#!/usr/bin/env python3
"""Live natural-language scene editing helpers for ForgeEngine V1."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any

from art_bible import ArtBible, default_art_bible
from kit_bashing import apply_variations, bash_scene, quality_score, _upsert_scene_quality_metadata


_PATCH_SCHEMA = "gameforge.scene_live_edit_patch.v1"


def _seed_for_prompt(prompt: str) -> int:
    digest = hashlib.sha256(f"live-edit|{prompt.strip()}".encode("utf-8")).digest()
    return int.from_bytes(digest[:8], byteorder="big", signed=False)


def _default_render_2d(scene_payload: dict[str, Any]) -> dict[str, Any]:
    render_2d = scene_payload.get("render_2d")
    if not isinstance(render_2d, dict):
        render_2d = {"enabled": True, "sprites": [], "tilemaps": [], "entity_sprite_map": {}}
        scene_payload["render_2d"] = render_2d
    if not isinstance(render_2d.get("sprites"), list):
        render_2d["sprites"] = []
    if not isinstance(render_2d.get("tilemaps"), list):
        render_2d["tilemaps"] = []
    if not isinstance(render_2d.get("entity_sprite_map"), dict):
        render_2d["entity_sprite_map"] = {}
    return render_2d


def _prompt_context(prompt: str) -> dict[str, Any]:
    normalized = prompt.lower()
    season = "autumn" if re.search(r"\b(autumn|fall)\b", normalized) else "neutral"
    mood = "melancholic" if re.search(r"\b(melancholic|somber|moody|sad)\b", normalized) else "neutral"
    dusk = bool(re.search(r"\b(dusk|sunset|evening|twilight)\b", normalized))
    lanterns = bool(re.search(r"\b(lantern|glow|glowing|warm light)\b", normalized))
    leaves = bool(re.search(r"\b(leaf|leaves|foliage)\b", normalized))
    return {
        "season": season,
        "mood": mood,
        "dusk": dusk,
        "lanterns": lanterns,
        "leaves": leaves,
    }


def natural_language_edit(scene_json_path: Path, prompt: str, art_bible: ArtBible) -> dict[str, Any]:
    clean_prompt = prompt.strip()
    if not clean_prompt:
        raise ValueError("prompt must be non-empty")

    scene_payload = json.loads(scene_json_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    render_2d = _default_render_2d(scene_payload)
    sprites = render_2d["sprites"]
    context = _prompt_context(clean_prompt)

    existing_modules = [
        {"module_id": str(item.get("module_id", ""))}
        for item in sprites
        if isinstance(item, dict) and item.get("module_id")
    ]
    seed = _seed_for_prompt(clean_prompt)

    bashing = bash_scene(clean_prompt, art_bible, existing_modules=existing_modules)
    candidates = apply_variations(bashing.get("module_instances", []), clean_prompt, art_bible, seed)
    additions: list[dict[str, Any]] = []
    for instance in candidates[:4]:
        additions.append(
            {
                "asset_id": instance.get("asset_id", ""),
                "position": instance.get("position", {"x": 0.0, "y": 0.0}),
                "size": instance.get("size", {"x": 1.0, "y": 1.0}),
                "tint": instance.get("tint", {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}),
                "rotation_radians": float(instance.get("rotation_radians", 0.0)),
                "layer": float(instance.get("layer", 0.0)),
                "entity_type": "kit_module",
                "module_id": instance.get("module_id", ""),
                "variation": instance.get("variation", {}),
            }
        )

    tint_update: dict[str, Any] = {}
    if context["mood"] == "melancholic":
        tint_update = {"r": 0.86, "g": 0.83, "b": 0.95, "a": 1.0}
    elif context["dusk"]:
        tint_update = {"r": 0.92, "g": 0.88, "b": 0.9, "a": 1.0}

    sprite_updates: list[dict[str, Any]] = []
    if tint_update:
        sprite_updates.append(
            {
                "match": {"entity_type": "kit_module"},
                "set": {"tint": tint_update},
                "limit": 24,
            }
        )

    if context["lanterns"]:
        additions.append(
            {
                "asset_id": "prop_lantern_glow",
                "position": {"x": -1.6, "y": -0.9},
                "size": {"x": 0.75, "y": 0.75},
                "tint": {"r": 1.08, "g": 0.92, "b": 0.72, "a": 1.0},
                "rotation_radians": 0.0,
                "layer": 60.0,
                "entity_type": "kit_module",
                "module_id": "prop_lantern",
                "variation": {"procedural_v1": {"seed": seed, "reason": "prompt_lantern_glow"}},
            }
        )

    entity_map_set: dict[str, str] = {}
    if context["leaves"]:
        entity_map_set["ambient_leaf_fx"] = "fx_autumn_leaves"

    patch = {
        "schema": _PATCH_SCHEMA,
        "prompt": clean_prompt,
        "seed": seed,
        "changes": {
            "render_2d": {
                "enabled": True,
                "sprites_add": additions,
                "sprites_update": sprite_updates,
                "entity_sprite_map_set": entity_map_set,
            }
        },
    }
    return patch


def apply_scene_patch(scene_json_path: Path, patch: dict[str, Any]) -> dict[str, Any]:
    payload = json.loads(scene_json_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    render_2d = _default_render_2d(payload)
    changes = patch.get("changes") if isinstance(patch, dict) else None
    render_changes = changes.get("render_2d") if isinstance(changes, dict) else None
    if not isinstance(render_changes, dict):
        return {"applied": False, "reason": "No render_2d changes"}

    render_2d["enabled"] = bool(render_changes.get("enabled", render_2d.get("enabled", False)))

    sprites = render_2d.get("sprites", [])
    if not isinstance(sprites, list):
        sprites = []
        render_2d["sprites"] = sprites

    added = 0
    for sprite in render_changes.get("sprites_add", []):
        if isinstance(sprite, dict) and sprite.get("asset_id"):
            sprites.append(sprite)
            added += 1

    updated = 0
    for instruction in render_changes.get("sprites_update", []):
        if not isinstance(instruction, dict):
            continue
        match = instruction.get("match")
        set_values = instruction.get("set")
        limit = int(instruction.get("limit", 999999))
        if not isinstance(match, dict) or not isinstance(set_values, dict):
            continue
        touched = 0
        for sprite in sprites:
            if not isinstance(sprite, dict):
                continue
            matched = True
            for key, expected in match.items():
                if sprite.get(key) != expected:
                    matched = False
                    break
            if not matched:
                continue
            for set_key, set_value in set_values.items():
                sprite[set_key] = set_value
            updated += 1
            touched += 1
            if touched >= limit:
                break

    entity_map = render_2d.get("entity_sprite_map")
    if not isinstance(entity_map, dict):
        entity_map = {}
        render_2d["entity_sprite_map"] = entity_map
    for key, value in render_changes.get("entity_sprite_map_set", {}).items():
        if str(key).strip() and str(value).strip():
            entity_map[str(key)] = str(value)

    tilemaps = render_2d.get("tilemaps")
    if not isinstance(tilemaps, list):
        tilemaps = []
        render_2d["tilemaps"] = tilemaps
    for change in render_changes.get("tilemaps_update", []):
        if not isinstance(change, dict):
            continue
        target_id = str(change.get("id", "")).strip()
        set_values = change.get("set")
        if not target_id or not isinstance(set_values, dict):
            continue
        for tilemap in tilemaps:
            if isinstance(tilemap, dict) and str(tilemap.get("id", "")).strip() == target_id:
                for set_key, set_value in set_values.items():
                    tilemap[set_key] = set_value
                break

    quality = quality_score(payload)
    _upsert_scene_quality_metadata(payload, quality, source="edit-scene", prompt=str(patch.get("prompt", "")))

    scene_json_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    return {
        "applied": True,
        "quality_score": quality.get("score", 0),
        "quality_metadata": quality,
        "scene_path": str(scene_json_path),
        "sprites_added": added,
        "sprites_updated": updated,
        "schema": patch.get("schema", "unknown"),
        "seed": patch.get("seed"),
    }


def edit_scene_from_prompt(scene_json_path: Path, prompt: str, art_bible_path: Path | None = None) -> dict[str, Any]:
    if art_bible_path is not None and art_bible_path.exists():
        art_bible = ArtBible.from_json_file(art_bible_path)
    else:
        art_bible = default_art_bible(project_name=scene_json_path.parent.name or "GameForge Project")

    patch = natural_language_edit(scene_json_path, prompt, art_bible)
    applied = apply_scene_patch(scene_json_path, patch)
    return {
        "command": "/edit_scene",
        "scene_path": str(scene_json_path),
        "prompt": prompt,
        "patch": patch,
        "applied": applied,
    }
