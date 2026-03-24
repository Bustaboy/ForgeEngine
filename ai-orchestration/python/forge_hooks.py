"""JSON-compatible AI orchestration hooks for ForgeEngine V1 runtime systems.

These hooks are intentionally local-first and deterministic. They generate
runtime-compatible JSON payloads that can be loaded by `SceneLoader.cpp` while
remaining backward-compatible with existing scene files.
"""

from __future__ import annotations

import json
import re
from copy import deepcopy
from pathlib import Path
from typing import Any


def _slug(value: str) -> str:
    token = re.sub(r"[^a-z0-9]+", "_", value.lower()).strip("_")
    return token or "value"


def _vec3(x: float, y: float, z: float) -> dict[str, float]:
    return {"x": float(x), "y": float(y), "z": float(z)}


def _vec4(x: float, y: float, z: float, w: float = 1.0) -> dict[str, float]:
    return {"x": float(x), "y": float(y), "z": float(z), "w": float(w)}


def _ivec2(x: int, y: int) -> dict[str, int]:
    return {"x": int(x), "y": int(y)}


def _safe_entity_id(scene_json: dict[str, Any]) -> int:
    max_id = 0
    entities = scene_json.get("entities")
    if isinstance(entities, list):
        for entity in entities:
            if isinstance(entity, dict):
                value = entity.get("id", 0)
                if isinstance(value, int):
                    max_id = max(max_id, value)
    return max_id + 1


def generate_dialog_tree(npc_name: str, personality: str, theme: str) -> dict[str, Any]:
    """Generate a `DialogComponent` payload compatible with runtime JSON.

    Returns keys accepted by `SceneLoader.cpp`:
    - nodes[].id, nodes[].text, nodes[].choices[]
    - choice.text, choice.next_node_id, choice.effect
    - start_node_id, active_node_id, in_progress
    """
    npc = npc_name.strip() or "NPC"
    personality_label = personality.strip() or "neutral"
    theme_label = theme.strip() or "local affairs"
    base = _slug(npc)

    start_id = f"{base}_intro"
    trust_id = f"{base}_trust"
    task_id = f"{base}_task"

    return {
        "nodes": [
            {
                "id": start_id,
                "text": f"{npc} ({personality_label}) studies you carefully. \"Here about {theme_label}?\"",
                "choices": [
                    {
                        "text": "I can help if you need something.",
                        "next_node_id": trust_id,
                        "effect": {"relationship_delta": 0.2},
                    },
                    {
                        "text": "Just browsing. Any supplies?",
                        "next_node_id": task_id,
                        "effect": {"inventory_item": "coin", "inventory_delta": 1, "relationship_delta": -0.05},
                    },
                ],
            },
            {
                "id": trust_id,
                "text": f"\"Good. Bring wood and stone before sundown and we'll talk again.\"",
                "choices": [
                    {
                        "text": "Understood. I will return prepared.",
                        "effect": {"relationship_delta": 0.1},
                    }
                ],
            },
            {
                "id": task_id,
                "text": "\"Take this token and check the town board for contracts.\"",
                "choices": [
                    {
                        "text": "Thanks. I'll start there.",
                        "effect": {"inventory_item": "quest_token", "inventory_delta": 1},
                    }
                ],
            },
        ],
        "start_node_id": start_id,
        "active_node_id": start_id,
        "in_progress": False,
    }


def generate_recipes(theme: str, count: int = 4) -> list[dict[str, Any]]:
    """Generate recipe dicts aligned with `InventorySystem::Recipe` fields."""
    if count <= 0:
        raise ValueError("count must be > 0")

    theme_label = theme.strip() or "settlement"
    normalized = _slug(theme_label)
    recipe_rows: list[dict[str, Any]] = []
    for index in range(count):
        suffix = index + 1
        recipe_rows.append(
            {
                "name": f"{theme_label.title()}Craft{suffix}",
                "inputs": {"wood": 2 + index, "stone": 1 + (index % 3)},
                "output_item": f"{normalized}_item_{suffix}",
                "output_quantity": 1 + (index % 2),
            }
        )
    return recipe_rows


def generate_building_templates(count: int = 3) -> list[dict[str, Any]]:
    """Generate build template dicts aligned with `BuildTemplate` field names."""
    if count <= 0:
        raise ValueError("count must be > 0")

    templates: list[dict[str, Any]] = []
    for index in range(count):
        size_x = 2 + (index % 2)
        size_y = 2 + (index % 3)
        templates.append(
            {
                "type": f"AIGeneratedStructure{index + 1}",
                "grid_size": _ivec2(size_x, size_y),
                "world_scale": _vec3(float(size_x), 1.0 + (index * 0.1), float(size_y)),
                "color": _vec4(0.3 + (index * 0.1), 0.5, 0.4 + (index * 0.07), 1.0),
            }
        )
    return templates


def generate_npc_with_dialog(name: str, role: str) -> dict[str, Any]:
    """Generate a runtime-compatible `Entity` containing a `DialogComponent`."""
    display_name = name.strip() or "Generated NPC"
    role_label = role.strip() or "villager"
    color = _vec4(0.65, 0.75, 0.9, 1.0) if role_label.lower() == "healer" else _vec4(0.8, 0.7, 0.5, 1.0)

    return {
        "id": 0,
        "name": display_name,
        "role": role_label,
        "faction": {"faction_id": "guild_builders", "role": role_label},
        "transform": {"pos": _vec3(0.0, 0.0, 0.0), "rot": _vec3(0.0, 0.0, 0.0), "scale": _vec3(0.8, 1.6, 0.8)},
        "renderable": {"color": color},
        "velocity": _vec3(0.0, 0.0, 0.0),
        "inventory": {"herb": 2} if role_label.lower() == "healer" else {"coin": 1},
        "dialog": generate_dialog_tree(display_name, role_label, "village life"),
    }


def modify_scene(scene_json: dict[str, Any], instruction: str) -> dict[str, Any]:
    """Apply high-level local scene mutations while preserving SceneLoader compatibility."""
    if not isinstance(scene_json, dict):
        raise ValueError("scene_json must be a dict")

    updated = deepcopy(scene_json)
    normalized = instruction.strip().lower()

    entities = updated.setdefault("entities", [])
    if not isinstance(entities, list):
        raise ValueError("scene_json['entities'] must be a list when present")

    if "npc" in normalized:
        npc = generate_npc_with_dialog("AI NPC", "villager")
        npc["id"] = _safe_entity_id(updated)
        npc["transform"]["pos"] = _vec3(float(len(entities)) * 1.5, 0.0, 0.0)
        entities.append(npc)
        updated.setdefault("npc_relationships", {})[str(npc["id"])] = 0.0

    house_count = 0
    number_match = re.search(r"\b(\d+)\b", normalized)
    if ("house" in normalized or "building" in normalized) and number_match:
        house_count = int(number_match.group(1))
    elif "house" in normalized or "building" in normalized:
        house_count = 1

    for index in range(max(0, house_count)):
        entity_id = _safe_entity_id(updated)
        entities.append(
            {
                "id": entity_id,
                "transform": {
                    "pos": _vec3(3.0 + (index * 2.5), 0.0, 2.0),
                    "rot": _vec3(0.0, 0.0, 0.0),
                    "scale": _vec3(2.0, 1.0, 2.0),
                },
                "renderable": {"color": _vec4(0.84, 0.58, 0.34, 1.0)},
                "velocity": _vec3(0.0, 0.0, 0.0),
                "buildable": {"type": "SmallHouse", "grid_size": _ivec2(2, 2)},
            }
        )

    if "day" in normalized or "night" in normalized or "cycle" in normalized:
        if "faster" in normalized:
            updated["day_cycle_speed"] = float(updated.get("day_cycle_speed", 0.01)) * 1.5
        elif "slower" in normalized:
            updated["day_cycle_speed"] = float(updated.get("day_cycle_speed", 0.01)) * 0.75
        elif "night" in normalized:
            updated["day_progress"] = 0.85
        else:
            updated["day_progress"] = 0.25
        updated["day_cycle_speed"] = max(0.0, float(updated.get("day_cycle_speed", 0.01)))
        updated["day_progress"] = min(1.0, max(0.0, float(updated.get("day_progress", 0.25))))

    if "recipe" in normalized:
        updated["ai_generated_recipes"] = generate_recipes("settlement", 4)
    if "template" in normalized or "build template" in normalized:
        updated["ai_generated_build_templates"] = generate_building_templates(3)

    return updated


def co_creator_tick(
    scene_json: dict[str, Any],
    biome: str,
    world_style_guide: str,
    recent_actions: list[str],
    day_progress: float,
) -> list[dict[str, Any]]:
    """Produce small, coherent scene mutations for the live co-creator panel.

    Each suggestion returns:
    - id/title
    - why_this_fits (human-facing rationale)
    - mutation (small runtime-compatible change payload)
    """
    if not isinstance(scene_json, dict):
        raise ValueError("scene_json must be a dict")

    entities = scene_json.get("entities")
    if not isinstance(entities, list):
        entities = []

    biome_label = (biome or scene_json.get("biome") or "temperate").strip()
    style_label = (world_style_guide or scene_json.get("world_style_guide") or "grounded stylized frontier").strip()
    safe_day_progress = min(1.0, max(0.0, float(day_progress)))
    recent_text = " ".join(item.strip().lower() for item in recent_actions if item.strip())
    style_lower = style_label.lower()
    biome_lower = biome_label.lower()
    factions_raw = scene_json.get("factions")
    factions = factions_raw if isinstance(factions_raw, dict) else {}
    reputation_raw = scene_json.get("player_reputation")
    player_reputation = reputation_raw if isinstance(reputation_raw, dict) else {}
    dominant_faction_id = ""
    dominant_faction_name = "local communities"
    dominant_reputation = 0.0
    for faction_id, value in player_reputation.items():
        if isinstance(value, (int, float)) and (not dominant_faction_id or value > dominant_reputation):
            dominant_faction_id = str(faction_id)
            dominant_reputation = float(value)
    if dominant_faction_id and isinstance(factions.get(dominant_faction_id), dict):
        dominant_faction_name = str(factions[dominant_faction_id].get("display_name") or dominant_faction_id)

    anchor_x = 0.0
    anchor_z = 0.0
    anchor_count = 0
    for entity in entities:
        if not isinstance(entity, dict):
            continue
        transform = entity.get("transform")
        if not isinstance(transform, dict):
            continue
        pos = transform.get("pos")
        if not isinstance(pos, dict):
            continue
        x = pos.get("x")
        z = pos.get("z")
        if isinstance(x, (int, float)) and isinstance(z, (int, float)):
            anchor_x += float(x)
            anchor_z += float(z)
            anchor_count += 1
    if anchor_count > 0:
        anchor_x /= anchor_count
        anchor_z /= anchor_count

    next_id = _safe_entity_id(scene_json)
    spacing = 2.2 + min(2.8, anchor_count * 0.12)

    def _buildable_entity(
        entity_id: int,
        entity_type: str,
        color: tuple[float, float, float],
        grid_size: tuple[int, int],
        offset_x: float,
        offset_z: float,
        scale: tuple[float, float, float],
    ) -> dict[str, Any]:
        return {
            "id": entity_id,
            "transform": {
                "pos": _vec3(anchor_x + offset_x, 0.0, anchor_z + offset_z),
                "rot": _vec3(0.0, 0.0, 0.0),
                "scale": _vec3(scale[0], scale[1], scale[2]),
            },
            "renderable": {"color": _vec4(color[0], color[1], color[2], 1.0)},
            "velocity": _vec3(0.0, 0.0, 0.0),
            "buildable": {"type": entity_type, "grid_size": _ivec2(grid_size[0], grid_size[1])},
        }

    is_evening = 0.60 <= safe_day_progress <= 0.90
    is_night = safe_day_progress >= 0.85 or safe_day_progress <= 0.15

    suggestions: list[dict[str, Any]] = []

    if "desert" in biome_lower:
        suggestions.append(
            {
                "id": "desert_oasis_outpost",
                "title": "Add a shaded oasis outpost near your current path",
                "why_this_fits": (
                    "Your biome is desert, so shade and water access feel practical. "
                    "This placement sits near existing structures so travel loops stay tight and believable. "
                    f"It aligns with the current tone around {dominant_faction_name}."
                ),
                "mutation": {
                    "type": "add_entity",
                    "faction_id": dominant_faction_id,
                    "entity": _buildable_entity(
                        next_id,
                        "DesertOasisOutpost",
                        (0.77, 0.68, 0.46),
                        (3, 2),
                        spacing,
                        0.8,
                        (2.4, 1.1, 1.9),
                    ),
                },
            }
        )
    elif "snow" in biome_lower or "tundra" in biome_lower or "arctic" in biome_lower:
        suggestions.append(
            {
                "id": "snow_windbreak_hut",
                "title": "Add a windbreak hut on the exposed edge",
                "why_this_fits": (
                    "In cold biomes, shelter placement matters more than decoration. "
                    "A compact windbreak extends survival space without breaking your established layout rhythm."
                ),
                "mutation": {
                    "type": "add_entity",
                    "faction_id": dominant_faction_id,
                    "entity": _buildable_entity(
                        next_id,
                        "WindbreakHut",
                        (0.70, 0.78, 0.85),
                        (2, 2),
                        -spacing,
                        1.2,
                        (1.7, 1.0, 1.7),
                    ),
                },
            }
        )
    else:
        suggestions.append(
            {
                "id": "temperate_waystation",
                "title": "Add a waystation that supports your existing route",
                "why_this_fits": (
                    "Your current world reads as a lived-in route network, so a waystation improves pacing and story continuity. "
                    "It is placed close enough to feel useful but not crowded."
                ),
                "mutation": {
                    "type": "add_entity",
                    "faction_id": dominant_faction_id,
                    "entity": _buildable_entity(
                        next_id,
                        "Waystation",
                        (0.58, 0.66, 0.54),
                        (2, 2),
                        spacing * 0.8,
                        -1.1,
                        (1.9, 1.0, 1.9),
                    ),
                },
            }
        )

    lighting_note = "lantern" if is_night else "sunset focal lighting" if is_evening else "daylight readability"
    lighting_target = 0.82 if is_evening else 0.90 if is_night else 0.40
    suggestions.append(
        {
            "id": "tone_match_lighting",
            "title": f"Tune the scene clock for stronger {lighting_note}",
            "why_this_fits": (
                "Small lighting shifts keep emotional tone coherent with what players are likely doing at this time slice. "
                "This improves visual harmony without changing your core layout."
            ),
            "mutation": {
                "type": "set_day_progress",
                "value": lighting_target,
            },
        }
    )

    if "build" in recent_text or "house" in recent_text or "outpost" in recent_text:
        followup_type = "SupplyDepot" if "realistic" in style_lower or "grounded" in style_lower else "CraftNook"
        suggestions.append(
            {
                "id": "progression_followup_support",
                "title": "Add support infrastructure for your recent expansion",
                "why_this_fits": (
                    "You recently expanded structures, so adding nearby support space keeps cause-and-effect progression believable. "
                    "It turns construction into a lived gameplay loop instead of isolated props."
                ),
                "mutation": {
                    "type": "add_entity",
                    "entity": _buildable_entity(
                        next_id + 1,
                        followup_type,
                        (0.62, 0.57, 0.48),
                        (2, 1),
                        spacing * 0.55,
                        2.0,
                        (1.6, 0.9, 1.2),
                    ),
                },
            }
        )

    filtered: list[dict[str, Any]] = []
    for suggestion in suggestions:
        mutation = suggestion.get("mutation")
        if not isinstance(mutation, dict):
            continue
        faction_id = mutation.get("faction_id")
        if isinstance(faction_id, str) and faction_id:
            rep = player_reputation.get(faction_id, 0.0)
            if isinstance(rep, (int, float)) and float(rep) < -25.0:
                continue
        filtered.append(suggestion)

    return filtered[:3]


def apply_to_scene_file(scene_path: str, instruction: str) -> dict[str, Any]:
    """Load, modify, and save a scene JSON file in place."""
    path = Path(scene_path)
    if not path.exists():
        raise FileNotFoundError(f"scene file not found: {scene_path}")

    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("scene JSON root must be an object")

    updated = modify_scene(payload, instruction)
    path.write_text(json.dumps(updated, indent=2) + "\n", encoding="utf-8")
    return updated
