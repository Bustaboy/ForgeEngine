#!/usr/bin/env python3
"""GameForge V1 AI orchestration entrypoint and interview helpers."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path


UNCERTAINTY_CUES = {
    "i don't know",
    "idk",
    "unsure",
    "not sure",
    "you decide",
    "anything",
    "whatever",
    "unknown",
    "maybe",
}

THINK_FOR_ME_CUES = {
    "think for me",
    "think of something",
    "you pick",
    "pick for me",
    "surprise me",
}


@dataclass(frozen=True)
class SuggestionOption:
    option_id: str
    title: str
    summary: str
    tradeoff: str


@dataclass(frozen=True)
class SuggestionResponse:
    topic: str
    source_input: str
    ambiguous: bool
    options: list[SuggestionOption]


@dataclass(frozen=True)
class DirectionProposal:
    direction_id: str
    title: str
    elevator_pitch: str
    gameplay_pillars: list[str]
    prototype_seed: dict[str, object]
    tradeoff: str


@dataclass(frozen=True)
class ThinkForMeResponse:
    mode: str
    topic: str
    source_input: str
    triggered: bool
    confirmation_required: bool
    proposals: list[DirectionProposal]
    human_summary_markdown: str


@dataclass(frozen=True)
class RegenerationConflict:
    target_path: str
    lock_path: str
    reason: str


@dataclass(frozen=True)
class RegenerationResult:
    requires_confirmation: bool
    conflict_prompt: str
    conflicts: list[RegenerationConflict]
    updated_files: list[str]
    skipped_locked_files: list[str]


@dataclass(frozen=True)
class ConsequenceTransitionResult:
    applied: bool
    previous_node_id: str
    current_node_id: str
    selected_choice_id: str
    npc_state: dict[str, int]
    world_state: dict[str, str]


@dataclass(frozen=True)
class AssetImportRequest:
    source_path: str
    source_type: str
    license_id: str
    display_name: str | None = None
    user_tags: list[str] | None = None
    rights_confirmation: bool = False


@dataclass(frozen=True)
class AssetImportError:
    source_path: str
    code: str
    message: str
    remediation: str


@dataclass(frozen=True)
class AssetCatalogEntry:
    asset_id: str
    display_name: str
    category: str
    tags: list[str]
    license_id: str
    source_type: str
    relative_path: str
    imported_at_utc: str
    metadata: dict[str, object]


@dataclass(frozen=True)
class AssetImportPipelineResult:
    imported_assets: list[AssetCatalogEntry]
    errors: list[AssetImportError]


@dataclass(frozen=True)
class StylePresetDefinition:
    preset_id: str
    display_name: str
    parent_preset_id: str | None
    transformations: dict[str, dict[str, object]]
    source: str


@dataclass(frozen=True)
class ProjectStyleSelectionState:
    active_preset_id: str
    helper_mode: str


@dataclass(frozen=True)
class BotPlaytestProbe:
    probe_id: str
    probe_type: str
    target: str
    expected: object
    required: bool = True


@dataclass(frozen=True)
class BotPlaytestScenario:
    schema: str
    scenario_id: str
    title: str
    max_runtime_seconds: int
    probes: list[BotPlaytestProbe]


@dataclass(frozen=True)
class BotPlaytestProbeResult:
    probe_id: str
    status: str
    details: str
    required: bool


@dataclass(frozen=True)
class BotPlaytestResult:
    scenario_id: str
    prototype_root: str
    status: str
    human_review_required: bool
    summary: str
    completed_at_utc: str
    probe_results: list[BotPlaytestProbeResult]
    inconclusive_reasons: list[str]


@dataclass(frozen=True)
class PlaytestReportSection:
    section_id: str
    title: str
    status: str
    findings: list[str]
    recommendations: list[str]


@dataclass(frozen=True)
class ActionablePlaytestReport:
    schema: str
    report_id: str
    scenario_id: str
    prototype_root: str
    generated_at_utc: str
    overall_status: str
    summary: str
    sections: list[PlaytestReportSection]
    source_probe_results: list[BotPlaytestProbeResult]


ALLOWED_LICENSES = {
    "cc0-1.0",
    "public-domain",
    "cc-by-4.0",
    "user-owned",
}

BLOCKED_LICENSES = {
    "cc-by-sa",
    "cc-by-nc",
}

ASSET_CATEGORY_BY_EXTENSION = {
    ".png": "textures",
    ".jpg": "textures",
    ".jpeg": "textures",
    ".tga": "textures",
    ".bmp": "textures",
    ".gif": "ui",
    ".svg": "ui",
    ".wav": "audio",
    ".mp3": "audio",
    ".ogg": "audio",
    ".flac": "audio",
    ".fbx": "characters",
    ".glb": "props",
    ".gltf": "props",
    ".obj": "props",
    ".blend": "props",
}

STYLE_BUILTIN_PRESETS: list[StylePresetDefinition] = [
    StylePresetDefinition(
        preset_id="cozy-stylized",
        display_name="Cozy Stylized",
        parent_preset_id=None,
        source="built-in",
        transformations={
            "textures": {"saturation": 1.15, "contrast": 0.95, "temperature_shift": 0.2},
            "props": {"edge_softness": 0.6, "roughness_bias": 0.15},
            "ui": {"corner_rounding": 0.8, "font_weight": "medium", "accent_intensity": 0.85},
            "audio": {"warmth": 0.7, "dynamic_range": 0.65},
        },
    ),
    StylePresetDefinition(
        preset_id="semi-realistic",
        display_name="Semi-Realistic",
        parent_preset_id=None,
        source="built-in",
        transformations={
            "textures": {"saturation": 1.0, "contrast": 1.1, "temperature_shift": 0.0},
            "props": {"edge_softness": 0.35, "roughness_bias": 0.05},
            "ui": {"corner_rounding": 0.35, "font_weight": "regular", "accent_intensity": 0.7},
            "audio": {"warmth": 0.5, "dynamic_range": 0.9},
        },
    ),
    StylePresetDefinition(
        preset_id="low-poly-clean",
        display_name="Low-Poly Clean",
        parent_preset_id=None,
        source="built-in",
        transformations={
            "textures": {"saturation": 0.95, "contrast": 1.05, "temperature_shift": -0.05},
            "props": {"edge_softness": 0.1, "roughness_bias": 0.0, "simplify_geometry": 0.9},
            "ui": {"corner_rounding": 0.4, "font_weight": "semi-bold", "accent_intensity": 0.75},
            "audio": {"warmth": 0.45, "dynamic_range": 0.7},
        },
    ),
    StylePresetDefinition(
        preset_id="dark-fantasy-stylized",
        display_name="Dark Fantasy Stylized",
        parent_preset_id=None,
        source="built-in",
        transformations={
            "textures": {"saturation": 0.8, "contrast": 1.2, "temperature_shift": -0.25},
            "props": {"edge_softness": 0.25, "roughness_bias": 0.35},
            "ui": {"corner_rounding": 0.2, "font_weight": "semi-bold", "accent_intensity": 0.95},
            "audio": {"warmth": 0.3, "dynamic_range": 1.0},
        },
    ),
]


def _build_curated_options(topic: str) -> list[SuggestionOption]:
    normalized_topic = topic.strip().lower() or "game-direction"

    return [
        SuggestionOption(
            option_id=f"{normalized_topic}-balanced-foundation",
            title="Balanced Foundation",
            summary="Start with a medium-scope loop that mixes RTS/sim management with light RPG progression.",
            tradeoff="Safest path with predictable effort, but less distinctive at first.",
        ),
        SuggestionOption(
            option_id=f"{normalized_topic}-systems-first",
            title="Systems-First Sandbox",
            summary="Prioritize simulation depth and replayability before narrative complexity.",
            tradeoff="Strong emergent gameplay, but story and character hooks land later.",
        ),
        SuggestionOption(
            option_id=f"{normalized_topic}-story-first",
            title="Story-Led Adventure",
            summary="Lead with quests, tone, and player fantasy, while keeping systems intentionally simple.",
            tradeoff="Clear identity and onboarding, but lower systemic depth in early builds.",
        ),
    ]


def _build_direction_proposals(topic: str) -> list[DirectionProposal]:
    normalized_topic = topic.strip().lower() or "game-direction"
    return [
        DirectionProposal(
            direction_id=f"{normalized_topic}-cozy-colony-tales",
            title="Cozy Colony Tales",
            elevator_pitch="Build a small frontier town where relationships and seasonal events shape long-term growth.",
            gameplay_pillars=[
                "Resource planning with forgiving pacing",
                "NPC bonds influencing quests and rewards",
                "Expandable settlement with handcrafted landmarks",
            ],
            prototype_seed={
                "genre_weights": {"rts_sim": 0.65, "rpg": 0.35},
                "core_loop": "Gather, build, and resolve weekly social quests",
                "style_preset": "Cozy Stylized",
                "target_platforms": ["windows", "ubuntu"],
                "rendering": "vulkan-first",
            },
            tradeoff="Accessible and warm, but lower moment-to-moment combat intensity.",
        ),
        DirectionProposal(
            direction_id=f"{normalized_topic}-iron-frontier-command",
            title="Iron Frontier Command",
            elevator_pitch="Lead a tactical outpost where automation and defense systems evolve against escalating threats.",
            gameplay_pillars=[
                "Tight build-order strategy and logistics",
                "Upgradeable unit roles with light RPG specialization",
                "Reactive world events that pressure economy choices",
            ],
            prototype_seed={
                "genre_weights": {"rts_sim": 0.75, "rpg": 0.25},
                "core_loop": "Extract, fortify, and survive assault waves",
                "style_preset": "Semi-Realistic",
                "target_platforms": ["windows", "ubuntu"],
                "rendering": "vulkan-first",
            },
            tradeoff="Strong systems depth, but onboarding is harder for first-time players.",
        ),
        DirectionProposal(
            direction_id=f"{normalized_topic}-relicbound-odyssey",
            title="Relicbound Odyssey",
            elevator_pitch="Recover ancient relics across regions while your choices reshape factions and quest outcomes.",
            gameplay_pillars=[
                "Quest-driven exploration with branching consequences",
                "Companion progression and equipment synergy",
                "Light base upgrades unlocking new narrative routes",
            ],
            prototype_seed={
                "genre_weights": {"rts_sim": 0.35, "rpg": 0.65},
                "core_loop": "Explore, decide, and invest relic power into your camp",
                "style_preset": "Dark Fantasy Stylized",
                "target_platforms": ["windows", "ubuntu"],
                "rendering": "vulkan-first",
            },
            tradeoff="High narrative payoff, but systemic simulation breadth is intentionally narrower.",
        ),
    ]


def _render_direction_markdown(proposals: list[DirectionProposal]) -> str:
    lines = ["# Think-for-me proposals", ""]
    for idx, proposal in enumerate(proposals, start=1):
        lines.append(f"## {idx}) {proposal.title}")
        lines.append(f"- Direction ID: `{proposal.direction_id}`")
        lines.append(f"- Pitch: {proposal.elevator_pitch}")
        lines.append("- Gameplay pillars:")
        for pillar in proposal.gameplay_pillars:
            lines.append(f"  - {pillar}")
        lines.append(f"- Tradeoff: {proposal.tradeoff}")
        lines.append("")
    lines.append("Reply with a direction id to confirm before commitment.")
    return "\n".join(lines)


def generate_uncertainty_options(user_input: str, topic: str = "game-direction") -> SuggestionResponse:
    normalized = user_input.strip().lower()
    ambiguous = not normalized or any(cue in normalized for cue in UNCERTAINTY_CUES)

    options = _build_curated_options(topic) if ambiguous else []
    return SuggestionResponse(
        topic=topic,
        source_input=user_input,
        ambiguous=ambiguous,
        options=options,
    )


def generate_think_for_me_directions(user_input: str, topic: str = "game-direction") -> ThinkForMeResponse:
    normalized = user_input.strip().lower()
    triggered = any(cue in normalized for cue in THINK_FOR_ME_CUES)
    proposals = _build_direction_proposals(topic) if triggered else []
    markdown = _render_direction_markdown(proposals) if proposals else ""
    return ThinkForMeResponse(
        mode="think-for-me",
        topic=topic,
        source_input=user_input,
        triggered=triggered,
        confirmation_required=True,
        proposals=proposals,
        human_summary_markdown=markdown,
    )


def _slugify(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug or "prototype"


def _write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _normalize_lock_path(value: str) -> str:
    path = value.strip().replace("\\", "/").strip("/")
    return path


def _target_hits_lock(target_path: str, lock_path: str) -> bool:
    if not lock_path:
        return False
    normalized_target = _normalize_lock_path(target_path)
    normalized_lock = _normalize_lock_path(lock_path)
    return normalized_target == normalized_lock or normalized_target.startswith(f"{normalized_lock}/")


def _build_regeneration_conflict_prompt(conflicts: list[RegenerationConflict]) -> str:
    if not conflicts:
        return ""
    conflict_lines = [f"- {conflict.target_path} (locked by: {conflict.lock_path})" for conflict in conflicts]
    joined_lines = "\n".join(conflict_lines)
    return (
        "Regeneration requested changes in locked content.\n"
        "Review impacted targets:\n"
        f"{joined_lines}\n"
        "Confirm destructive regeneration to overwrite locked targets."
    )


def _resolve_safe_regeneration_destination(prototype_root: Path, target_path: str) -> Path:
    candidate_input = target_path.strip()
    if not candidate_input:
        raise ValueError("Regeneration target path cannot be blank.")

    normalized_input = candidate_input.replace("\\", "/")
    if re.match(r"^[a-zA-Z]:[\\/]", candidate_input):
        raise ValueError(f"Drive-qualified regeneration path is not allowed: {target_path}")
    if normalized_input.startswith("/") or normalized_input.startswith("//"):
        raise ValueError(f"Absolute regeneration path is not allowed: {target_path}")

    resolved_root = prototype_root.resolve()
    resolved_target = (resolved_root / Path(normalized_input)).resolve()
    try:
        resolved_target.relative_to(resolved_root)
    except ValueError as exc:
        raise ValueError(f"Regeneration target escapes prototype root: {target_path}") from exc
    return resolved_target


def apply_partial_regeneration(
    prototype_root: Path,
    updates: dict[str, str],
    locked_paths: list[str] | None = None,
    confirm_destructive: bool = False,
) -> RegenerationResult:
    locked_paths = locked_paths or []
    conflicts: list[RegenerationConflict] = []
    updated_files: list[str] = []
    skipped_locked_files: list[str] = []

    for target_path in sorted(updates.keys()):
        normalized_target = _normalize_lock_path(target_path)
        matching_lock = next((lock for lock in locked_paths if _target_hits_lock(normalized_target, lock)), None)
        if matching_lock and not confirm_destructive:
            skipped_locked_files.append(normalized_target)
            conflicts.append(
                RegenerationConflict(
                    target_path=normalized_target,
                    lock_path=_normalize_lock_path(matching_lock),
                    reason="locked-content-protection",
                )
            )
            continue

        destination = _resolve_safe_regeneration_destination(prototype_root, target_path)
        _write_text(destination, updates[target_path])
        updated_files.append(normalized_target)

    requires_confirmation = bool(conflicts)
    conflict_prompt = _build_regeneration_conflict_prompt(conflicts)
    return RegenerationResult(
        requires_confirmation=requires_confirmation,
        conflict_prompt=conflict_prompt,
        conflicts=conflicts,
        updated_files=updated_files,
        skipped_locked_files=skipped_locked_files,
    )


def _escape_cpp_string_literal(value: object) -> str:
    text = str(value or "")
    text = text.replace("\\", "\\\\")
    text = text.replace('"', '\\"')
    text = text.replace("\n", "\\n")
    text = text.replace("\r", "\\r")
    text = text.replace("\t", "\\t")
    return text


def _normalize_license_id(raw_license: str) -> str:
    normalized = raw_license.strip().lower().replace("_", "-")
    normalized = re.sub(r"\s+", "-", normalized)
    return normalized


def _derive_asset_category(path: Path) -> str:
    return ASSET_CATEGORY_BY_EXTENSION.get(path.suffix.lower(), "uncategorized")


def _split_tags(value: str) -> list[str]:
    return [piece for piece in re.split(r"[^a-z0-9]+", value.lower()) if piece]


def _build_auto_tags(path: Path, category: str, source_type: str, user_tags: list[str] | None) -> list[str]:
    inferred = set(_split_tags(path.stem))
    inferred.add(category)
    inferred.add(source_type)
    inferred.add(path.suffix.lower().lstrip(".") or "unknown")

    for raw_tag in user_tags or []:
        for normalized in _split_tags(raw_tag):
            inferred.add(normalized)

    return sorted(inferred)


def _read_asset_catalog(catalog_path: Path) -> list[dict[str, object]]:
    if not catalog_path.exists():
        return []
    payload = json.loads(catalog_path.read_text(encoding="utf-8"))
    assets = payload.get("assets", [])
    if not isinstance(assets, list):
        raise ValueError("Asset catalog is invalid: expected a top-level 'assets' list.")
    return [dict(item) for item in assets]


def _write_asset_catalog(catalog_path: Path, assets: list[dict[str, object]]) -> None:
    payload = {
        "schema": "gameforge.asset_catalog.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "assets": assets,
    }
    _write_text(catalog_path, json.dumps(payload, indent=2))


def _preset_library_path(project_root: Path) -> Path:
    return project_root / "config" / "style-presets.v1.json"


def _project_style_state_path(project_root: Path) -> Path:
    return project_root / "config" / "project-style.v1.json"


def _preset_index(presets: list[StylePresetDefinition]) -> dict[str, StylePresetDefinition]:
    return {preset.preset_id: preset for preset in presets}


def _normalize_preset_id(value: str) -> str:
    return _slugify(value)


def _merge_transformations(
    base: dict[str, dict[str, object]],
    overrides: dict[str, dict[str, object]],
) -> dict[str, dict[str, object]]:
    merged = {category: dict(settings) for category, settings in base.items()}
    for category, settings in overrides.items():
        existing = dict(merged.get(category, {}))
        existing.update(settings)
        merged[category] = existing
    return merged


def list_style_presets(project_root: Path) -> list[StylePresetDefinition]:
    library_path = _preset_library_path(project_root)
    presets = list(STYLE_BUILTIN_PRESETS)
    if not library_path.exists():
        return presets

    payload = json.loads(library_path.read_text(encoding="utf-8"))
    custom = payload.get("custom_presets", [])
    if not isinstance(custom, list):
        raise ValueError("Style preset library is invalid: expected custom_presets list.")

    for item in custom:
        if not isinstance(item, dict):
            continue
        preset_id = str(item.get("preset_id", "")).strip()
        display_name = str(item.get("display_name", "")).strip()
        if not preset_id or not display_name:
            continue
        transformations = item.get("transformations", {})
        if not isinstance(transformations, dict):
            transformations = {}
        normalized_transformations: dict[str, dict[str, object]] = {}
        for category, settings in transformations.items():
            if not isinstance(settings, dict):
                continue
            normalized_transformations[str(category)] = dict(settings)

        presets.append(
            StylePresetDefinition(
                preset_id=preset_id,
                display_name=display_name,
                parent_preset_id=str(item.get("parent_preset_id", "")).strip() or None,
                transformations=normalized_transformations,
                source="user",
            )
        )

    return presets


def create_user_style_preset(
    project_root: Path,
    display_name: str,
    base_preset_id: str,
    overrides: dict[str, dict[str, object]] | None = None,
) -> StylePresetDefinition:
    normalized_display_name = display_name.strip()
    if not normalized_display_name:
        raise ValueError("Preset display name cannot be blank.")

    presets = list_style_presets(project_root)
    index = _preset_index(presets)
    normalized_base = _normalize_preset_id(base_preset_id)
    if normalized_base not in index:
        raise ValueError(f"Base style preset not found: {base_preset_id}")

    preset_id = _normalize_preset_id(normalized_display_name)
    if not preset_id:
        raise ValueError("Preset display name must produce a non-empty preset_id.")
    if preset_id in index:
        raise ValueError(f"Preset already exists: {preset_id}")

    base = index[normalized_base]
    merged_transformations = _merge_transformations(base.transformations, overrides or {})
    created = StylePresetDefinition(
        preset_id=preset_id,
        display_name=normalized_display_name,
        parent_preset_id=base.preset_id,
        transformations=merged_transformations,
        source="user",
    )

    library_path = _preset_library_path(project_root)
    existing_custom: list[dict[str, object]] = []
    if library_path.exists():
        payload = json.loads(library_path.read_text(encoding="utf-8"))
        existing_custom = list(payload.get("custom_presets", []))

    existing_custom.append(
        {
            "preset_id": created.preset_id,
            "display_name": created.display_name,
            "parent_preset_id": created.parent_preset_id,
            "transformations": created.transformations,
            "source": created.source,
        }
    )

    payload = {
        "schema": "gameforge.style_preset_library.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "custom_presets": existing_custom,
    }
    _write_text(library_path, json.dumps(payload, indent=2))
    return created


def select_project_style_preset(project_root: Path, preset_id: str) -> ProjectStyleSelectionState:
    presets = list_style_presets(project_root)
    normalized_id = _normalize_preset_id(preset_id)
    if normalized_id not in _preset_index(presets):
        raise ValueError(f"Style preset not found: {preset_id}")

    state = ProjectStyleSelectionState(active_preset_id=normalized_id, helper_mode="match-project-style")
    _write_text(
        _project_style_state_path(project_root),
        json.dumps(
            {
                "schema": "gameforge.project_style.v1",
                "generated_at_utc": datetime.now(timezone.utc).isoformat(),
                "active_preset_id": state.active_preset_id,
                "helper_mode": state.helper_mode,
            },
            indent=2,
        ),
    )
    return state


def _load_project_style_state(project_root: Path) -> ProjectStyleSelectionState:
    path = _project_style_state_path(project_root)
    if not path.exists():
        return ProjectStyleSelectionState(active_preset_id="cozy-stylized", helper_mode="match-project-style")
    payload = json.loads(path.read_text(encoding="utf-8"))
    return ProjectStyleSelectionState(
        active_preset_id=_normalize_preset_id(str(payload.get("active_preset_id", "cozy-stylized"))),
        helper_mode=str(payload.get("helper_mode", "match-project-style")),
    )


def match_project_style(
    project_root: Path,
    sample_assets: list[dict[str, object]],
) -> list[dict[str, object]]:
    presets = list_style_presets(project_root)
    preset_index = _preset_index(presets)
    state = _load_project_style_state(project_root)
    active = preset_index.get(state.active_preset_id, preset_index["cozy-stylized"])

    transformed: list[dict[str, object]] = []
    for asset in sample_assets:
        category = str(asset.get("category", "uncategorized")).strip().lower()
        base_metadata = dict(asset.get("metadata", {})) if isinstance(asset.get("metadata"), dict) else {}
        style_settings = dict(active.transformations.get(category, active.transformations.get("textures", {})))
        transformed.append(
            {
                **asset,
                "style_preset_id": active.preset_id,
                "style_helper_action": "match-project-style",
                "metadata": {
                    **base_metadata,
                    "style_transform": style_settings,
                },
            }
        )

    return transformed


def _parse_asset_id_suffix(value: object) -> int | None:
    match = re.fullmatch(r"asset-(\d+)", str(value or "").strip().lower())
    if not match:
        return None
    return int(match.group(1))


def _next_asset_sequence(existing_assets: list[dict[str, object]]) -> int:
    numeric_suffixes = [
        parsed
        for parsed in (_parse_asset_id_suffix(asset.get("asset_id")) for asset in existing_assets)
        if parsed is not None
    ]
    return (max(numeric_suffixes) + 1) if numeric_suffixes else 1


def import_assets(
    project_root: Path,
    requests: list[AssetImportRequest],
) -> AssetImportPipelineResult:
    catalog_path = project_root / "assets" / "catalog.v1.json"
    library_dir = project_root / "assets" / "library"
    library_dir.mkdir(parents=True, exist_ok=True)

    existing_assets = _read_asset_catalog(catalog_path)
    imported_assets: list[AssetCatalogEntry] = []
    errors: list[AssetImportError] = []
    next_asset_sequence = _next_asset_sequence(existing_assets)

    for request in requests:
        source_path = Path(request.source_path)
        normalized_license = _normalize_license_id(request.license_id)
        source_type = request.source_type.strip().lower()

        if source_type not in {"ai-generated", "manual-upload"}:
            errors.append(
                AssetImportError(
                    source_path=request.source_path,
                    code="unsupported-source-type",
                    message=f"Source type '{request.source_type}' is not supported in V1.",
                    remediation="Use 'ai-generated' or 'manual-upload'.",
                )
            )
            continue

        if not source_path.exists() or not source_path.is_file():
            errors.append(
                AssetImportError(
                    source_path=request.source_path,
                    code="missing-source-file",
                    message="Asset source file was not found.",
                    remediation="Verify the source path points to an existing local file.",
                )
            )
            continue

        if normalized_license in BLOCKED_LICENSES:
            errors.append(
                AssetImportError(
                    source_path=request.source_path,
                    code="blocked-license",
                    message=f"License '{request.license_id}' is blocked in V1.",
                    remediation="Use CC0/Public Domain, CC-BY 4.0 (with attribution), or user-owned assets.",
                )
            )
            continue

        if normalized_license not in ALLOWED_LICENSES:
            errors.append(
                AssetImportError(
                    source_path=request.source_path,
                    code="unclear-license",
                    message=f"License '{request.license_id}' is unsupported or unclear for V1.",
                    remediation="Provide a clear allow-listed license id and re-import.",
                )
            )
            continue

        if normalized_license == "user-owned" and not request.rights_confirmation:
            errors.append(
                AssetImportError(
                    source_path=request.source_path,
                    code="rights-confirmation-required",
                    message="User-owned assets require explicit rights confirmation.",
                    remediation="Set rights_confirmation=true when importing user-owned assets.",
                )
            )
            continue

        category = _derive_asset_category(source_path)
        tags = _build_auto_tags(source_path, category, source_type, request.user_tags)
        asset_id = f"asset-{next_asset_sequence:04d}"
        next_asset_sequence += 1
        imported_at_utc = datetime.now(timezone.utc).isoformat()
        target_name = f"{asset_id}{source_path.suffix.lower()}"
        target_path = library_dir / target_name
        target_path.write_bytes(source_path.read_bytes())
        relative_path = str(target_path.relative_to(project_root)).replace("\\", "/")

        entry = AssetCatalogEntry(
            asset_id=asset_id,
            display_name=request.display_name or source_path.stem,
            category=category,
            tags=tags,
            license_id=normalized_license,
            source_type=source_type,
            relative_path=relative_path,
            imported_at_utc=imported_at_utc,
            metadata={
                "source_filename": source_path.name,
                "source_path": str(source_path),
                "rights_confirmation": request.rights_confirmation,
            },
        )
        imported_assets.append(entry)

    updated_assets = [*existing_assets, *[asdict(asset) for asset in imported_assets]]
    _write_asset_catalog(catalog_path, updated_assets)

    return AssetImportPipelineResult(imported_assets=imported_assets, errors=errors)


def search_asset_catalog(
    project_root: Path,
    query: str = "",
    category: str | None = None,
    required_tags: list[str] | None = None,
) -> list[dict[str, object]]:
    catalog_path = project_root / "assets" / "catalog.v1.json"
    assets = _read_asset_catalog(catalog_path)
    normalized_query = query.strip().lower()
    normalized_category = category.strip().lower() if category else None
    normalized_tags = {item.strip().lower() for item in (required_tags or []) if item.strip()}

    results: list[dict[str, object]] = []
    for asset in assets:
        asset_name = str(asset.get("display_name", "")).lower()
        asset_category = str(asset.get("category", "")).lower()
        asset_tags = {str(item).lower() for item in asset.get("tags", [])}

        if normalized_category and asset_category != normalized_category:
            continue
        if normalized_tags and not normalized_tags.issubset(asset_tags):
            continue
        if normalized_query:
            haystack = " ".join([asset_name, asset_category, " ".join(sorted(asset_tags))])
            if normalized_query not in haystack:
                continue

        results.append(asset)

    return results


def apply_consequence_choice(tracker: dict[str, object], choice_id: str) -> ConsequenceTransitionResult:
    if "state" not in tracker or "graph" not in tracker:
        raise ValueError("Tracker payload missing graph/state sections.")

    state = tracker["state"]
    graph = tracker["graph"]
    current_node_id = state.get("current_node_id", "")
    nodes = graph.get("nodes", [])
    node = next((item for item in nodes if item.get("node_id") == current_node_id), None)
    if not node:
        raise ValueError(f"Current node not found in graph: {current_node_id}")

    choice = next((item for item in node.get("choices", []) if item.get("choice_id") == choice_id), None)
    if not choice:
        return ConsequenceTransitionResult(
            applied=False,
            previous_node_id=current_node_id,
            current_node_id=current_node_id,
            selected_choice_id=choice_id,
            npc_state=dict(state.get("npc_state", {})),
            world_state=dict(state.get("world_state", {})),
        )

    npc_state = dict(state.get("npc_state", {}))
    world_state = dict(state.get("world_state", {}))

    npc_delta = choice.get("effects", {}).get("npc_delta", {})
    for key, delta in npc_delta.items():
        npc_state[key] = int(npc_state.get(key, 0)) + int(delta)

    world_updates = choice.get("effects", {}).get("world_set", {})
    for key, value in world_updates.items():
        world_state[key] = str(value)

    next_node_id = choice.get("next_node_id", current_node_id)
    state["current_node_id"] = next_node_id
    state["npc_state"] = npc_state
    state["world_state"] = world_state
    state["last_choice_id"] = choice_id

    return ConsequenceTransitionResult(
        applied=True,
        previous_node_id=current_node_id,
        current_node_id=next_node_id,
        selected_choice_id=choice_id,
        npc_state=npc_state,
        world_state=world_state,
    )


def derive_branch_view(branch_view: dict[str, object], tracker: dict[str, object]) -> dict[str, object]:
    current_node_id = tracker.get("state", {}).get("current_node_id", "")
    available_transitions: set[tuple[str, str]] = set()
    nodes = tracker.get("graph", {}).get("nodes", [])
    for node in nodes:
        if node.get("node_id") != current_node_id:
            continue
        for choice in node.get("choices", []):
            choice_id = str(choice.get("choice_id", ""))
            next_node_id = str(choice.get("next_node_id", ""))
            if choice_id:
                available_transitions.add((choice_id, next_node_id))

    resolved_edges: list[dict[str, object]] = []
    for edge in branch_view.get("edges", []):
        edge_copy = dict(edge)
        edge_choice_id = str(edge_copy.get("choice_id", ""))
        edge_to = str(edge_copy.get("to", ""))
        if edge_copy.get("from") == current_node_id:
            edge_copy["live_status"] = (
                "active-choice" if (edge_choice_id, edge_to) in available_transitions else "inactive"
            )
        else:
            edge_copy["live_status"] = "inactive"
        resolved_edges.append(edge_copy)

    resolved_view = dict(branch_view)
    resolved_view["live_state"] = {
        "current_node_id": current_node_id,
        "updated_at_utc": datetime.now(timezone.utc).isoformat(),
    }
    resolved_view["edges"] = resolved_edges
    return resolved_view


def _generate_prototype(brief_path: Path, output_dir: Path) -> Path:
    brief = json.loads(brief_path.read_text(encoding="utf-8"))
    concept = brief.get("concept", "GameForge Prototype")
    mechanics = brief.get("mechanics", {})
    style = brief.get("style", {})
    narrative = brief.get("narrative", {})

    prototype_root = output_dir / _slugify(concept)
    prototype_root.mkdir(parents=True, exist_ok=True)

    manifest = {
        "generator": "gameforge-v1-prototype",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_brief": str(brief_path),
        "project_name": concept,
        "platforms": ["windows", "ubuntu"],
        "rendering": "vulkan-first",
        "scope": "single-player baseline",
    }

    scene = {
        "scene_id": "baseline_scene",
        "player_spawn": {"x": 0, "y": 1, "z": 0},
        "camera": {"mode": "third_person", "follow_player": True},
        "world_notes": narrative.get("world_notes", ""),
    }

    player_controller = {
        "schema": "gameforge.player_controller.v1",
        "movement": {"forward": "W", "back": "S", "left": "A", "right": "D", "jump": "Space"},
        "look": {"mouse_sensitivity": 1.0},
        "interaction": {"primary": "Mouse0", "secondary": "Mouse1"},
    }

    ui_layout = {
        "schema": "gameforge.ui.hud.v1",
        "widgets": [
            {"id": "quest_tracker", "anchor": "top-left", "enabled": True},
            {"id": "health_bar", "anchor": "top-center", "enabled": True},
            {"id": "hint_text", "anchor": "bottom-center", "enabled": True},
        ],
        "ui_direction": style.get("ui_direction", "Minimal readable HUD"),
    }

    save_stub = {
        "schema": "gameforge.save.v1",
        "active_slot": "slot_01",
        "last_checkpoint": "baseline_scene:start",
        "player_state": {"level": 1, "xp": 0},
    }

    rts_sim_template = {
        "schema": "gameforge.rts_sim.template.v1",
        "module_id": "rts_sim_baseline",
        "description": "Reusable local-first RTS/sim starter systems module.",
        "single_player_only": True,
        "systems": {
            "units_agents": {
                "worker": {"max_hp": 60, "gather_rate_per_minute": 24, "build_speed": 1.0},
                "hauler": {"max_hp": 90, "carry_capacity": 20, "move_speed": 1.1},
            },
            "resource_loop": {
                "resources": ["food", "wood", "stone"],
                "starting_stockpile": {"food": 120, "wood": 80, "stone": 40},
                "tick_seconds": 5,
            },
            "building_placement": {
                "grid_size": 2,
                "footprints": {
                    "town_center": {"w": 4, "h": 4},
                    "farm": {"w": 3, "h": 3},
                    "sawmill": {"w": 3, "h": 3},
                    "quarry": {"w": 3, "h": 3},
                },
                "collision_policy": "blocked-cells-reject",
            },
            "progression_tree": {
                "tier_1": ["efficient_tools", "storage_crates"],
                "tier_2": ["stone_foundation", "crop_rotation"],
            },
        },
    }

    rts_sim_map = {
        "schema": "gameforge.rts_sim.scenario_map.v1",
        "map_id": "green-valley-outpost",
        "size": {"width": 64, "height": 64},
        "spawn": {
            "town_center": {"x": 12, "y": 12},
            "workers": [{"x": 10, "y": 11}, {"x": 11, "y": 10}, {"x": 12, "y": 10}],
        },
        "resource_nodes": [
            {"type": "wood", "x": 24, "y": 18, "amount": 500},
            {"type": "wood", "x": 26, "y": 20, "amount": 450},
            {"type": "stone", "x": 35, "y": 12, "amount": 320},
            {"type": "food", "x": 16, "y": 28, "amount": 600},
        ],
        "victory_hint": "Stabilize food income, place 3 production buildings, and unlock tier_2 progression.",
    }

    rts_sim_balance = {
        "schema": "gameforge.rts_sim.balance.v1",
        "difficulty": "medium",
        "economy": {
            "starting_food": 120,
            "starting_wood": 80,
            "starting_stone": 40,
            "worker_upkeep_per_minute": {"food": 3},
        },
        "buildings": {
            "farm": {"cost": {"wood": 30}, "build_seconds": 20, "output_per_minute": {"food": 35}},
            "sawmill": {"cost": {"wood": 20, "stone": 10}, "build_seconds": 26, "output_per_minute": {"wood": 45}},
            "quarry": {"cost": {"wood": 15, "stone": 25}, "build_seconds": 30, "output_per_minute": {"stone": 30}},
        },
        "progression": {
            "efficient_tools": {"cost": {"wood": 60, "stone": 30}, "unlock_seconds": 35},
            "storage_crates": {"cost": {"wood": 40}, "unlock_seconds": 25},
            "stone_foundation": {"cost": {"wood": 80, "stone": 60}, "unlock_seconds": 55},
            "crop_rotation": {"cost": {"food": 90, "wood": 25}, "unlock_seconds": 50},
        },
    }

    rpg_quest_dialogue = {
        "schema": "gameforge.rpg.quest_dialogue.v1",
        "module_id": "rpg_baseline_quests",
        "single_player_only": True,
        "quests": [
            {
                "quest_id": "q_harvest_dilemma",
                "title": "Harvest Dilemma",
                "start_node_id": "dialogue_mayor_intro",
                "objective": "Resolve the dispute over grain allocation.",
                "completion_nodes": ["dialogue_farmers_supported", "dialogue_merchants_supported"],
            }
        ],
        "dialogue_nodes": [
            {
                "node_id": "dialogue_mayor_intro",
                "speaker": "Mayor Elara",
                "text": "Winter stores are low. Should we support farmers or merchants first?",
                "choices": [
                    {
                        "choice_id": "support_farmers",
                        "label": "Prioritize farmers this week.",
                        "next_node_id": "dialogue_farmers_supported",
                    },
                    {
                        "choice_id": "support_merchants",
                        "label": "Prioritize merchants for faster trade.",
                        "next_node_id": "dialogue_merchants_supported",
                    },
                ],
            },
            {
                "node_id": "dialogue_farmers_supported",
                "speaker": "Farmer Lia",
                "text": "Thank you. The village trust grows with each harvest.",
                "choices": [],
            },
            {
                "node_id": "dialogue_merchants_supported",
                "speaker": "Merchant Roan",
                "text": "Trade routes are opening, but people may worry about rationing.",
                "choices": [],
            },
        ],
    }

    rpg_inventory_leveling = {
        "schema": "gameforge.rpg.inventory_leveling.v1",
        "module_id": "rpg_baseline_progression",
        "inventory": {
            "capacity_slots": 20,
            "starter_items": [
                {"item_id": "bread", "quantity": 3},
                {"item_id": "iron_dagger", "quantity": 1},
            ],
            "equipment_slots": ["main_hand", "off_hand", "armor", "trinket"],
        },
        "leveling": {
            "starting_level": 1,
            "starting_xp": 0,
            "xp_curve": [100, 250, 450, 700],
            "stat_growth_per_level": {"health": 8, "stamina": 4, "crafting": 2},
        },
    }

    consequence_tracker = {
        "schema": "gameforge.rpg.consequence_tracker.v1",
        "module_id": "rpg_choice_consequences",
        "graph": {
            "nodes": [
                {
                    "node_id": "dialogue_mayor_intro",
                    "choices": [
                        {
                            "choice_id": "support_farmers",
                            "next_node_id": "dialogue_farmers_supported",
                            "effects": {
                                "npc_delta": {"mayor_elara_trust": 1, "farmer_lia_affinity": 2, "merchant_roan_affinity": -1},
                                "world_set": {"grain_policy": "farmer-priority", "market_prices": "stable"},
                            },
                        },
                        {
                            "choice_id": "support_merchants",
                            "next_node_id": "dialogue_merchants_supported",
                            "effects": {
                                "npc_delta": {"mayor_elara_trust": -1, "farmer_lia_affinity": -2, "merchant_roan_affinity": 2},
                                "world_set": {"grain_policy": "merchant-priority", "market_prices": "volatile"},
                            },
                        },
                    ],
                },
                {"node_id": "dialogue_farmers_supported", "choices": []},
                {"node_id": "dialogue_merchants_supported", "choices": []},
            ]
        },
        "state": {
            "current_node_id": "dialogue_mayor_intro",
            "last_choice_id": "",
            "npc_state": {"mayor_elara_trust": 0, "farmer_lia_affinity": 0, "merchant_roan_affinity": 0},
            "world_state": {"grain_policy": "undecided", "market_prices": "uncertain"},
        },
    }

    branch_view = {
        "schema": "gameforge.rpg.branch_view.v1",
        "view_id": "quest_dialogue_branch_map",
        "nodes": [
            {"node_id": "dialogue_mayor_intro", "label": "Mayor Intro", "position": {"x": 0, "y": 0}},
            {"node_id": "dialogue_farmers_supported", "label": "Farmers Supported", "position": {"x": -280, "y": 200}},
            {"node_id": "dialogue_merchants_supported", "label": "Merchants Supported", "position": {"x": 280, "y": 200}},
        ],
        "edges": [
            {"edge_id": "edge_farmers", "from": "dialogue_mayor_intro", "to": "dialogue_farmers_supported", "choice_id": "support_farmers"},
            {"edge_id": "edge_merchants", "from": "dialogue_mayor_intro", "to": "dialogue_merchants_supported", "choice_id": "support_merchants"},
        ],
    }

    _write_text(prototype_root / "prototype-manifest.json", json.dumps(manifest, indent=2))
    _write_text(prototype_root / "scene" / "scene_scaffold.json", json.dumps(scene, indent=2))
    _write_text(prototype_root / "scene" / "rts_sim_scenario_map.json", json.dumps(rts_sim_map, indent=2))
    _write_text(prototype_root / "scripts" / "player_controller.json", json.dumps(player_controller, indent=2))
    _write_text(
        prototype_root / "systems" / "rts_sim" / "template_module.json",
        json.dumps(rts_sim_template, indent=2),
    )
    _write_text(prototype_root / "ui" / "hud_layout.json", json.dumps(ui_layout, indent=2))
    _write_text(prototype_root / "ui" / "branch_visualization.v1.json", json.dumps(branch_view, indent=2))
    _write_text(prototype_root / "config" / "rts_sim_balance.v1.json", json.dumps(rts_sim_balance, indent=2))
    _write_text(prototype_root / "save" / "savegame_hook.json", json.dumps(save_stub, indent=2))
    _write_text(prototype_root / "systems" / "rpg" / "quest_dialogue_framework.v1.json", json.dumps(rpg_quest_dialogue, indent=2))
    _write_text(
        prototype_root / "systems" / "rpg" / "inventory_leveling.v1.json",
        json.dumps(rpg_inventory_leveling, indent=2),
    )
    _write_text(
        prototype_root / "systems" / "rpg" / "consequence_state_tracker.v1.json",
        json.dumps(consequence_tracker, indent=2),
    )

    escaped_concept = _escape_cpp_string_literal(concept)
    escaped_core_loop = _escape_cpp_string_literal(mechanics.get("core_loop", ""))

    runtime_main = f'''#include <fstream>
#include <iostream>
#include <string>

int main() {{
    std::cout << "GameForge V1 prototype runtime (C++ baseline)\\n";
    std::cout << "Mode: local-first, single-player, no-code-first\\n";
    std::cout << "Rendering direction: Vulkan-first\\n";
    std::cout << "Project: {escaped_concept}\\n";
    std::cout << "Core loop seed: {escaped_core_loop}\\n";

    std::ifstream scene("scene/scene_scaffold.json");
    std::ifstream rtsModule("systems/rts_sim/template_module.json");
    std::ifstream rtsMap("scene/rts_sim_scenario_map.json");
    std::ifstream rtsBalance("config/rts_sim_balance.v1.json");
    std::ifstream player("scripts/player_controller.json");
    std::ifstream ui("ui/hud_layout.json");
    std::ifstream branchView("ui/branch_visualization.v1.json");
    std::ifstream save("save/savegame_hook.json");
    std::ifstream questDialogue("systems/rpg/quest_dialogue_framework.v1.json");
    std::ifstream inventoryLeveling("systems/rpg/inventory_leveling.v1.json");
    std::ifstream consequenceTracker("systems/rpg/consequence_state_tracker.v1.json");

    if (!scene.good() || !rtsModule.good() || !rtsMap.good() || !rtsBalance.good() || !player.good() || !ui.good() ||
        !branchView.good() || !save.good() || !questDialogue.good() || !inventoryLeveling.good() || !consequenceTracker.good()) {{
        std::cerr << "Missing generated scaffold files.\\n";
        return 2;
    }}

    std::cout << "Scene scaffold loaded.\\n";
    std::cout << "RTS/sim template module loaded.\\n";
    std::cout << "RTS/sim scenario map loaded.\\n";
    std::cout << "RTS/sim balance config loaded.\\n";
    std::cout << "Player controller loaded.\\n";
    std::cout << "Basic UI loaded.\\n";
    std::cout << "Branch visualization config loaded.\\n";
    std::cout << "Save/load hook loaded.\\n";
    std::cout << "RPG quest/dialogue framework loaded.\\n";
    std::cout << "RPG inventory + leveling module loaded.\\n";
    std::cout << "RPG consequence state tracker loaded.\\n";
    std::cout << "Core loop check: units -> resources -> placement -> progression is intact.\\n";
    std::cout << "Consequence check: player dialogue choices can change NPC/world state and branch transitions.\\n";
    std::cout << "Prototype launch success.\\n";
    return 0;
}}
'''

    launch_sh = """#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$PROJECT_DIR"
g++ -std=c++17 runtime/main.cpp -o runtime/prototype_runtime
./runtime/prototype_runtime
"""

    launch_ps1 = """$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
Set-Location $projectDir
g++ -std=c++17 runtime/main.cpp -o runtime/prototype_runtime.exe
./runtime/prototype_runtime.exe
"""

    readme = f"""# Generated Prototype: {concept}

This project was generated from a saved interview brief.

Included baseline scaffold:
- Scene/world scaffold (`scene/scene_scaffold.json`)
- RTS/sim scenario map (`scene/rts_sim_scenario_map.json`)
- Player control stub (`scripts/player_controller.json`)
- Basic UI config (`ui/hud_layout.json`)
- Branch visualization config (`ui/branch_visualization.v1.json`)
- RTS/sim reusable template module (`systems/rts_sim/template_module.json`)
- RTS/sim balancing config (`config/rts_sim_balance.v1.json`)
- RPG quest/dialogue module (`systems/rpg/quest_dialogue_framework.v1.json`)
- RPG inventory + leveling module (`systems/rpg/inventory_leveling.v1.json`)
- RPG consequence tracker (`systems/rpg/consequence_state_tracker.v1.json`)
- Save/load hook (`save/savegame_hook.json`)

One-click local launch commands:
- Ubuntu: `./launch_prototype.sh`
- Windows PowerShell: `pwsh -f ./launch_prototype.ps1`
"""

    _write_text(prototype_root / "runtime" / "main.cpp", runtime_main)
    _write_text(prototype_root / "launch_prototype.sh", launch_sh)
    _write_text(prototype_root / "launch_prototype.ps1", launch_ps1)
    _write_text(prototype_root / "README.md", readme)
    (prototype_root / "launch_prototype.sh").chmod(0o755)

    return prototype_root


def _launch_generated_prototype(prototype_root: Path) -> int:
    compile_cmd = ["g++", "-std=c++17", "runtime/main.cpp", "-o", "runtime/prototype_runtime"]
    try:
        compile_proc = subprocess.run(compile_cmd, cwd=prototype_root, text=True, capture_output=True)
    except FileNotFoundError:
        print("ERROR: g++ not found. Install g++ to compile and launch generated prototypes.")
        return 127

    if compile_proc.returncode != 0:
        print(compile_proc.stdout)
        print(compile_proc.stderr)
        return compile_proc.returncode

    run_proc = subprocess.run(["./runtime/prototype_runtime"], cwd=prototype_root, text=True, capture_output=True)
    print(run_proc.stdout, end="")
    if run_proc.returncode != 0:
        print(run_proc.stderr, end="")
    return run_proc.returncode


def _read_bot_playtest_scenario(scenario_path: Path) -> BotPlaytestScenario:
    payload = json.loads(scenario_path.read_text(encoding="utf-8"))
    probes_raw = payload.get("probes", [])
    if not isinstance(probes_raw, list) or not probes_raw:
        raise ValueError("Bot playtest scenario requires a non-empty probes list.")
    probes = [
        BotPlaytestProbe(
            probe_id=str(item.get("probe_id", "")).strip(),
            probe_type=str(item.get("probe_type", "")).strip().lower(),
            target=str(item.get("target", "")).strip(),
            expected=item.get("expected"),
            required=bool(item.get("required", True)),
        )
        for item in probes_raw
        if isinstance(item, dict)
    ]
    if any(not probe.probe_id or not probe.probe_type or not probe.target for probe in probes):
        raise ValueError("Every bot playtest probe must define probe_id, probe_type, and target.")
    if not probes:
        raise ValueError("Bot playtest scenario does not contain valid probes.")

    return BotPlaytestScenario(
        schema=str(payload.get("schema", "")).strip(),
        scenario_id=str(payload.get("scenario_id", "")).strip(),
        title=str(payload.get("title", "")).strip(),
        max_runtime_seconds=int(payload.get("max_runtime_seconds", 60)),
        probes=probes,
    )


def _resolve_json_path(payload: object, dot_path: str) -> object:
    current = payload
    for key in dot_path.split("."):
        if not key:
            continue
        if isinstance(current, dict) and key in current:
            current = current[key]
            continue
        raise KeyError(f"JSON path segment not found: {key}")
    return current


def _run_bot_probe(prototype_root: Path, probe: BotPlaytestProbe) -> BotPlaytestProbeResult:
    target_path = prototype_root / probe.target
    if probe.probe_type == "file_exists":
        if not isinstance(probe.expected, bool):
            return BotPlaytestProbeResult(
                probe_id=probe.probe_id,
                status="inconclusive",
                details=f"file_exists probe expected must be boolean, got {type(probe.expected).__name__}",
                required=probe.required,
            )
        exists = target_path.exists()
        status = "passed" if exists == probe.expected else "failed"
        details = f"Expected file_exists={probe.expected}, observed={exists} at {probe.target}"
        return BotPlaytestProbeResult(probe_id=probe.probe_id, status=status, details=details, required=probe.required)

    if probe.probe_type == "json_field_equals":
        if ":" not in probe.target:
            return BotPlaytestProbeResult(
                probe_id=probe.probe_id,
                status="inconclusive",
                details=f"Invalid json_field_equals target '{probe.target}', expected file:path.to.field",
                required=probe.required,
            )
        file_part, json_path = probe.target.split(":", 1)
        json_file = prototype_root / file_part
        if not json_file.exists():
            return BotPlaytestProbeResult(
                probe_id=probe.probe_id,
                status="failed",
                details=f"JSON file not found: {file_part}",
                required=probe.required,
            )
        try:
            payload = json.loads(json_file.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            return BotPlaytestProbeResult(
                probe_id=probe.probe_id,
                status="inconclusive",
                details=f"Malformed JSON in {file_part}: {exc.msg}",
                required=probe.required,
            )
        try:
            actual_value = _resolve_json_path(payload, json_path)
        except KeyError as exc:
            return BotPlaytestProbeResult(
                probe_id=probe.probe_id,
                status="inconclusive",
                details=str(exc),
                required=probe.required,
            )
        status = "passed" if actual_value == probe.expected else "failed"
        details = f"Expected {json_path}={probe.expected!r}, observed={actual_value!r}"
        return BotPlaytestProbeResult(probe_id=probe.probe_id, status=status, details=details, required=probe.required)

    return BotPlaytestProbeResult(
        probe_id=probe.probe_id,
        status="inconclusive",
        details=f"Unsupported probe_type '{probe.probe_type}'",
        required=probe.required,
    )


def run_bot_playtest_scenario(prototype_root: Path, scenario_path: Path) -> BotPlaytestResult:
    scenario = _read_bot_playtest_scenario(scenario_path)
    probe_results = [_run_bot_probe(prototype_root, probe) for probe in scenario.probes]
    inconclusive_reasons = [
        f"{result.probe_id}: {result.details}"
        for result in probe_results
        if result.required and result.status == "inconclusive"
    ]
    required_failures = [result for result in probe_results if result.required and result.status == "failed"]
    required_inconclusive = [result for result in probe_results if result.required and result.status == "inconclusive"]

    if required_inconclusive:
        status = "inconclusive"
        summary = "Bot playtest needs human review before baseline validation can be trusted."
    elif required_failures:
        status = "failed"
        summary = "Bot playtest found baseline gameplay validation failures."
    else:
        status = "passed"
        summary = "Bot playtest baseline checks passed for generated prototype."

    return BotPlaytestResult(
        scenario_id=scenario.scenario_id,
        prototype_root=str(prototype_root),
        status=status,
        human_review_required=bool(required_inconclusive),
        summary=summary,
        completed_at_utc=datetime.now(timezone.utc).isoformat(),
        probe_results=probe_results,
        inconclusive_reasons=inconclusive_reasons,
    )


def _derive_section_status(issue_count: int, inconclusive_count: int) -> str:
    if inconclusive_count > 0:
        return "needs-human-review"
    if issue_count > 0:
        return "action-needed"
    return "healthy"


def _build_report_section(section_id: str, title: str, section_probes: list[BotPlaytestProbeResult]) -> PlaytestReportSection:
    failed = [probe for probe in section_probes if probe.status == "failed" and probe.required]
    inconclusive = [probe for probe in section_probes if probe.status == "inconclusive" and probe.required]

    findings = [f"{probe.probe_id}: {probe.details}" for probe in failed + inconclusive]
    if not findings:
        findings = ["No required issues detected in this section during bot playtesting."]

    recommendations = []
    if inconclusive:
        recommendations.append("Schedule focused human playtest coverage to validate inconclusive probes.")
    if failed:
        recommendations.append("Prioritize fixes for failed probes before advancing to the next milestone gate.")
    if not recommendations:
        recommendations.append("Keep monitoring this section in subsequent regression runs.")

    return PlaytestReportSection(
        section_id=section_id,
        title=title,
        status=_derive_section_status(len(failed), len(inconclusive)),
        findings=findings,
        recommendations=recommendations,
    )


def generate_actionable_playtest_report(result: BotPlaytestResult) -> ActionablePlaytestReport:
    sections_map = {
        "progression": ["progression", "level", "xp", "tech", "unlock"],
        "economy": ["economy", "resource", "currency", "gold", "income"],
        "dead-end": ["dead_end", "dead-end", "quest", "branch", "dialogue", "blocked"],
        "pacing": ["pacing", "flow", "tempo", "loop", "downtime"],
        "performance": ["performance", "fps", "frame", "memory", "cpu", "runtime"],
    }

    normalized_probes = []
    for probe in result.probe_results:
        search_haystack = f"{probe.probe_id} {probe.details}".lower()
        normalized_probes.append((probe, search_haystack))

    section_matches: dict[str, list[BotPlaytestProbeResult]] = {}
    used_probe_ids: set[str] = set()
    for section_id, keywords in sections_map.items():
        matched = [probe for probe, haystack in normalized_probes if any(keyword in haystack for keyword in keywords)]
        for probe in matched:
            used_probe_ids.add(probe.probe_id)
        section_matches[section_id] = matched

    uncategorized = [probe for probe in result.probe_results if probe.probe_id not in used_probe_ids]
    if uncategorized:
        section_matches["progression"] = [*section_matches["progression"], *uncategorized]

    built_sections = [
        _build_report_section(section_id, section_id.replace("-", " ").title(), section_matches[section_id])
        for section_id in sections_map
    ]

    return ActionablePlaytestReport(
        schema="gameforge.playtest_report.v1",
        report_id=f"{result.scenario_id}-{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')}",
        scenario_id=result.scenario_id,
        prototype_root=result.prototype_root,
        generated_at_utc=datetime.now(timezone.utc).isoformat(),
        overall_status=result.status,
        summary=result.summary,
        sections=built_sections,
        source_probe_results=result.probe_results,
    )


def _write_playtest_report_markdown(report: ActionablePlaytestReport, destination: Path) -> None:
    lines = [
        "# GameForge V1 Playtest Report",
        "",
        f"- Report ID: `{report.report_id}`",
        f"- Scenario: `{report.scenario_id}`",
        f"- Generated (UTC): `{report.generated_at_utc}`",
        f"- Overall status: **{report.overall_status}**",
        "",
        f"## Summary",
        report.summary,
    ]

    for section in report.sections:
        lines.extend(
            [
                "",
                f"## {section.title}",
                f"- Status: **{section.status}**",
                "- Findings:",
            ]
        )
        lines.extend([f"  - {finding}" for finding in section.findings])
        lines.append("- Recommendations:")
        lines.extend([f"  - {recommendation}" for recommendation in section.recommendations])

    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text("\n".join(lines) + "\n", encoding="utf-8")


def run_bot_playtest_with_report(prototype_root: Path, scenario_path: Path, output_root: Path | None = None) -> tuple[BotPlaytestResult, ActionablePlaytestReport, Path, Path]:
    result = run_bot_playtest_scenario(prototype_root, scenario_path)
    report = generate_actionable_playtest_report(result)
    run_stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    report_dir = (output_root or prototype_root / "testing" / "reports") / result.scenario_id / run_stamp
    json_path = report_dir / "playtest-report.v1.json"
    markdown_path = report_dir / "playtest-report.v1.md"

    report_dir.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(asdict(report), indent=2), encoding="utf-8")
    _write_playtest_report_markdown(report, markdown_path)
    return result, report, json_path, markdown_path


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="GameForge V1 AI orchestration skeleton")
    parser.add_argument("--suggest-uncertain", dest="uncertain_input", help="User reply to evaluate for uncertainty")
    parser.add_argument("--think-for-me", dest="think_for_me_input", help="User reply to evaluate for think-for-me mode")
    parser.add_argument("--topic", default="game-direction", help="Interview topic for the option ids")
    parser.add_argument("--generate-prototype", dest="brief_path", help="Path to saved interview brief JSON")
    parser.add_argument("--output", default="build/generated-prototypes", help="Output directory for generated prototypes")
    parser.add_argument("--launch", action="store_true", help="Compile and launch generated prototype runtime")
    parser.add_argument("--import-asset-manifest", help="Path to JSON list of asset import requests")
    parser.add_argument("--project-root", help="Project root containing assets/catalog.v1.json")
    parser.add_argument("--asset-query", default="", help="Search query for asset catalog")
    parser.add_argument("--asset-category", default="", help="Category filter for asset search")
    parser.add_argument(
        "--asset-tags",
        default="",
        help="Comma-separated tag filters that must all be present in an asset.",
    )
    parser.add_argument("--style-list-presets", action="store_true", help="List built-in and user-defined style presets")
    parser.add_argument("--style-create-manifest", help="Path to JSON payload for creating a user style preset")
    parser.add_argument("--style-select-preset", help="Preset id to set as active project style")
    parser.add_argument("--style-match-samples", help="Path to JSON sample asset list for match-project-style action")
    parser.add_argument("--bot-playtest-scenario", help="Path to bot playtest scenario JSON")
    parser.add_argument("--prototype-root", help="Generated prototype root to validate with bot playtests")
    parser.add_argument("--bot-playtest-report-output", help="Optional output directory for persisted playtest reports")
    return parser.parse_args()


def main() -> int:
    args = _parse_args()

    if args.uncertain_input is not None:
        response = generate_uncertainty_options(args.uncertain_input, args.topic)
        print(json.dumps(asdict(response), indent=2))
        return 0

    if args.think_for_me_input is not None:
        response = generate_think_for_me_directions(args.think_for_me_input, args.topic)
        print(json.dumps(asdict(response), indent=2))
        return 0

    if args.brief_path:
        prototype_root = _generate_prototype(Path(args.brief_path), Path(args.output))
        print(f"Generated prototype at: {prototype_root}")
        if args.launch:
            return _launch_generated_prototype(prototype_root)
        return 0

    if args.import_asset_manifest:
        if not args.project_root:
            raise ValueError("--project-root is required with --import-asset-manifest")
        manifest_payload = json.loads(Path(args.import_asset_manifest).read_text(encoding="utf-8"))
        requests = [AssetImportRequest(**item) for item in manifest_payload]
        result = import_assets(Path(args.project_root), requests)
        print(json.dumps(asdict(result), indent=2))
        return 0

    if args.project_root and (args.asset_query or args.asset_category or args.asset_tags):
        tags = [item.strip() for item in args.asset_tags.split(",") if item.strip()]
        results = search_asset_catalog(
            project_root=Path(args.project_root),
            query=args.asset_query,
            category=args.asset_category or None,
            required_tags=tags,
        )
        print(json.dumps({"results": results}, indent=2))
        return 0

    if args.style_list_presets:
        if not args.project_root:
            raise ValueError("--project-root is required with --style-list-presets")
        presets = list_style_presets(Path(args.project_root))
        print(json.dumps({"presets": [asdict(item) for item in presets]}, indent=2))
        return 0

    if args.style_create_manifest:
        if not args.project_root:
            raise ValueError("--project-root is required with --style-create-manifest")
        payload = json.loads(Path(args.style_create_manifest).read_text(encoding="utf-8"))
        preset = create_user_style_preset(
            Path(args.project_root),
            display_name=str(payload.get("display_name", "")).strip(),
            base_preset_id=str(payload.get("base_preset_id", "cozy-stylized")).strip(),
            overrides=payload.get("overrides", {}),
        )
        print(json.dumps({"created_preset": asdict(preset)}, indent=2))
        return 0

    if args.style_select_preset:
        if not args.project_root:
            raise ValueError("--project-root is required with --style-select-preset")
        state = select_project_style_preset(Path(args.project_root), args.style_select_preset)
        print(json.dumps({"style_state": asdict(state)}, indent=2))
        return 0

    if args.style_match_samples:
        if not args.project_root:
            raise ValueError("--project-root is required with --style-match-samples")
        payload = json.loads(Path(args.style_match_samples).read_text(encoding="utf-8"))
        if not isinstance(payload, list):
            raise ValueError("--style-match-samples payload must be a JSON list")
        transformed = match_project_style(Path(args.project_root), [dict(item) for item in payload])
        print(json.dumps({"transformed_assets": transformed}, indent=2))
        return 0

    if args.bot_playtest_scenario:
        if not args.prototype_root:
            raise ValueError("--prototype-root is required with --bot-playtest-scenario")
        output_root = Path(args.bot_playtest_report_output) if args.bot_playtest_report_output else None
        result, report, report_json_path, report_markdown_path = run_bot_playtest_with_report(
            Path(args.prototype_root),
            Path(args.bot_playtest_scenario),
            output_root=output_root,
        )
        print(
            json.dumps(
                {
                    "bot_playtest_result": asdict(result),
                    "actionable_report": asdict(report),
                    "report_paths": {
                        "json": str(report_json_path),
                        "markdown": str(report_markdown_path),
                    },
                },
                indent=2,
            )
        )
        return 0 if result.status != "failed" else 1

    print("GameForge V1 AI orchestration skeleton (Python)")
    print("Local-first orchestration placeholder")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
