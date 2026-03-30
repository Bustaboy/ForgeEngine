#!/usr/bin/env python3
"""Soul Loom AI orchestration entrypoint and interview helpers."""

from __future__ import annotations

import argparse
from collections import defaultdict
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
from dataclasses import asdict, dataclass, field, replace
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path

PYTHON_ROOT = Path(__file__).resolve().parent
if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))

from benchmark import record_performance_snapshot, run_benchmark_as_dict, should_run_idle_benchmark
from forge_hooks import (
    apply_to_scene_file,
    co_creator_tick,
    generate_building_templates,
    generate_dialog_tree,
    generate_npc_with_dialog,
    generate_recipes,
    modify_scene,
)
from models import prepare_models_as_dict
from pipeline import PIPELINE_STAGE_ORDER, StageDefinition
from art_bible import ArtBible, default_art_bible, default_asset_review_metadata, write_default_art_bible
from consistency import batch_generate, consistency_score
from kit_bashing import apply_generated_loot_to_scene, apply_kit_bash_to_scene, apply_variations_to_scene, quality_score
from live_edit import edit_scene_from_prompt
from change_log import append_change_log_entry, get_recent_changes
from model_manager import (
    DownloadCancelledError,
    ManagedModelDownloadError,
    download_model,
    ensure_freewill_model,
    list_installed_models,
    remove_model,
    run_quick_setup,
    run_onboarding,
)


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

MANUAL_FALLBACK_FAILURE_THRESHOLD = 5


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
class AttributionBundleEntry:
    asset_id: str
    display_name: str
    source: str
    license_id: str
    attribution_text: str
    attribution_url: str
    path: str


@dataclass(frozen=True)
class AttributionBundleExportResult:
    generated: bool
    required_asset_count: int
    json_path: str | None
    markdown_path: str | None


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
    critical_dead_end_blockers: list[str] = field(default_factory=list)


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
    critical_dead_end_blockers_count: int
    critical_dead_end_blockers: list[str]
    sections: list[PlaytestReportSection]
    source_probe_results: list[BotPlaytestProbeResult]


@dataclass(frozen=True)
class OrchestrationResult:
    orchestration_type: str
    target: str
    source: str
    confidence: float
    suggested_scene_patch: list[dict[str, object]]
    summary: str
    health_summary: dict[str, object] = field(default_factory=dict)
    provenance: list[dict[str, object]] = field(default_factory=list)
    playtest_feedback: dict[str, object] | None = None


@dataclass(frozen=True)
class OperationFailureFallbackState:
    operation_id: str
    consecutive_failures: int
    fallback_offered: bool
    guided_manual_mode_available: bool
    retry_with_ai_available: bool
    retry_action: str
    manual_mode_action: str | None


@dataclass(frozen=True)
class GeneratedGraphicAssetResult:
    generated: bool
    asset_type: str
    output_path: str
    metadata_path: str
    backend: str
    model: str
    seed: int
    prompt: str
    enhanced_prompt: str
    art_bible_path: str | None
    quality_score: float
    consistency_score: float
    variant_group_id: str
    variant_count: int
    variant_index: int
    generated_at_utc: str


@dataclass(frozen=True)
class AssetReviewResult:
    reviewed: bool
    decision: str
    source_asset_path: str
    destination_asset_path: str
    metadata_path: str
    review_status: str
    reviewer: str
    reviewed_at_utc: str
    production_ready: bool


class PipelineStageStatus(str, Enum):
    PASSED = "passed"
    FAILED = "failed"
    SKIPPED = "skipped"
    NEEDS_HUMAN_REVIEW = "needs-human-review"


@dataclass(frozen=True)
class PipelineStageResult:
    stage_id: str
    stage_title: str
    status: str
    summary: str
    started_at_utc: str
    completed_at_utc: str
    artifacts: list[str]
    fallback_state: OperationFailureFallbackState
    metadata: dict[str, object] = field(default_factory=dict)


@dataclass(frozen=True)
class PipelineExecutionResult:
    schema: str
    pipeline_id: str
    status: str
    brief_path: str
    output_root: str
    prototype_root: str | None
    benchmark: dict[str, object]
    stage_results: list[PipelineStageResult]
    dead_end_blockers: list[str]
    runtime_launch_status: str
    runtime_launch_pid: int | None
    runtime_launch_manifest_path: str | None
    runtime_launch_executable_path: str | None
    commercial_policy_checks: dict[str, object]
    csharp_shell_example: str
    completed_at_utc: str


class OperationFailureTracker:
    """Tracks sequential failures per operation id for guided manual fallback."""

    def __init__(self, fallback_failure_threshold: int = MANUAL_FALLBACK_FAILURE_THRESHOLD) -> None:
        if fallback_failure_threshold <= 0:
            raise ValueError("fallback_failure_threshold must be >= 1")
        self._fallback_failure_threshold = fallback_failure_threshold
        self._failure_streak_by_operation: dict[str, int] = {}

    def record_result(self, operation_id: str, *, success: bool) -> OperationFailureFallbackState:
        normalized_operation_id = operation_id.strip()
        if not normalized_operation_id:
            raise ValueError("operation_id is required")

        if success:
            self._failure_streak_by_operation[normalized_operation_id] = 0
            return OperationFailureFallbackState(
                operation_id=normalized_operation_id,
                consecutive_failures=0,
                fallback_offered=False,
                guided_manual_mode_available=False,
                retry_with_ai_available=True,
                retry_action="try_ai_again",
                manual_mode_action=None,
            )

        next_streak = self._failure_streak_by_operation.get(normalized_operation_id, 0) + 1
        self._failure_streak_by_operation[normalized_operation_id] = next_streak
        fallback_offered = next_streak >= self._fallback_failure_threshold
        return OperationFailureFallbackState(
            operation_id=normalized_operation_id,
            consecutive_failures=next_streak,
            fallback_offered=fallback_offered,
            guided_manual_mode_available=fallback_offered,
            retry_with_ai_available=True,
            retry_action="try_ai_again",
            manual_mode_action="guided_manual_mode" if fallback_offered else None,
        )


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

ATTRIBUTION_REQUIRED_LICENSES = {
    "cc-by-4.0",
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


def _render_generated_runtime_templates(
    prototype_root: Path,
    concept: str,
    scene: dict[str, object],
    asset_plan: dict[str, object],
) -> dict[str, object]:
    repo_root = Path(__file__).resolve().parents[2]
    templates_root = repo_root / "runtime" / "cpp" / "templates"
    if not templates_root.exists():
        raise ValueError(f"Runtime template root is missing: {templates_root}")

    generated_root = prototype_root / "generated" / "cpp"
    generated_root.mkdir(parents=True, exist_ok=True)

    spawn = dict(scene.get("player_spawn", {}))
    npcs = scene.get("npcs", [])
    first_npc = npcs[0] if isinstance(npcs, list) and npcs else {}

    def _float_literal(value: object, default: float) -> str:
        try:
            number = float(value)
        except (TypeError, ValueError):
            number = default
        literal = f"{number:.3f}".rstrip("0").rstrip(".")
        if "." not in literal:
            literal = f"{literal}.0"
        return literal

    replacements = {
        "{{PLAYER_SPAWN_X}}": _float_literal(spawn.get("x", 0.0), 0.0),
        "{{PLAYER_SPAWN_Y}}": _float_literal(spawn.get("y", 0.0), 0.0),
        "{{PLAYER_SPEED}}": "0.42",
        "{{NPC_ID}}": str(first_npc.get("id", "npc_01")),
        "{{NPC_SPAWN_X}}": _float_literal(first_npc.get("spawn_x", 0.35), 0.35),
        "{{NPC_SPAWN_Y}}": _float_literal(first_npc.get("spawn_y", -0.15), -0.15),
        "{{NPC_RADIUS}}": "0.34",
        "{{NPC_ANGULAR_SPEED}}": "0.85",
    }

    api_header = """#pragma once

#include <cstddef>

constexpr int kGeneratedSceneEntityLimit = 32;
constexpr int kGeneratedEntityIdCapacity = 64;

struct GeneratedEntity {
    char id[kGeneratedEntityIdCapacity];
    float x;
    float y;
    float vx;
    float vy;
    float size;
    float r;
    float g;
    float b;
    int active;
};

struct GeneratedSceneState {
    GeneratedEntity entities[kGeneratedSceneEntityLimit];
    int entity_count;
    float elapsed_time;
};

inline void gf_copy_id(char* destination, const char* source) {
    if (destination == nullptr || source == nullptr) {
        return;
    }
    std::size_t index = 0;
    while (source[index] != '\\0' && index < static_cast<std::size_t>(kGeneratedEntityIdCapacity - 1)) {
        destination[index] = source[index];
        ++index;
    }
    destination[index] = '\\0';
}

void GF_InitGeneratedScene(GeneratedSceneState* state);
void GF_UpdatePlayerController(GeneratedSceneState* state, float dt_seconds);
void GF_UpdateBasicNpc(GeneratedSceneState* state, float dt_seconds);
"""
    _write_text(generated_root / "gameplay_api.hpp", api_header)

    generated_files: list[str] = []
    for template_name in (
        "scene.cpp.template",
        "player_controller.cpp.template",
        "basic_npc.cpp.template",
        "CMakeLists.txt.template",
    ):
        template_path = templates_root / template_name
        content = template_path.read_text(encoding="utf-8")
        for key, value in replacements.items():
            content = content.replace(key, value)
        output_name = template_name.replace(".template", "")
        output_path = prototype_root / "generated" / output_name if output_name == "CMakeLists.txt" else generated_root / output_name
        _write_text(output_path, content)
        generated_files.append(str(output_path))

    metadata = {
        "schema": "gameforge.generated_runtime_templates.v1",
        "concept": concept,
        "generated_at_utc": _utc_now_iso(),
        "source_templates_root": str(templates_root),
        "generated_cpp_root": str(generated_root),
        "files": generated_files,
        "asset_policy_blocked": asset_plan.get("blocked_licenses", []),
    }
    _write_json(prototype_root / "generated" / "generated_runtime_manifest.v1.json", metadata)
    return metadata


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


def _require_nonempty_metadata_value(
    metadata: dict[str, object],
    keys: list[str],
) -> str | None:
    for key in keys:
        raw = metadata.get(key)
        if isinstance(raw, str) and raw.strip():
            return raw.strip()
    return None


def _build_attribution_entries(assets: list[dict[str, object]]) -> list[AttributionBundleEntry]:
    entries: list[AttributionBundleEntry] = []
    metadata_errors: list[str] = []

    for asset in sorted(assets, key=lambda item: str(item.get("asset_id", ""))):
        license_id = _normalize_license_id(str(asset.get("license_id", "")))
        if license_id not in ATTRIBUTION_REQUIRED_LICENSES:
            continue

        asset_id = str(asset.get("asset_id", "")).strip()
        display_name = str(asset.get("display_name", "")).strip() or asset_id
        metadata = dict(asset.get("metadata", {})) if isinstance(asset.get("metadata"), dict) else {}
        source = _require_nonempty_metadata_value(metadata, ["source", "source_url", "source_path"])
        attribution_text = _require_nonempty_metadata_value(metadata, ["attribution_text"])
        attribution_url = _require_nonempty_metadata_value(metadata, ["attribution_url"])
        path = str(asset.get("relative_path", "")).strip()

        missing_fields: list[str] = []
        if not source:
            missing_fields.append("metadata.source (or source_url/source_path)")
        if not attribution_text:
            missing_fields.append("metadata.attribution_text")
        if not attribution_url:
            missing_fields.append("metadata.attribution_url")
        if not path:
            missing_fields.append("relative_path")

        if missing_fields:
            metadata_errors.append(f"{asset_id or '<missing-asset-id>'}: {', '.join(missing_fields)}")
            continue

        entries.append(
            AttributionBundleEntry(
                asset_id=asset_id,
                display_name=display_name,
                source=source,
                license_id=license_id,
                attribution_text=attribution_text,
                attribution_url=attribution_url,
                path=path,
            )
        )

    if metadata_errors:
        details = "; ".join(metadata_errors)
        raise ValueError(
            "Attribution export requires complete metadata for attribution-required assets. "
            f"Missing fields: {details}"
        )

    return entries


def _write_attribution_markdown(entries: list[AttributionBundleEntry], destination: Path) -> None:
    lines = [
        "# Attribution",
        "",
        "This file lists assets that require attribution for distribution compliance.",
    ]
    for entry in entries:
        lines.extend(
            [
                "",
                f"## {entry.display_name} (`{entry.asset_id}`)",
                f"- License: {entry.license_id}",
                f"- Source: {entry.source}",
                f"- Attribution: {entry.attribution_text}",
                f"- Attribution URL: {entry.attribution_url}",
                f"- Path: `{entry.path}`",
            ]
        )
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text("\n".join(lines) + "\n", encoding="utf-8")


def export_attribution_bundle(
    project_root: Path,
    output_dir: Path | None = None,
) -> AttributionBundleExportResult:
    assets = _read_asset_catalog(project_root / "assets" / "catalog.v1.json")
    entries = _build_attribution_entries(assets)
    if not entries:
        return AttributionBundleExportResult(
            generated=False,
            required_asset_count=0,
            json_path=None,
            markdown_path=None,
        )

    output_root = output_dir or (project_root / "compliance")
    json_path = output_root / "attribution.bundle.v1.json"
    markdown_path = output_root / "attribution.md"
    payload = {
        "schema": "gameforge.attribution_bundle.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "asset_count": len(entries),
        "entries": [asdict(entry) for entry in entries],
    }
    output_root.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    _write_attribution_markdown(entries, markdown_path)
    return AttributionBundleExportResult(
        generated=True,
        required_asset_count=len(entries),
        json_path=str(json_path),
        markdown_path=str(markdown_path),
    )


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


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _write_json(path: Path, payload: dict[str, object]) -> None:
    _write_text(path, json.dumps(payload, indent=2))


def _generate_with_comfyui(enhanced_prompt: str, seed: int, output_path: Path, asset_type: str) -> tuple[str, str]:
    endpoint = os.environ.get("GAMEFORGE_COMFYUI_ENDPOINT", "http://127.0.0.1:8188").rstrip("/")
    workflow_path = os.environ.get("GAMEFORGE_COMFYUI_WORKFLOW_JSON", "").strip()
    if not workflow_path:
        raise ValueError("ComfyUI backend requires GAMEFORGE_COMFYUI_WORKFLOW_JSON")

    workflow_payload = json.loads(Path(workflow_path).read_text(encoding="utf-8"))
    if not isinstance(workflow_payload, dict):
        raise ValueError("ComfyUI workflow payload must be a JSON object")
    prompt_graph = dict(workflow_payload.get("prompt", workflow_payload))
    for node_payload in prompt_graph.values():
        if not isinstance(node_payload, dict):
            continue
        inputs = node_payload.get("inputs")
        if not isinstance(inputs, dict):
            continue
        if "text" in inputs:
            inputs["text"] = enhanced_prompt
        if "seed" in inputs:
            inputs["seed"] = seed
        if "filename_prefix" in inputs:
            inputs["filename_prefix"] = f"gameforge_{asset_type}_{output_path.stem}"

    request_payload = json.dumps({"prompt": prompt_graph}).encode("utf-8")
    request = urllib.request.Request(
        f"{endpoint}/prompt",
        data=request_payload,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:  # noqa: S310
        submit_payload = json.loads(response.read().decode("utf-8"))
    prompt_id = str(submit_payload.get("prompt_id", "")).strip()
    if not prompt_id:
        raise RuntimeError(f"ComfyUI did not return prompt_id: {submit_payload}")

    deadline = time.time() + 180
    history_payload: dict[str, object] | None = None
    while time.time() < deadline:
        with urllib.request.urlopen(f"{endpoint}/history/{prompt_id}", timeout=30) as response:  # noqa: S310
            history_payload = json.loads(response.read().decode("utf-8"))
        if isinstance(history_payload, dict) and history_payload.get(prompt_id):
            break
        time.sleep(1.0)
    if not history_payload or prompt_id not in history_payload:
        raise TimeoutError("Timed out waiting for ComfyUI generation history")

    outputs = history_payload[prompt_id].get("outputs", {})
    if not isinstance(outputs, dict):
        raise RuntimeError("ComfyUI history output did not include outputs block")

    for node_output in outputs.values():
        if not isinstance(node_output, dict):
            continue
        images = node_output.get("images")
        if isinstance(images, list) and images:
            first_image = images[0]
            if isinstance(first_image, dict) and first_image.get("filename"):
                comfy_output_dir = Path(os.environ.get("GAMEFORGE_COMFYUI_OUTPUT_DIR", str(Path.home() / "ComfyUI" / "output")))
                source_path = comfy_output_dir / str(first_image["filename"])
                if source_path.exists():
                    output_path.write_bytes(source_path.read_bytes())
                    return ("comfyui", os.environ.get("GAMEFORGE_COMFYUI_MODEL_NAME", "comfyui-local-workflow"))
    raise RuntimeError("ComfyUI run finished but no image file could be resolved from history")


def _generate_with_debug_backend(enhanced_prompt: str, seed: int, output_path: Path, asset_type: str) -> tuple[str, str]:
    normalized = (enhanced_prompt + f"|{asset_type}|{seed}").encode("utf-8")
    bucket = sum(normalized) % 255
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024">
<rect width="1024" height="1024" fill="rgb({bucket}, {(bucket * 3) % 255}, {(bucket * 7) % 255})"/>
<text x="40" y="512" fill="white" font-size="36">Soul Loom Local Debug Asset</text>
<text x="40" y="572" fill="white" font-size="24">type={asset_type} seed={seed}</text>
</svg>
"""
    output_path.write_text(svg, encoding="utf-8")
    return ("debug-local", "procedural-svg-v1")


def _quality_score_from_prompt(enhanced_prompt: str, asset_type: str) -> float:
    prompt_tokens = [token for token in re.split(r"\s+", enhanced_prompt.strip()) if token]
    prompt_density = min(1.0, len(prompt_tokens) / 96.0)
    specificity_bonus = 0.1 if asset_type in enhanced_prompt.lower() else 0.0
    return round(max(0.0, min(1.0, 0.55 + (prompt_density * 0.35) + specificity_bonus)), 4)


def quality_scan_scene(scene_path: Path, art_bible_path: Path | None = None) -> dict[str, object]:
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    if art_bible_path is not None and art_bible_path.exists():
        art_bible = ArtBible.from_json_file(art_bible_path)
    else:
        art_bible = default_art_bible(project_name=scene_path.parent.name or "Soul Loom Project")

    quality = quality_score(scene_payload, art_bible=art_bible)
    consistency = consistency_score(scene_payload, art_bible=art_bible)
    quality_node = scene_payload.get("quality_metadata")
    if not isinstance(quality_node, dict):
        quality_node = {}
        scene_payload["quality_metadata"] = quality_node
    quality_node.update(
        {
            "schema": "gameforge.scene_quality_metadata.v1",
            "score": int(quality.get("score", 0)),
            "components": quality.get("components", {}),
            "estimated_vram_mb": float(quality.get("estimated_vram_mb", 0.0)),
            "sprite_count": int(quality.get("sprite_count", 0)),
            "warnings": quality.get("warnings", []),
            "consistency_score": float(consistency.get("score", 0.0)),
            "consistency_components": consistency.get("components", {}),
            "source": "quality-scan",
        }
    )
    scene_path.write_text(json.dumps(scene_payload, indent=2) + "\n", encoding="utf-8")
    return {"scene_path": str(scene_path), "quality": quality, "consistency": consistency, "persisted": True}


def _safe_scene_patch(candidate_patch: list[dict[str, object]]) -> list[dict[str, object]]:
    safe_patch: list[dict[str, object]] = []
    for operation in candidate_patch:
        if not isinstance(operation, dict):
            continue
        op = str(operation.get("op", "")).strip().lower()
        path = str(operation.get("path", "")).strip()
        if op not in {"set", "add", "remove"}:
            continue
        if not path.startswith("/") or path.startswith("/_internal"):
            continue
        safe_operation: dict[str, object] = {"op": op, "path": path}
        if op != "remove" and "value" in operation:
            safe_operation["value"] = operation.get("value")
        safe_patch.append(safe_operation)
    return safe_patch


def _read_scene_npcs(scene_payload: dict[str, object]) -> list[dict[str, object]]:
    npcs = scene_payload.get("npcs")
    if isinstance(npcs, list):
        return [npc for npc in npcs if isinstance(npc, dict)]
    entities = scene_payload.get("entities")
    if not isinstance(entities, list):
        return []
    return [
        entity
        for entity in entities
        if isinstance(entity, dict)
        and str(entity.get("type", "")).strip().lower() == "npc"
    ]


def _extract_npc_needs(npc_payload: dict[str, object]) -> dict[str, float]:
    needs_node = npc_payload.get("needs")
    if not isinstance(needs_node, dict):
        return {}
    extracted: dict[str, float] = {}
    for key in ("hunger", "energy", "social", "fun"):
        raw_value = needs_node.get(key)
        if isinstance(raw_value, (int, float)):
            extracted[key] = float(raw_value)
    return extracted


def _relationship_summary(scene_payload: dict[str, object], npc_id: str) -> dict[str, float]:
    if not npc_id:
        return {}
    relationships = scene_payload.get("relationships")
    if not isinstance(relationships, dict):
        return {}
    profile = relationships.get(npc_id)
    if not isinstance(profile, dict):
        return {}
    summary: dict[str, float] = {}
    for key in ("trust", "respect", "grudge", "debt", "loyalty"):
        raw_value = profile.get(key)
        if isinstance(raw_value, (int, float)):
            summary[key] = float(raw_value)
    return summary


def _source_from_scene_for_npc_day(scene_payload: dict[str, object], lightweight_mode: str) -> str:
    free_will = scene_payload.get("free_will")
    rag = scene_payload.get("rag")
    free_will_enabled = isinstance(free_will, dict) and bool(free_will.get("enabled", True))
    llm_enabled = isinstance(free_will, dict) and bool(free_will.get("llm_enabled", False))
    rag_enabled = isinstance(rag, dict) and bool(rag.get("enabled", True))

    if lightweight_mode == "performance":
        return "scripted"
    if rag_enabled and free_will_enabled:
        return "hybrid"
    if llm_enabled:
        return "llm"
    return "scripted"


def _source_from_scene_for_narrative(scene_payload: dict[str, object], lightweight_mode: str) -> str:
    rag = scene_payload.get("rag")
    free_will = scene_payload.get("free_will")
    rag_enabled = isinstance(rag, dict) and bool(rag.get("enabled", True))
    llm_enabled = isinstance(free_will, dict) and bool(free_will.get("llm_enabled", False))
    if lightweight_mode == "performance":
        return "scripted"
    if rag_enabled:
        return "hybrid"
    if llm_enabled:
        return "llm"
    return "scripted"


def _source_from_scene_for_review(scene_payload: dict[str, object], lightweight_mode: str) -> str:
    rag = scene_payload.get("rag")
    free_will = scene_payload.get("free_will")
    rag_enabled = isinstance(rag, dict) and bool(rag.get("enabled", True))
    llm_enabled = isinstance(free_will, dict) and bool(free_will.get("llm_enabled", False))
    if lightweight_mode == "performance":
        return "scripted"
    if rag_enabled and llm_enabled:
        return "hybrid"
    if rag_enabled:
        return "rag"
    if llm_enabled:
        return "llm"
    return "scripted"


def _should_include_review_playtest(scene_payload: dict[str, object], target: str) -> bool:
    ai_node = scene_payload.get("ai_orchestration")
    default_enabled = isinstance(ai_node, dict) and bool(ai_node.get("enable_bot_playtesting_in_review", False))
    normalized_target = target.strip().lower()
    if normalized_target in {"playtest", "with_playtest", "review+playtest"}:
        return True
    if normalized_target in {"no_playtest", "without_playtest"}:
        return False
    return default_enabled


def _simulate_scene_review_playtest(scene_payload: dict[str, object], fps_avg: float) -> dict[str, object]:
    findings: list[str] = []
    recommendations: list[str] = []

    entities = scene_payload.get("entities")
    entity_count = len(entities) if isinstance(entities, list) else 0
    npcs = _read_scene_npcs(scene_payload)
    if entity_count == 0:
        findings.append("Scene has no entities; traversal loop cannot be validated.")
        recommendations.append("Add at least one player spawn and one interactable prop for smoke playtests.")
    elif entity_count > 120:
        findings.append(f"High entity density ({entity_count}) may cause traversal hitches.")
        recommendations.append("Cull distant props or convert decorative actors to static instances.")

    if len(npcs) == 0:
        findings.append("No NPCs found for social/quest interaction checks.")
        recommendations.append("Add at least one NPC with scripted_behavior for deterministic review checkpoints.")

    weather = scene_payload.get("weather")
    weather_fx = int(weather.get("particle_density", 0) or 0) if isinstance(weather, dict) else 0
    if weather_fx > 60 and fps_avg < 40:
        findings.append("Weather particle density appears high relative to measured framerate.")
        recommendations.append("Lower weather particle density for performance mode review paths.")

    if not findings:
        findings.append("No critical blockers detected in short deterministic playtest pass.")

    return {
        "mode": "short_pass",
        "executed": True,
        "status": "ok",
        "findings": findings[:4],
        "recommendations": recommendations[:4],
    }


def _coerce_orchestration_type(orchestration_type: str) -> str:
    normalized = orchestration_type.strip().lower()
    if normalized not in {"narrative_checkpoint", "npc_day", "scene_review"}:
        raise ValueError(
            "Usage: orchestrator.py /orchestrate <narrative_checkpoint|npc_day|scene_review> <scene_json_path> [target]; "
            "npc_day target=<npc_id>, narrative_checkpoint target=<checkpoint>, "
            "scene_review target=<playtest|with_playtest|no_playtest>"
        )
    return normalized


def _build_orchestration_result(
    *,
    orchestration_type: str,
    scene_path: Path,
    target: str | None = None,
) -> OrchestrationResult:
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    normalized_type = _coerce_orchestration_type(orchestration_type)
    normalized_target = (target or "default").strip() or "default"
    recent_changes = get_recent_changes(scene_path, limit=6)
    latest_snapshot = record_performance_snapshot(scene_path, session_name=f"orchestrate_{normalized_type}")["snapshot"]
    metrics = latest_snapshot.get("metrics", {}) if isinstance(latest_snapshot, dict) else {}
    fps_avg = float(metrics.get("fps_avg", 0.0) or 0.0)
    lightweight_mode = str(
        ((scene_payload.get("optimization_overrides") or {}) if isinstance(scene_payload.get("optimization_overrides"), dict) else {}).get(
            "lightweight_mode",
            "balanced",
        )
    ).strip().lower()
    performance_throttled = lightweight_mode == "performance"
    health_summary: dict[str, object] = {}
    provenance: list[dict[str, object]] = []
    playtest_feedback: dict[str, object] | None = None

    if normalized_type == "narrative_checkpoint":
        source = _source_from_scene_for_narrative(scene_payload, lightweight_mode)
        confidence = 0.84 if recent_changes else 0.68
        if performance_throttled:
            confidence = min(confidence, 0.62)
        generational_memory_size = len(scene_payload.get("compressed_event_log", [])) if isinstance(scene_payload.get("compressed_event_log"), list) else 0
        weather = scene_payload.get("weather") if isinstance(scene_payload.get("weather"), dict) else {}
        settlement = scene_payload.get("settlement") if isinstance(scene_payload.get("settlement"), dict) else {}
        recent_summary = recent_changes[0]["summary"] if recent_changes else "No recent changes logged."
        patch = _safe_scene_patch(
            [
                {
                    "op": "set",
                    "path": "/narrative_orchestration/last_checkpoint",
                    "value": normalized_target,
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/source",
                    "value": source,
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/checkpoint_summary",
                    "value": str(recent_summary)[:240],
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/generational_memory_size",
                    "value": generational_memory_size,
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/lightweight_mode",
                    "value": lightweight_mode,
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/status",
                    "value": "throttled" if performance_throttled else "ready",
                },
                {
                    "op": "set",
                    "path": "/narrative_orchestration/world_state",
                    "value": {
                        "weather": str(weather.get("current_weather", "clear")),
                        "morale": float(settlement.get("morale", 0.0) or 0.0),
                        "day_count": int(scene_payload.get("day_count", 1) or 1),
                    },
                },
            ]
        )
        summary = (
            f"Checkpoint '{normalized_target}' adapted from change-log + memory ({generational_memory_size} entries)"
            f" with source={source}{' [throttled]' if performance_throttled else ''}."
        )
    elif normalized_type == "npc_day":
        source = _source_from_scene_for_npc_day(scene_payload, lightweight_mode)
        confidence = 0.78 if source == "hybrid" else 0.7
        if performance_throttled:
            confidence = min(confidence, 0.6)
        npc_entries = _read_scene_npcs(scene_payload)
        npc_count = len(npc_entries)
        selected_npc = next(
            (
                npc
                for npc in npc_entries
                if str(npc.get("id", "")).strip() == normalized_target
            ),
            npc_entries[0] if npc_entries else {},
        )
        selected_npc_id = str(selected_npc.get("id", normalized_target)).strip() or normalized_target
        needs = _extract_npc_needs(selected_npc)
        relationship = _relationship_summary(scene_payload, selected_npc_id)
        scripted_behavior = selected_npc.get("scripted_behavior") if isinstance(selected_npc.get("scripted_behavior"), dict) else {}
        spark_line = ""
        free_will = scene_payload.get("free_will")
        if isinstance(free_will, dict) and isinstance(free_will.get("last_spark_line_by_npc"), dict):
            spark_line = str(free_will["last_spark_line_by_npc"].get(selected_npc_id, ""))
        recent_headlines = [str(change.get("summary", "")).strip() for change in recent_changes[:2] if str(change.get("summary", "")).strip()]
        npc_summary = (
            f"NPC {selected_npc_id}: scripted={str(scripted_behavior.get('current_state', 'none'))}, "
            f"spark='{spark_line or 'none'}', needs={needs or 'n/a'}, relationships={relationship or 'n/a'}."
        )
        patch = _safe_scene_patch(
            [
                {
                    "op": "set",
                    "path": "/free_will/orchestration/day_plan_target",
                    "value": selected_npc_id,
                },
                {
                    "op": "set",
                    "path": "/free_will/orchestration/day_plan_status",
                    "value": "throttled" if performance_throttled else "ready",
                },
                {
                    "op": "set",
                    "path": "/free_will/orchestration/day_plan_npc_count",
                    "value": npc_count,
                },
                {
                    "op": "set",
                    "path": f"/free_will/orchestration/day_plan_by_npc/{selected_npc_id}",
                    "value": {
                        "summary": npc_summary[:320],
                        "source": source,
                        "lightweight_mode": lightweight_mode,
                        "change_log_headlines": recent_headlines,
                    },
                },
                {
                    "op": "set",
                    "path": "/free_will/orchestration/day_plan_latest_summary",
                    "value": npc_summary[:320],
                },
            ]
        )
        summary = (
            f"NPC day summary ready for '{selected_npc_id}' using {source} orchestration"
            f" across {npc_count} NPC(s){' [throttled]' if performance_throttled else ''}."
        )
    else:
        source = _source_from_scene_for_review(scene_payload, lightweight_mode)
        confidence = 0.79 if source in {"hybrid", "rag"} else 0.72
        if performance_throttled:
            confidence = min(confidence, 0.63)

        max_suggestions = 1 if performance_throttled else 3
        optimization = optimization_critique(scene_path, max_suggestions=max_suggestions)
        suggestions = optimization.get("suggestions", []) if isinstance(optimization, dict) else []
        aggregated_patch: list[dict[str, object]] = []
        if isinstance(suggestions, list):
            for suggestion in suggestions[:max_suggestions]:
                if not isinstance(suggestion, dict):
                    continue
                raw_patch = suggestion.get("patch", [])
                if isinstance(raw_patch, list):
                    aggregated_patch.extend(raw_patch)
        patch = _safe_scene_patch(aggregated_patch)

        optimization_summary = optimization.get("summary", {}) if isinstance(optimization, dict) else {}
        health_summary = {
            "project_health_score": int(optimization.get("health_score", 0) or 0) if isinstance(optimization, dict) else 0,
            "lightweight_mode": lightweight_mode,
            "lightweight_mode_suggestion": (
                optimization.get("lightweight_mode_suggestion", {}) if isinstance(optimization, dict) else {}
            ),
            "performance": {
                "fps_avg": round(fps_avg, 2),
                "target_profile": str(optimization_summary.get("target_profile", "unknown")),
                "draw_calls": int(metrics.get("draw_calls", 0) or 0),
                "vram_usage_mb": round(float(metrics.get("vram_usage_mb", 0.0) or 0.0), 1),
            },
            "change_log_samples": [str(change.get("summary", "")).strip() for change in recent_changes[:3] if isinstance(change, dict)],
        }

        rag = scene_payload.get("rag")
        rag_documents = 0
        if isinstance(rag, dict):
            corpus = rag.get("documents")
            rag_documents = len(corpus) if isinstance(corpus, list) else int(rag.get("document_count", 0) or 0)

        provenance = [
            {"source": "optimization_critique", "kind": "critique", "used": True, "suggestions": len(suggestions) if isinstance(suggestions, list) else 0},
            {"source": "performance_snapshot", "kind": "metrics", "used": True, "session": f"orchestrate_{normalized_type}", "fps_avg": round(fps_avg, 2)},
            {"source": "change_log", "kind": "history", "used": True, "entries_considered": len(recent_changes)},
            {"source": "rag", "kind": "knowledge", "used": bool(rag_documents), "documents": rag_documents},
        ]

        if _should_include_review_playtest(scene_payload, normalized_target):
            playtest_feedback = _simulate_scene_review_playtest(scene_payload, fps_avg)
            provenance.append({"source": "bot_playtest_hook", "kind": "simulation", "used": True, "mode": "short_pass"})

        summary = (
            f"Scene review completed: health {health_summary.get('project_health_score', 0)}/100"
            f" using {source} orchestration with {len(patch)} safe patch op(s)"
            f"{' + playtest feedback' if playtest_feedback else ''}."
        )

    return OrchestrationResult(
        orchestration_type=normalized_type,
        target=normalized_target,
        source=source,
        confidence=round(confidence, 2),
        suggested_scene_patch=patch,
        summary=summary,
        health_summary=health_summary,
        provenance=provenance,
        playtest_feedback=playtest_feedback,
    )


def optimization_critique(scene_path: Path, max_suggestions: int = 5) -> dict[str, object]:
    """Generate a lightweight optimization critique with JSON patch suggestions."""

    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    project_root = scene_path.resolve().parent.parent
    history_path = project_root / "performance_history.json"
    changes_path = project_root / "changes.log.json"
    models_path = project_root / "models.json"

    history_payload = {}
    if history_path.exists():
        try:
            history_payload = json.loads(history_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            history_payload = {}
    snapshots = history_payload.get("snapshots", []) if isinstance(history_payload, dict) else []
    latest_snapshot = snapshots[-1] if isinstance(snapshots, list) and snapshots else {}
    previous_snapshot = snapshots[-2] if isinstance(snapshots, list) and len(snapshots) > 1 else {}
    latest_metrics = latest_snapshot.get("metrics", {}) if isinstance(latest_snapshot, dict) else {}
    previous_metrics = previous_snapshot.get("metrics", {}) if isinstance(previous_snapshot, dict) else {}

    target_profile = "unknown"
    if isinstance(latest_snapshot, dict):
        target_profile = str(latest_snapshot.get("target_hardware_profile", "unknown") or "unknown")

    target_fps = {"potato": 30.0, "balanced": 45.0, "high_fidelity": 60.0}.get(target_profile, 45.0)
    fps_avg = float(latest_metrics.get("fps_avg", 0.0) or 0.0)
    vram_mb = float(latest_metrics.get("vram_usage_mb", 0.0) or 0.0)
    draw_calls = int(latest_metrics.get("draw_calls", 0) or 0)
    update_time_ms = float(latest_metrics.get("update_time_ms", 0.0) or 0.0)
    fps_prev = float(previous_metrics.get("fps_avg", fps_avg) or fps_avg)
    fps_delta = round(fps_avg - fps_prev, 2)
    sprite_count_metric = int(latest_metrics.get("sprite_count", 0) or 0)
    entity_count_metric = int(latest_metrics.get("entity_count", 0) or 0)

    quality_metadata = scene_payload.get("quality_metadata", {})
    quality_score_value = float(quality_metadata.get("score", 0.0) or 0.0) if isinstance(quality_metadata, dict) else 0.0

    models_payload = {}
    if models_path.exists():
        try:
            models_payload = json.loads(models_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            models_payload = {}
    installed_models = models_payload.get("installed_models", []) if isinstance(models_payload, dict) else []
    managed_models = models_payload.get("models", {}) if isinstance(models_payload, dict) else {}
    forgeguard_available = any(
        isinstance(entry, dict) and str(entry.get("friendly_name", "")).strip().lower() == "forgeguard"
        for entry in installed_models if isinstance(installed_models, list)
    )
    if not forgeguard_available and isinstance(managed_models, dict):
        forgeguard_entry = managed_models.get("forgeguard", {})
        forgeguard_path = str(forgeguard_entry.get("path", "")).strip() if isinstance(forgeguard_entry, dict) else ""
        forgeguard_available = bool(forgeguard_path)

    optimization_overrides = scene_payload.get("optimization_overrides", {})
    if not isinstance(optimization_overrides, dict):
        optimization_overrides = {}
    scene_mode = str(optimization_overrides.get("lightweight_mode", "balanced") or "balanced").strip().lower()
    if scene_mode not in {"performance", "balanced", "quality"}:
        scene_mode = "balanced"

    score = 65
    if fps_avg > 0:
        score += 15 if fps_avg >= target_fps else -15
    if fps_delta < -2.0:
        score -= 8
    if draw_calls > 450:
        score -= 10
    if vram_mb > 0 and vram_mb > 4096:
        score -= 8
    if quality_score_value > 0:
        score += 5 if quality_score_value >= 70 else -4
    score = max(0, min(100, int(round(score))))

    guardrails_node = optimization_overrides.get("guardrails", {})
    if not isinstance(guardrails_node, dict):
        guardrails_node = {}
    soft_warning_threshold = int(guardrails_node.get("soft_warning_threshold", 50) or 50)
    hard_block_threshold = int(guardrails_node.get("hard_block_threshold", 30) or 30)
    hard_block_enabled = bool(guardrails_node.get("hard_block_enabled", False))
    soft_warning_threshold = max(20, min(95, soft_warning_threshold))
    hard_block_threshold = max(10, min(90, hard_block_threshold))
    if hard_block_threshold >= soft_warning_threshold:
        hard_block_threshold = max(10, soft_warning_threshold - 10)

    recent_changes = get_recent_changes(scene_path, limit=8)
    has_changes_log = changes_path.exists()

    def _has_system_node(name: str) -> bool:
        return isinstance(scene_payload.get(name), dict)

    def _node_enabled(name: str) -> bool:
        node = scene_payload.get(name)
        if not isinstance(node, dict):
            return False
        enabled = node.get("enabled")
        if isinstance(enabled, bool):
            return enabled
        return True

    def _estimate_scene_complexity() -> dict[str, int]:
        entities = scene_payload.get("entities")
        sprites = scene_payload.get("sprites")
        render_2d = scene_payload.get("render_2d")
        inferred_entities = len(entities) if isinstance(entities, list) else 0
        inferred_sprites = len(sprites) if isinstance(sprites, list) else 0
        if isinstance(render_2d, dict):
            inferred_sprites = max(
                inferred_sprites,
                int(render_2d.get("sprite_count", 0) or 0),
            )

        return {
            "entity_count": max(entity_count_metric, inferred_entities),
            "sprite_count": max(sprite_count_metric, inferred_sprites),
            "draw_calls": draw_calls,
        }

    scene_complexity = _estimate_scene_complexity()
    entity_count = int(scene_complexity.get("entity_count", 0) or 0)
    sprite_count = int(scene_complexity.get("sprite_count", 0) or 0)
    has_inventory_system = _has_system_node("inventory_system")
    has_inventory_recipes = _has_system_node("inventory_recipes")
    has_heavy_post = _node_enabled("post_processing") or _node_enabled("post_process")
    has_particle_stack = _node_enabled("particle_system") or _node_enabled("weather_system")
    has_realtime_shadowing = _node_enabled("lighting_system") or _node_enabled("light_system")
    recent_summary = " ".join(str(change.get("summary", "")) for change in recent_changes[:4]).lower()
    duplicate_hint = "duplicate" in recent_summary or "duplication" in recent_summary
    profile_scale = {"potato": 1.2, "balanced": 1.0, "high_fidelity": 0.8}.get(target_profile, 1.0)

    pruning_candidates: list[dict[str, object]] = []

    def _add_pruning_candidate(
        *,
        candidate_id: str,
        priority: int,
        title: str,
        description: str,
        alternative: str,
        estimated_win: dict[str, float | int],
        trigger: bool,
        patch: list[dict[str, object]],
    ) -> None:
        if not trigger:
            return
        pruning_candidates.append(
            {
                "id": candidate_id,
                "priority": priority,
                "kind": "prune",
                "title": title,
                "summary": description,
                "description": description,
                "lightweight_alternative": alternative,
                "safety": "safe",
                "reversible": True,
                "confidence": 0.74,
                "impact": "medium" if priority > 1 else "high",
                "estimated_win": estimated_win,
                "preview": f"Alternative: {alternative}",
                "patch": patch,
                "rollback_patch": [
                    {"op": "remove", "path": op["path"]}
                    for op in patch
                    if isinstance(op, dict) and str(op.get("op", "")).lower() == "set"
                ],
            }
        )

    _add_pruning_candidate(
        candidate_id="pr-001",
        priority=1,
        title="Replace heavy rain particles with sprite-sheet weather overlay",
        description="Particle/weather stack appears expensive for current scene complexity; suggest lightweight weather overlay.",
        alternative="Use animated sprite billboards or screen-space weather sprite sheet.",
        estimated_win={
            "fps_gain_pct": round(6.0 * profile_scale, 1),
            "vram_saved_mb": int(round(96 * profile_scale)),
        },
        trigger=has_particle_stack and (draw_calls > 380 or sprite_count > 800),
        patch=[
            {"op": "set", "path": "/weather_system/use_sprite_sheet_overlay", "value": True},
            {"op": "set", "path": "/weather_system/particle_emission_scale", "value": 0.35},
            {"op": "set", "path": "/optimization_overrides/pruning/particle_weather_mode", "value": "sprite_sheet_overlay"},
        ],
    )
    _add_pruning_candidate(
        candidate_id="pr-002",
        priority=1,
        title="Scope real-time shadows to nearby gameplay-critical entities",
        description="Real-time shadowing can be pruned on distant/background entities without harming readability.",
        alternative="Switch distant lights/occluders to baked or static shadow mode.",
        estimated_win={
            "fps_gain_pct": round(7.5 * profile_scale, 1),
            "draw_call_reduction_pct": int(round(12 * profile_scale)),
        },
        trigger=has_realtime_shadowing and (draw_calls > 420 or fps_avg < target_fps),
        patch=[
            {"op": "set", "path": "/render_2d/realtime_shadows_distance_m", "value": 18},
            {"op": "set", "path": "/lighting_system/distant_shadow_mode", "value": "baked"},
            {"op": "set", "path": "/optimization_overrides/pruning/realtime_shadow_scope", "value": "nearby_only"},
        ],
    )
    _add_pruning_candidate(
        candidate_id="pr-003",
        priority=2,
        title="Reduce heavy post-processing chain for target hardware",
        description="Current post-processing is likely over budget for the target profile.",
        alternative="Use selective bloom-only pass or LUT-only color grading.",
        estimated_win={
            "fps_gain_pct": round(5.5 * profile_scale, 1),
            "vram_saved_mb": int(round(128 * profile_scale)),
        },
        trigger=has_heavy_post and (vram_mb > 3072 or fps_avg < target_fps or "post" in recent_summary),
        patch=[
            {"op": "set", "path": "/post_processing/quality_tier", "value": "low"},
            {"op": "set", "path": "/post_processing/enable_motion_blur", "value": False},
            {"op": "set", "path": "/optimization_overrides/pruning/post_processing_profile", "value": "light"},
        ],
    )
    _add_pruning_candidate(
        candidate_id="pr-004",
        priority=2,
        title="Merge duplicate behavior systems into shared canonical controller",
        description="Recent changes and systems indicate duplicated runtime behavior ownership.",
        alternative="Delegate to one canonical system with data-driven variants.",
        estimated_win={
            "update_time_ms_reduction": round(0.8 * profile_scale, 2),
            "entity_update_reduction_pct": int(round(9 * profile_scale)),
        },
        trigger=duplicate_hint or (has_inventory_system and has_inventory_recipes) or entity_count > 500,
        patch=[
            {"op": "set", "path": "/optimization_overrides/pruning/merge_duplicate_behaviors", "value": True},
            {"op": "set", "path": "/optimization_overrides/pruning/canonical_behavior_owner", "value": "inventory_system"},
        ],
    )

    pass_one_context = {
        "recent_change_count": len(recent_changes),
        "latest_change_action": recent_changes[0].get("action_type", "unknown") if recent_changes else "none",
        "snapshot_count": len(snapshots) if isinstance(snapshots, list) else 0,
        "target_profile": target_profile,
        "metrics": {
            "fps_avg": fps_avg,
            "fps_delta": fps_delta,
            "draw_calls": draw_calls,
            "vram_usage_mb": vram_mb,
            "update_time_ms": update_time_ms,
            "entity_count": entity_count,
            "sprite_count": sprite_count,
        },
        "scene_complexity": scene_complexity,
        "scene_systems": sorted(
            key
            for key, value in scene_payload.items()
            if isinstance(value, dict) and key.endswith("_system")
        ),
    }

    critique_findings: list[dict[str, object]] = []
    if fps_avg > 0 and fps_avg < target_fps:
        critique_findings.append(
            {
                "id": "fg-low-fps",
                "priority": 1,
                "kind": "performance",
                "title": "Frame pacing under target profile",
                "rationale": f"Average FPS ({fps_avg:.1f}) is below target ({target_fps:.0f}) for '{target_profile}'.",
                "impact": "high",
            }
        )
    if draw_calls > 450:
        critique_findings.append(
            {
                "id": "fg-draw-call-bloat",
                "priority": 2,
                "kind": "bloat",
                "title": "Draw-call bloat detected",
                "rationale": f"Draw calls ({draw_calls}) exceed conservative V1 budget.",
                "impact": "medium",
            }
        )
    if vram_mb > 0 and vram_mb > 4096:
        critique_findings.append(
            {
                "id": "fg-vram-pressure",
                "priority": 3,
                "kind": "performance",
                "title": "VRAM pressure detected",
                "rationale": f"VRAM usage ({vram_mb:.0f} MB) is above V1 default guardrail.",
                "impact": "medium",
            }
        )
    if has_inventory_system and has_inventory_recipes:
        critique_findings.append(
            {
                "id": "fg-dup-inventory-systems",
                "priority": 2,
                "kind": "architecture",
                "title": "Inventory system duplication risk",
                "rationale": "Reuse InventorySystem as the single source to avoid split ownership.",
                "impact": "medium",
            }
        )

    suggestions: list[dict[str, object]] = []
    for finding in sorted(critique_findings, key=lambda item: int(item.get("priority", 99))):
        finding_id = str(finding.get("id", "fg-suggestion"))
        if finding_id == "fg-low-fps":
            suggestions.append(
                {
                    "id": "sg-001",
                    "priority": 1,
                    "title": "Enable conservative runtime optimization hints",
                    "summary": str(finding.get("rationale", "")),
                    "safety": "safe",
                    "confidence": 0.86,
                    "impact": "high",
                    "estimated_win": {"fps_gain_pct": 10.0, "frame_time_ms_reduction": 2.3},
                    "preview": "Adds runtime hints and avoids creating new systems.",
                    "patch": [
                        {"op": "set", "path": "/optimization_overrides/runtime_hints/prefer_low_cost_shaders", "value": True},
                        {"op": "set", "path": "/optimization_overrides/runtime_hints/target_fps", "value": target_fps},
                    ],
                }
            )
        elif finding_id == "fg-draw-call-bloat":
            suggestions.append(
                {
                    "id": "sg-002",
                    "priority": 2,
                    "title": "Cap dynamic lights for heavy scenes",
                    "summary": str(finding.get("rationale", "")),
                    "safety": "safe",
                    "confidence": 0.81,
                    "impact": "medium",
                    "estimated_win": {"fps_gain_pct": 6.0, "draw_call_reduction_pct": 14.0},
                    "preview": "Sets render_2d.max_dynamic_lights=6 and enables occlusion hints.",
                    "patch": [
                        {"op": "set", "path": "/render_2d/max_dynamic_lights", "value": 6},
                        {"op": "set", "path": "/optimization_overrides/render_hints/occlusion_culling", "value": True},
                    ],
                }
            )
        elif finding_id == "fg-vram-pressure":
            suggestions.append(
                {
                    "id": "sg-003",
                    "priority": 3,
                    "title": "Enable texture streaming hints",
                    "summary": str(finding.get("rationale", "")),
                    "safety": "safe",
                    "confidence": 0.78,
                    "impact": "medium",
                    "estimated_win": {"vram_reduction_mb": 512.0},
                    "preview": "Turns on texture streaming and lowers memory spikes.",
                    "patch": [
                        {"op": "set", "path": "/render_2d/texture_streaming_enabled", "value": True},
                    ],
                }
            )
        elif finding_id == "fg-dup-inventory-systems":
            suggestions.append(
                {
                    "id": "sg-004",
                    "priority": 2,
                    "title": "Consolidate inventory ownership",
                    "summary": str(finding.get("rationale", "")),
                    "safety": "safe",
                    "confidence": 0.73,
                    "impact": "medium",
                    "estimated_win": {"update_time_ms_reduction": 0.5},
                    "preview": "Flags InventorySystem as canonical to prevent duplicated logic.",
                    "patch": [
                        {"op": "set", "path": "/inventory_system/is_primary", "value": True},
                        {"op": "set", "path": "/inventory_recipes/delegates_to", "value": "inventory_system"},
                    ],
                }
            )

    if draw_calls > 420 or entity_count > int(700 * profile_scale):
        suggestions.append(
            {
                "id": "sg-005",
                "priority": 2,
                "title": "Enable distance LOD and culling overrides",
                "summary": "Large kit-bashed scene is above conservative draw/entity budgets.",
                "safety": "safe",
                "confidence": 0.82,
                "impact": "high",
                "estimated_win": {"draw_call_reduction_pct": 18.0, "cpu_update_reduction_pct": 10.0},
                "preview": "Enables runtime LOD distance culling with conservative thresholds.",
                "patch": [
                    {"op": "set", "path": "/optimization_overrides/runtime/enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/lod_distance_culling_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/lod_near_distance_m", "value": 14},
                    {"op": "set", "path": "/optimization_overrides/runtime/lod_far_distance_m", "value": 36},
                    {"op": "set", "path": "/optimization_overrides/runtime/sprite_cull_distance_m", "value": 46},
                    {"op": "set", "path": "/optimization_overrides/runtime/mesh_cull_distance_m", "value": 64},
                ],
            }
        )

    if vram_mb > 0 and vram_mb > 2048:
        suggestions.append(
            {
                "id": "sg-006",
                "priority": 3,
                "title": "Generate texture atlas + compression manifests",
                "summary": "VRAM usage indicates atlas/compression should be enabled for approved sprites.",
                "safety": "safe",
                "confidence": 0.79,
                "impact": "medium",
                "estimated_win": {"vram_reduction_mb": 320.0},
                "preview": "Turns on runtime atlas and texture compression overrides.",
                "patch": [
                    {"op": "set", "path": "/optimization_overrides/runtime/enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/texture_atlas_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/texture_compression_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/memory_guardrails_enabled", "value": True},
                ],
            }
        )

    if fps_avg < target_fps:
        suggestions.append(
            {
                "id": "sg-007",
                "priority": 3,
                "title": "Enable shader variant cache warm-up",
                "summary": "Compile only used shader variants and cache binaries from generated manifest.",
                "safety": "safe",
                "confidence": 0.77,
                "impact": "medium",
                "estimated_win": {"first_frame_hitch_reduction_ms": 10.0},
                "preview": "Enables shader_variant_cache_enabled under runtime overrides.",
                "patch": [
                    {"op": "set", "path": "/optimization_overrides/runtime/enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/shader_variant_cache_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/draw_call_batching_enabled", "value": True},
                ],
            }
        )

    suggested_lightweight_mode = "balanced"
    if fps_avg > 0 and (fps_avg < target_fps - 5.0 or draw_calls > 520 or vram_mb > 3200):
        suggested_lightweight_mode = "performance"
    elif fps_avg >= target_fps + 8.0 and draw_calls < 320 and vram_mb > 0 and vram_mb < 2300:
        suggested_lightweight_mode = "quality"

    if suggested_lightweight_mode != scene_mode:
        mode_patch: list[dict[str, object]] = [
            {"op": "set", "path": "/optimization_overrides/lightweight_mode", "value": suggested_lightweight_mode},
            {"op": "set", "path": "/optimization_overrides/project_health_score", "value": score},
            {"op": "set", "path": "/optimization_overrides/runtime/enabled", "value": True},
            {"op": "set", "path": "/optimization_overrides/runtime/memory_guardrails_enabled", "value": True},
        ]
        if suggested_lightweight_mode == "performance":
            mode_patch.extend(
                [
                    {"op": "set", "path": "/optimization_overrides/runtime/lod_distance_culling_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/draw_call_batching_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/pruning/post_processing_profile", "value": "light"},
                    {"op": "set", "path": "/optimization_overrides/pruning/particle_weather_mode", "value": "sprite_sheet_overlay"},
                ]
            )
        elif suggested_lightweight_mode == "quality":
            mode_patch.extend(
                [
                    {"op": "set", "path": "/optimization_overrides/runtime/lod_distance_culling_enabled", "value": False},
                    {"op": "set", "path": "/optimization_overrides/runtime/draw_call_batching_enabled", "value": True},
                    {"op": "set", "path": "/optimization_overrides/runtime/shader_variant_cache_enabled", "value": True},
                ]
            )
        suggestions.append(
            {
                "id": "sg-010",
                "priority": 1,
                "title": f"Switch lightweight mode to '{suggested_lightweight_mode}'",
                "summary": (
                    f"ForgeGuard recommendation for target '{target_profile}' from perf history + change log. "
                    f"Current={scene_mode}, suggested={suggested_lightweight_mode}."
                ),
                "safety": "safe",
                "confidence": 0.84 if forgeguard_available else 0.69,
                "impact": "high",
                "estimated_win": {
                    "fps_target_gap": round(target_fps - fps_avg, 2),
                    "draw_call_pressure": draw_calls,
                    "vram_usage_mb": round(vram_mb, 1),
                },
                "preview": "Applies lightweight preset plus safe P5/P6 overrides. User confirmation required.",
                "patch": mode_patch,
            }
        )

    suggestions.append(
        {
            "id": "sg-900",
            "priority": 99,
            "title": "Stamp optimization checkpoint metadata",
            "summary": "Tracks local checkpoint metadata for the next critique pass.",
            "safety": "safe",
            "confidence": 0.9,
            "impact": "low",
            "estimated_win": {"regression_detection": "improved"},
            "preview": "Writes optimization_overrides.last_checkpoint_utc and score baseline.",
            "patch": [
                {"op": "set", "path": "/optimization_overrides/last_checkpoint_utc", "value": datetime.now(timezone.utc).isoformat()},
                {"op": "set", "path": "/optimization_overrides/baseline_health_score", "value": score},
                {"op": "set", "path": "/optimization_overrides/project_health_score", "value": score},
            ],
        }
    )
    pruned = sorted(pruning_candidates, key=lambda item: int(item.get("priority", 99)))[:4]
    suggestions.extend(pruned)

    # Pass 3: refine into lighter JSON patch set and keep highest impact first.
    refined_suggestions = sorted(suggestions, key=lambda item: int(item.get("priority", 99)))
    refined_suggestions = refined_suggestions[: max(3, min(max_suggestions, 5))]
    for suggestion in refined_suggestions:
        suggestion["passes"] = ["pass-1-context", "pass-2-critique", "pass-3-refine"]

    return {
        "scene_path": str(scene_path),
        "health_score": score,
        "project_health_score": score,
        "lightweight_mode": scene_mode,
        "lightweight_mode_suggestion": {
            "current": scene_mode,
            "suggested": suggested_lightweight_mode,
            "reason": "forgeguard-heuristic" if forgeguard_available else "heuristic-fallback",
            "requires_confirmation": True,
        },
        "guardrails": {
            "soft_warning_threshold": soft_warning_threshold,
            "hard_block_threshold": hard_block_threshold,
            "hard_block_enabled": hard_block_enabled,
            "status": "hard_block" if hard_block_enabled and score <= hard_block_threshold else "warning" if score <= soft_warning_threshold else "healthy",
        },
        "health_summary": {
            "target_profile": target_profile,
            "target_fps": target_fps,
            "fps_avg": fps_avg,
            "fps_delta": fps_delta,
            "draw_calls": draw_calls,
            "vram_usage_mb": vram_mb,
            "update_time_ms": update_time_ms,
            "quality_score": quality_score_value,
        },
        "recent_changes": recent_changes,
        "suggestions": refined_suggestions,
        "pruning_suggestions": pruned,
        "source_model": "forgeguard" if forgeguard_available else "heuristic-fallback",
        "critique_passes": {
            "pass_1": pass_one_context,
            "pass_2": {"findings": critique_findings, "model": "forgeguard" if forgeguard_available else "heuristic"},
            "prune_pass": {
                "candidate_count": len(pruning_candidates),
                "selected_count": len(pruned),
                "model": "forgeguard" if forgeguard_available else "heuristic",
            },
            "pass_3": {"refined_suggestion_count": len(refined_suggestions), "max_suggestions": max_suggestions},
        },
        "signals": {
            "has_performance_history": bool(snapshots),
            "has_changes_log": has_changes_log,
            "has_quality_metadata": isinstance(quality_metadata, dict) and bool(quality_metadata),
        },
    }


def build_runtime_optimization_assets(scene_path: Path) -> dict[str, object]:
    """Build lightweight atlas + shader variant manifests for runtime optimization overrides."""
    scene_payload = json.loads(scene_path.read_text(encoding="utf-8"))
    if not isinstance(scene_payload, dict):
        raise ValueError("Scene payload must be a JSON object")

    project_root = scene_path.resolve().parent.parent
    approved_root = project_root / "Assets" / "Approved"
    atlas_root = project_root / "Assets" / "Generated" / "runtime_atlas"
    shader_root = project_root / "Assets" / "Generated" / "shaders"
    atlas_root.mkdir(parents=True, exist_ok=True)
    shader_root.mkdir(parents=True, exist_ok=True)

    render_2d = scene_payload.get("render_2d", {}) if isinstance(scene_payload.get("render_2d"), dict) else {}
    sprites = render_2d.get("sprites", []) if isinstance(render_2d.get("sprites"), list) else []
    target_profile = "balanced"
    history_path = project_root / "performance_history.json"
    if history_path.exists():
        try:
            history_payload = json.loads(history_path.read_text(encoding="utf-8"))
            snapshots = history_payload.get("snapshots", []) if isinstance(history_payload, dict) else []
            if isinstance(snapshots, list) and snapshots:
                target_profile = str(snapshots[-1].get("target_hardware_profile", target_profile) or target_profile)
        except (json.JSONDecodeError, OSError):
            pass

    sprite_usage: dict[str, int] = defaultdict(int)
    for sprite in sprites:
        if not isinstance(sprite, dict):
            continue
        asset_id = str(sprite.get("asset_id", "") or "").strip()
        if asset_id:
            sprite_usage[asset_id] += 1

    atlas_groups: dict[str, list[str]] = defaultdict(list)
    extensions = {".png", ".jpg", ".jpeg"}
    for asset_id, usage in sorted(sprite_usage.items(), key=lambda item: (-item[1], item[0])):
        matches = []
        if approved_root.exists():
            for ext in extensions:
                candidate = approved_root / f"{asset_id}{ext}"
                if candidate.exists():
                    matches.append(candidate)
        if not matches:
            continue
        bucket = "core" if usage >= 8 else "secondary"
        atlas_groups[bucket].append(matches[0].relative_to(project_root).as_posix())

    compression = {"potato": "etc2", "balanced": "bc3", "high_fidelity": "bc7"}.get(target_profile, "bc3")
    atlas_manifest = {
        "schema": "gameforge.runtime_texture_atlas.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "target_hardware_profile": target_profile,
        "compression": compression,
        "atlas_groups": [{"name": name, "sources": sources} for name, sources in sorted(atlas_groups.items())],
    }
    atlas_manifest_path = atlas_root / "atlas_manifest.v1.json"
    atlas_manifest_path.write_text(json.dumps(atlas_manifest, indent=2) + "\n", encoding="utf-8")

    variants = [{"key": "base", "spv": "vertex.vert.spv"}, {"key": "lit", "spv": "fragment.frag.spv"}]
    post = scene_payload.get("post_processing")
    if isinstance(post, dict) and bool(post.get("enabled")):
        if bool(post.get("bloom_enabled", True)):
            variants.append({"key": "bloom_extract", "spv": "bloom_extract.frag.spv"})
            variants.append({"key": "gaussian_blur", "spv": "gaussian_blur.frag.spv"})
        variants.append({"key": "combine_tonemap", "spv": "combine_tonemap.frag.spv"})
    shader_manifest = {
        "schema": "gameforge.shader_variant_cache.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "variants": variants,
    }
    shader_manifest_path = shader_root / "shader_variant_cache.v1.json"
    shader_manifest_path.write_text(json.dumps(shader_manifest, indent=2) + "\n", encoding="utf-8")

    return {
        "scene_path": str(scene_path),
        "atlas_manifest": str(atlas_manifest_path),
        "shader_manifest": str(shader_manifest_path),
        "target_hardware_profile": target_profile,
        "atlas_group_count": len(atlas_manifest["atlas_groups"]),
        "shader_variant_count": len(variants),
    }


def _graphics_asset_roots(project_root: Path) -> tuple[Path, Path, Path]:
    assets_root = project_root / "Assets"
    return (
        assets_root / "Generated",
        assets_root / "Approved",
        assets_root / "Rejected",
    )


def _metadata_path_for_asset(asset_path: Path) -> Path:
    return asset_path.with_suffix(".metadata.json")


def _resolve_asset_project_root(asset_path: Path) -> Path:
    candidate = asset_path.resolve()
    for parent in [candidate.parent, *candidate.parents]:
        if parent.name == "Assets":
            return parent.parent
    return Path.cwd()


def _normalize_decision(decision: str) -> str:
    normalized = decision.strip().lower()
    if normalized not in {"approve", "reject", "regenerate"}:
        raise ValueError("decision must be one of: approve, reject, regenerate")
    return normalized


def _load_or_create_asset_metadata(asset_path: Path) -> tuple[Path, dict[str, object]]:
    metadata_path = _metadata_path_for_asset(asset_path)
    if metadata_path.exists():
        payload = json.loads(metadata_path.read_text(encoding="utf-8"))
        if not isinstance(payload, dict):
            raise ValueError(f"Asset metadata must be a JSON object: {metadata_path}")
    else:
        payload = {
            "schema": "gameforge.generated-graphic-asset.v1",
            "generated": True,
            "generated_at_utc": _utc_now_iso(),
            "output_path": str(asset_path),
        }
    return metadata_path, payload


def move_to_approved(asset_path: str, reviewer: str = "local-user") -> AssetReviewResult:
    source = Path(asset_path).expanduser().resolve()
    if not source.exists():
        raise FileNotFoundError(f"Asset file does not exist: {source}")

    project_root = _resolve_asset_project_root(source)
    _, approved_root, _ = _graphics_asset_roots(project_root)
    approved_root.mkdir(parents=True, exist_ok=True)
    destination = approved_root / source.name
    metadata_source_path, metadata_payload = _load_or_create_asset_metadata(source)
    destination_metadata = _metadata_path_for_asset(destination)

    source.replace(destination)
    if metadata_source_path.exists():
        metadata_source_path.replace(destination_metadata)

    reviewed_at = _utc_now_iso()
    metadata_payload["output_path"] = str(destination)
    metadata_payload["review"] = {
        "status": "approved",
        "decision": "approve",
        "reviewer": reviewer,
        "timestamp_utc": reviewed_at,
    }
    metadata_payload["review_status"] = "approved"
    metadata_payload["approved_for_runtime"] = True
    metadata_payload["production_ready"] = True
    _write_json(destination_metadata, metadata_payload)

    return AssetReviewResult(
        reviewed=True,
        decision="approve",
        source_asset_path=str(source),
        destination_asset_path=str(destination),
        metadata_path=str(destination_metadata),
        review_status="approved",
        reviewer=reviewer,
        reviewed_at_utc=reviewed_at,
        production_ready=True,
    )


def move_to_rejected(asset_path: str, reviewer: str = "local-user", *, decision: str = "reject") -> AssetReviewResult:
    normalized_decision = _normalize_decision(decision)
    source = Path(asset_path).expanduser().resolve()
    if not source.exists():
        raise FileNotFoundError(f"Asset file does not exist: {source}")

    project_root = _resolve_asset_project_root(source)
    _, _, rejected_root = _graphics_asset_roots(project_root)
    rejected_root.mkdir(parents=True, exist_ok=True)
    destination = rejected_root / source.name
    metadata_source_path, metadata_payload = _load_or_create_asset_metadata(source)
    destination_metadata = _metadata_path_for_asset(destination)

    source.replace(destination)
    if metadata_source_path.exists():
        metadata_source_path.replace(destination_metadata)

    reviewed_at = _utc_now_iso()
    review_status = "rejected" if normalized_decision == "reject" else "regenerate-requested"
    metadata_payload["output_path"] = str(destination)
    metadata_payload["review"] = {
        "status": review_status,
        "decision": normalized_decision,
        "reviewer": reviewer,
        "timestamp_utc": reviewed_at,
    }
    metadata_payload["review_status"] = review_status
    metadata_payload["approved_for_runtime"] = False
    metadata_payload["production_ready"] = False
    _write_json(destination_metadata, metadata_payload)

    return AssetReviewResult(
        reviewed=True,
        decision=normalized_decision,
        source_asset_path=str(source),
        destination_asset_path=str(destination),
        metadata_path=str(destination_metadata),
        review_status=review_status,
        reviewer=reviewer,
        reviewed_at_utc=reviewed_at,
        production_ready=False,
    )


def review_asset(asset_path: str, decision: str = "approve", reviewer: str = "local-user") -> AssetReviewResult:
    normalized_decision = _normalize_decision(decision)
    if normalized_decision == "approve":
        return move_to_approved(asset_path, reviewer=reviewer)
    if normalized_decision in {"reject", "regenerate"}:
        return move_to_rejected(asset_path, reviewer=reviewer, decision=normalized_decision)
    raise ValueError("Unsupported review decision")


def generate_asset(prompt: str, art_bible_path: Path | None = None, type: str = "sprite", count: int = 1) -> GeneratedGraphicAssetResult:
    normalized_type = type.strip().lower()
    if normalized_type not in {"sprite", "texture", "ui"}:
        raise ValueError("asset_type must be one of: sprite, texture, ui")
    prompt_clean = prompt.strip()
    if not prompt_clean:
        raise ValueError("prompt must be non-empty")

    resolved_art_bible_path = art_bible_path or (Path.cwd() / "art_bible.json")
    if resolved_art_bible_path.exists():
        art_bible = ArtBible.from_json_file(resolved_art_bible_path)
        art_bible_source: str | None = str(resolved_art_bible_path)
    else:
        art_bible = default_art_bible(project_name=Path.cwd().name or "Soul Loom Project")
        art_bible_source = None
    enhanced_prompt = art_bible.enhance_prompt(prompt_clean)
    safe_count = max(1, int(count))

    seed = int(os.environ.get("GAMEFORGE_GRAPHICS_SEED", "0")) or int(time.time()) % 2_147_483_647
    generated_root, approved_root, rejected_root = _graphics_asset_roots(Path.cwd())
    generated_root.mkdir(parents=True, exist_ok=True)
    approved_root.mkdir(parents=True, exist_ok=True)
    rejected_root.mkdir(parents=True, exist_ok=True)
    file_stem = f"{normalized_type}-{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')}-{seed}"
    backend_mode = os.environ.get("GAMEFORGE_GRAPHICS_BACKEND", "debug-local").strip().lower()
    output_extension = ".png" if backend_mode == "comfyui" else ".svg"
    output_path = generated_root / f"{file_stem}{output_extension}"

    batch = batch_generate([prompt_clean], art_bible=art_bible, count=safe_count)
    first_variant = batch["items"][0]["variants"][0] if batch.get("items") else {"seed": seed, "variant_index": 0}
    primary_seed = int(first_variant.get("seed", seed))
    if backend_mode == "comfyui":
        backend, model = _generate_with_comfyui(enhanced_prompt, primary_seed, output_path, normalized_type)
    elif backend_mode == "debug-local":
        backend, model = _generate_with_debug_backend(enhanced_prompt, primary_seed, output_path, normalized_type)
    else:
        raise ValueError("Unsupported GAMEFORGE_GRAPHICS_BACKEND. Supported values: comfyui, debug-local")

    quality = quality_score({"prompt": prompt_clean, "enhanced_prompt": enhanced_prompt, "dimensions": {"width": 1024, "height": 1024}}, art_bible=art_bible)
    consistency = consistency_score({"prompt": prompt_clean, "enhanced_prompt": enhanced_prompt}, art_bible=art_bible)
    asset_quality_score = float(quality.get("score", 0))
    asset_consistency_score = float(consistency.get("score", 0))
    metadata_path = generated_root / f"{file_stem}.metadata.json"
    variant_group_id = f"{normalized_type}-{batch.get('shared_seed', primary_seed)}"
    variants_written: list[dict[str, object]] = []
    for variant in batch["items"][0]["variants"]:
        variant_seed = int(variant.get("seed", primary_seed))
        variant_index = int(variant.get("variant_index", 0))
        if variant_index == 0:
            variant_output_path = output_path
        else:
            variant_stem = f"{file_stem}-v{variant_index + 1}"
            variant_ext = ".png" if backend_mode == "comfyui" else ".svg"
            variant_output_path = generated_root / f"{variant_stem}{variant_ext}"
            if backend_mode == "comfyui":
                _generate_with_comfyui(enhanced_prompt, variant_seed, variant_output_path, normalized_type)
            else:
                _generate_with_debug_backend(enhanced_prompt, variant_seed, variant_output_path, normalized_type)
        variants_written.append(
            {
                "variant_index": variant_index,
                "seed": variant_seed,
                "output_path": str(variant_output_path),
                "consistency_score": variant.get("consistency_score", asset_consistency_score),
                "consistency_components": variant.get("consistency_components", consistency.get("components", {})),
                "control_signature": variant.get("control_signature", ""),
            }
        )

    metadata_payload = {
        "schema": "gameforge.generated-graphic-asset.v1",
        "generated": True,
        "generated_at_utc": _utc_now_iso(),
        "asset_type": normalized_type,
        "output_path": str(output_path),
        "prompt": prompt_clean,
        "enhanced_prompt": enhanced_prompt,
        "seed": primary_seed,
        "backend": backend,
        "model": model,
        "art_bible_path": art_bible_source,
        "art_bible": art_bible.to_dict(),
        "quality_score": asset_quality_score,
        "consistency_score": asset_consistency_score,
        "consistency_components": consistency.get("components", {}),
        "quality_components": quality.get("components", {}),
        "estimated_vram_mb": quality.get("estimated_vram_mb", 0.0),
        "variant_group_id": variant_group_id,
        "variant_count": safe_count,
        "variant_index": 0,
        "shared_seed": batch.get("shared_seed", primary_seed),
        "control_profile": batch.get("control_profile", {}),
        "hooks": batch.get("hooks", {}),
        "variants": variants_written,
        "review": default_asset_review_metadata(),
        "review_status": "pending-review",
        "approved_for_runtime": False,
        "production_ready": False,
    }
    _write_json(metadata_path, metadata_payload)

    return GeneratedGraphicAssetResult(
        generated=True,
        asset_type=normalized_type,
        output_path=str(output_path),
        metadata_path=str(metadata_path),
        backend=backend,
        model=model,
        seed=primary_seed,
        prompt=prompt_clean,
        enhanced_prompt=enhanced_prompt,
        art_bible_path=art_bible_source,
        quality_score=asset_quality_score,
        consistency_score=asset_consistency_score,
        variant_group_id=variant_group_id,
        variant_count=safe_count,
        variant_index=0,
        generated_at_utc=metadata_payload["generated_at_utc"],
    )


def _run_stage_hook(hook_command: str | None, stage: StageDefinition, status: PipelineStageStatus, output_root: Path) -> None:
    if not hook_command:
        return
    env = os.environ.copy()
    env["GAMEFORGE_PIPELINE_STAGE_ID"] = stage.stage_id
    env["GAMEFORGE_PIPELINE_STAGE_TITLE"] = stage.title
    env["GAMEFORGE_PIPELINE_STAGE_STATUS"] = status.value
    env["GAMEFORGE_PIPELINE_OUTPUT_ROOT"] = str(output_root)
    subprocess.run(hook_command, shell=True, check=False, env=env)


def _default_bot_scenario(prototype_root: Path) -> Path:
    scenario = {
        "schema": "gameforge.bot_playtest.scenario.v1",
        "scenario_id": "pipeline-default-smoke",
        "title": "Pipeline default smoke validation",
        "max_runtime_seconds": 60,
        "probes": [
            {"probe_id": "manifest-exists", "probe_type": "file_exists", "target": "prototype-manifest.json", "expected": True},
            {
                "probe_id": "rendering-direction",
                "probe_type": "json_field_equals",
                "target": "prototype-manifest.json:rendering",
                "expected": "vulkan-first",
            },
            {
                "probe_id": "single-player-only",
                "probe_type": "json_field_equals",
                "target": "prototype-manifest.json:scope",
                "expected": "single-player baseline",
            },
        ],
    }
    path = prototype_root / "testing" / "pipeline-default-scenario.v1.json"
    _write_json(path, scenario)
    return path


def _evaluate_commercial_policy(brief: dict[str, object]) -> dict[str, object]:
    commercial = bool(brief.get("commercial", False))
    monetization = str(brief.get("monetization", "")).strip().lower()
    acknowledged = bool(brief.get("commercial_policy_acknowledged", False))
    trigger_reason = "paid_or_mtx" if commercial or monetization in {"paid", "mtx"} else "none"
    blocking_issues: list[str] = []
    if trigger_reason != "none" and not acknowledged:
        blocking_issues.append("Commercial policy acknowledgment is required before export.")
    return {
        "commercial_triggered": trigger_reason != "none",
        "trigger_reason": trigger_reason,
        "policy_acknowledged": acknowledged,
        "revenue_share_policy": "5% after first $1,000 gross revenue per game",
        "blocking_issues": blocking_issues,
    }


def _execute_generation_pipeline(
    brief_path: Path,
    output_root: Path,
    *,
    hook_command: str | None = None,
    playtest_scenario_path: Path | None = None,
    launch_runtime: bool = True,
) -> PipelineExecutionResult:
    brief = json.loads(brief_path.read_text(encoding="utf-8-sig"))
    pipeline_dir = output_root / "pipeline"
    pipeline_dir.mkdir(parents=True, exist_ok=True)
    benchmark_result = run_benchmark_as_dict(orchestrator_file=Path(__file__).resolve(), auto_prepare_models=True)
    tracker = OperationFailureTracker()
    stage_results: list[PipelineStageResult] = []
    dead_end_blockers: list[str] = []
    prototype_root: Path | None = None
    runtime_launch_status = "skipped"
    runtime_launch_pid: int | None = None
    runtime_launch_manifest_path: str | None = None
    runtime_launch_executable_path: str | None = None

    def execute_stage(
        stage: StageDefinition,
        action: callable,
    ) -> tuple[PipelineStageStatus, dict[str, object], list[str]]:
        started_at = _utc_now_iso()
        try:
            metadata, artifacts = action()
            fallback = tracker.record_result(stage.stage_id, success=True)
            status = PipelineStageStatus.PASSED
            summary = f"{stage.title} completed."
        except Exception as exc:  # noqa: BLE001
            metadata = {"error": str(exc)}
            artifacts = []
            fallback = tracker.record_result(stage.stage_id, success=False)
            status = PipelineStageStatus.FAILED
            summary = f"{stage.title} failed: {exc}"

        completed_at = _utc_now_iso()
        _run_stage_hook(hook_command, stage, status, output_root)
        stage_results.append(
            PipelineStageResult(
                stage_id=stage.stage_id,
                stage_title=stage.title,
                status=status.value,
                summary=summary,
                started_at_utc=started_at,
                completed_at_utc=completed_at,
                artifacts=artifacts,
                fallback_state=fallback,
                metadata=metadata,
            )
        )
        return status, metadata, artifacts

    def stage_story_analysis() -> tuple[dict[str, object], list[str]]:
        story = {
            "schema": "gameforge.pipeline.story_analysis.v1",
            "concept": str(brief.get("concept", "Soul Loom Prototype")).strip(),
            "narrative_weight": brief.get("narrative", {}),
            "constraints": {"single_player_only": True, "local_first": True},
            "generated_at_utc": _utc_now_iso(),
        }
        artifact = pipeline_dir / "01_story_analysis.v1.json"
        _write_json(artifact, story)
        return story, [str(artifact)]

    def stage_concept_doc() -> tuple[dict[str, object], list[str]]:
        concept = str(brief.get("concept", "Soul Loom Prototype")).strip()
        mechanics = brief.get("mechanics", {})
        markdown = "\n".join(
            [
                f"# Concept Doc: {concept}",
                "",
                "## Core Loop",
                f"- {mechanics.get('core_loop', 'Gather -> Build -> Progress')}",
                "",
                "## Platform and Runtime Defaults",
                "- Single-player only",
                "- Local-first workflow",
                "- Vulkan-first rendering direction",
            ]
        )
        artifact = pipeline_dir / "02_concept_doc.md"
        _write_text(artifact, markdown + "\n")
        return {"concept": concept}, [str(artifact)]

    def stage_asset_planning() -> tuple[dict[str, object], list[str]]:
        policy = _evaluate_commercial_policy(brief)
        plan = {
            "schema": "gameforge.pipeline.asset_planning.v1",
            "allowed_licenses": sorted(ALLOWED_LICENSES),
            "blocked_licenses": sorted(BLOCKED_LICENSES),
            "commercial_policy": policy,
            "generated_at_utc": _utc_now_iso(),
        }
        artifact = pipeline_dir / "03_asset_plan.v1.json"
        _write_json(artifact, plan)
        if policy["blocking_issues"]:
            raise ValueError("; ".join(policy["blocking_issues"]))
        return plan, [str(artifact)]

    def stage_code_generation() -> tuple[dict[str, object], list[str]]:
        nonlocal prototype_root
        prototype_root = _generate_prototype(brief_path, output_root)
        return {"prototype_root": str(prototype_root)}, [str(prototype_root)]

    def stage_integration() -> tuple[dict[str, object], list[str]]:
        if prototype_root is None:
            raise ValueError("Prototype root missing from code generation stage.")
        integration_manifest = {
            "schema": "gameforge.pipeline.integration.v1",
            "prototype_root": str(prototype_root),
            "integration_targets": ["runtime", "editor-shell", "asset-catalog"],
            "future_hooks": ["csharp_editor_entrypoint", "native_runtime_bridge"],
            "generated_at_utc": _utc_now_iso(),
        }
        artifact = prototype_root / "pipeline" / "05_integration_manifest.v1.json"
        _write_json(artifact, integration_manifest)
        return integration_manifest, [str(artifact)]

    def stage_bot_validation() -> tuple[dict[str, object], list[str]]:
        nonlocal dead_end_blockers
        if prototype_root is None:
            raise ValueError("Prototype root missing from code generation stage.")
        scenario_path = playtest_scenario_path or _default_bot_scenario(prototype_root)
        result, report, report_json_path, report_markdown_path = run_bot_playtest_with_report(
            prototype_root,
            scenario_path,
        )
        dead_end_blockers = list(result.critical_dead_end_blockers)
        metadata = {
            "status": result.status,
            "human_review_required": result.human_review_required,
            "critical_dead_end_blockers_count": len(dead_end_blockers),
        }
        stage_status = PipelineStageStatus.NEEDS_HUMAN_REVIEW if result.human_review_required else (
            PipelineStageStatus.FAILED if result.status == "failed" else PipelineStageStatus.PASSED
        )
        if stage_status != PipelineStageStatus.PASSED:
            raise ValueError(report.summary)
        return metadata, [str(scenario_path), str(report_json_path), str(report_markdown_path)]

    def stage_export() -> tuple[dict[str, object], list[str]]:
        if prototype_root is None:
            raise ValueError("Prototype root missing from code generation stage.")
        artifact = prototype_root / "pipeline" / "07_export_manifest.v1.json"
        scene_payload = json.loads((prototype_root / "scene" / "scene_scaffold.json").read_text(encoding="utf-8"))
        asset_plan_path = prototype_root / "pipeline" / "03_asset_plan.v1.json"
        if asset_plan_path.exists():
            asset_plan_payload = json.loads(asset_plan_path.read_text(encoding="utf-8"))
        else:
            fallback_asset_plan_path = pipeline_dir / "03_asset_plan.v1.json"
            asset_plan_payload = json.loads(fallback_asset_plan_path.read_text(encoding="utf-8"))
            asset_plan_path.parent.mkdir(parents=True, exist_ok=True)
            _write_json(asset_plan_path, asset_plan_payload)
        generated_runtime = _render_generated_runtime_templates(
            prototype_root=prototype_root,
            concept=str(brief.get("concept", "Soul Loom Prototype")),
            scene=scene_payload,
            asset_plan=asset_plan_payload,
        )
        export_manifest = {
            "schema": "gameforge.pipeline.export.v1",
            "prototype_root": str(prototype_root),
            "dead_end_blockers": dead_end_blockers,
            "benchmark_state_path": benchmark_result.get("state_path"),
            "generated_runtime": generated_runtime,
            "generated_at_utc": _utc_now_iso(),
        }
        _write_json(artifact, export_manifest)
        return export_manifest, [str(artifact), str(prototype_root / "generated" / "generated_runtime_manifest.v1.json")]

    stage_actions = {
        "story_analysis": stage_story_analysis,
        "concept_doc": stage_concept_doc,
        "asset_planning": stage_asset_planning,
        "code_generation": stage_code_generation,
        "integration": stage_integration,
        "bot_validation": stage_bot_validation,
        "export": stage_export,
    }

    for stage in PIPELINE_STAGE_ORDER:
        status, _, _ = execute_stage(stage, stage_actions[stage.stage_id])
        if status == PipelineStageStatus.FAILED:
            break

    commercial_policy = _evaluate_commercial_policy(brief)
    pipeline_status = "failed" if any(item.status == PipelineStageStatus.FAILED.value for item in stage_results) else "passed"
    if pipeline_status == "passed" and launch_runtime and prototype_root is not None:
        export_manifest_path = prototype_root / "pipeline" / "07_export_manifest.v1.json"
        try:
            launch_result = _build_and_launch_generated_runtime(
                prototype_root=prototype_root,
                manifest_path=export_manifest_path,
            )
        except Exception as exc:  # noqa: BLE001 - pipeline should fail gracefully with structured output
            launch_result = {
                "status": "failed",
                "pid": None,
                "manifest_path": str(export_manifest_path),
                "executable_path": None,
                "error": f"runtime_launch_unhandled_error: {exc}",
            }
        runtime_launch_status = launch_result["status"]
        runtime_launch_pid = launch_result["pid"]
        runtime_launch_manifest_path = launch_result["manifest_path"]
        runtime_launch_executable_path = launch_result["executable_path"]
        if runtime_launch_status != "launched":
            launch_error = str(launch_result.get("error", "unknown runtime launch error"))
            print(f"[Orchestrator][ERROR] Runtime launch failed: {launch_error}")
            if stage_results:
                latest_result = stage_results[-1]
                updated_metadata = dict(latest_result.metadata)
                updated_metadata["runtime_launch_error"] = launch_error
                stage_results[-1] = replace(latest_result, metadata=updated_metadata)
            pipeline_status = "failed"
    elif launch_runtime and prototype_root is None:
        runtime_launch_status = "failed"
    elif launch_runtime:
        runtime_launch_status = "skipped"

    csharp_shell_example = (
        "python ai-orchestration/python/orchestrator.py "
        f"--run-generation-pipeline --generate-prototype {brief_path} --output {output_root} --launch-runtime"
    )
    return PipelineExecutionResult(
        schema="gameforge.pipeline.execution.v1",
        pipeline_id=f"pipeline-{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')}",
        status=pipeline_status,
        brief_path=str(brief_path),
        output_root=str(output_root),
        prototype_root=str(prototype_root) if prototype_root else None,
        benchmark=benchmark_result,
        stage_results=stage_results,
        dead_end_blockers=dead_end_blockers,
        runtime_launch_status=runtime_launch_status,
        runtime_launch_pid=runtime_launch_pid,
        runtime_launch_manifest_path=runtime_launch_manifest_path,
        runtime_launch_executable_path=runtime_launch_executable_path,
        commercial_policy_checks=commercial_policy,
        csharp_shell_example=csharp_shell_example,
        completed_at_utc=_utc_now_iso(),
    )


def _generate_prototype(brief_path: Path, output_dir: Path) -> Path:
    brief = json.loads(brief_path.read_text(encoding="utf-8-sig"))
    concept = brief.get("concept", "Soul Loom Prototype")
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
        "player_spawn": {"x": 0, "y": 0, "z": 0},
        "camera": {"mode": "third_person", "follow_player": True},
        "world_notes": narrative.get("world_notes", ""),
        "npcs": [{"id": "npc_01", "spawn_x": 0.35, "spawn_y": -0.15}],
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
    std::cout << "Soul Loom prototype runtime (C++ baseline)\\n";
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


def _resolve_generated_runner_path(generated_build_root: Path) -> Path:
    if os.name == "nt":
        candidates = [
            generated_build_root / "Debug" / "generated_gameplay_runner.exe",
            generated_build_root / "Release" / "generated_gameplay_runner.exe",
            generated_build_root / "generated_gameplay_runner.exe",
        ]
    else:
        candidates = [generated_build_root / "generated_gameplay_runner"]

    for candidate in candidates:
        if candidate.exists():
            return candidate
    raise FileNotFoundError(
        f"Generated gameplay executable missing. Looked for: {', '.join(str(item) for item in candidates)}"
    )


def _build_and_launch_generated_runtime(
    *,
    prototype_root: Path,
    manifest_path: Path,
) -> dict[str, object]:
    generated_root = prototype_root / "generated"
    generated_build_root = generated_root / "build"

    if not generated_root.exists():
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": f"Generated root missing: {generated_root}",
        }

    if not manifest_path.exists():
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": f"Manifest path missing: {manifest_path}",
        }

    try:
        configure_proc = subprocess.run(
            ["cmake", "-S", str(generated_root), "-B", str(generated_build_root)],
            cwd=prototype_root,
            text=True,
            capture_output=True,
        )
    except OSError as exc:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": f"cmake_configure_execution_failed: {exc}",
        }
    if configure_proc.returncode != 0:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": "cmake_configure_failed",
            "stdout": configure_proc.stdout,
            "stderr": configure_proc.stderr,
        }

    try:
        build_proc = subprocess.run(
            ["cmake", "--build", str(generated_build_root)],
            cwd=prototype_root,
            text=True,
            capture_output=True,
        )
    except OSError as exc:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": f"cmake_build_execution_failed: {exc}",
        }
    if build_proc.returncode != 0:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": "cmake_build_failed",
            "stdout": build_proc.stdout,
            "stderr": build_proc.stderr,
        }

    try:
        executable_path = _resolve_generated_runner_path(generated_build_root)
    except FileNotFoundError as exc:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": None,
            "error": str(exc),
        }

    launch_command = [str(executable_path), "--manifest", str(manifest_path)]
    popen_kwargs: dict[str, object] = {
        "cwd": str(prototype_root),
        "stdout": subprocess.DEVNULL,
        "stderr": subprocess.DEVNULL,
    }
    if os.name == "nt":
        popen_kwargs["creationflags"] = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        popen_kwargs["start_new_session"] = True

    try:
        process = subprocess.Popen(launch_command, **popen_kwargs)  # noqa: S603
    except OSError as exc:
        return {
            "status": "failed",
            "pid": None,
            "manifest_path": str(manifest_path),
            "executable_path": str(executable_path),
            "error": f"runtime_launch_execution_failed: {exc}",
        }

    print(f"[Orchestrator][INFO] Runtime launch succeeded: pid={process.pid}, manifest={manifest_path}")
    return {
        "status": "launched",
        "pid": process.pid,
        "manifest_path": str(manifest_path),
        "executable_path": str(executable_path),
    }


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


def _scan_critical_dead_end_blockers(prototype_root: Path) -> list[str]:
    quest_graph_path = prototype_root / "systems" / "rpg" / "quest_dialogue_framework.v1.json"
    if not quest_graph_path.exists():
        return [f"{quest_graph_path.relative_to(prototype_root)}: quest graph file is missing"]

    try:
        payload = json.loads(quest_graph_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        return [f"{quest_graph_path.relative_to(prototype_root)}: malformed JSON ({exc.msg})"]

    quests = payload.get("quests")
    dialogue_nodes = payload.get("dialogue_nodes")
    blockers: list[str] = []

    if not isinstance(quests, list) or not quests:
        return [f"{quest_graph_path.relative_to(prototype_root)}: quests must be a non-empty list"]
    if not isinstance(dialogue_nodes, list) or not dialogue_nodes:
        return [f"{quest_graph_path.relative_to(prototype_root)}: dialogue_nodes must be a non-empty list"]

    node_by_id: dict[str, dict[str, object]] = {}
    duplicate_ids: set[str] = set()
    for raw_node in dialogue_nodes:
        if not isinstance(raw_node, dict):
            blockers.append("dialogue_nodes: each entry must be an object")
            continue
        node_id = str(raw_node.get("node_id", "")).strip()
        if not node_id:
            blockers.append("dialogue_nodes: node_id is required")
            continue
        if node_id in node_by_id:
            duplicate_ids.add(node_id)
        node_by_id[node_id] = raw_node
    for node_id in sorted(duplicate_ids):
        blockers.append(f"dialogue_nodes.{node_id}: duplicate node_id")

    if not node_by_id:
        return [*blockers, "dialogue_nodes: no valid node_id entries found"]

    for raw_quest in quests:
        if not isinstance(raw_quest, dict):
            blockers.append("quests: each entry must be an object")
            continue
        quest_id = str(raw_quest.get("quest_id", "")).strip() or "<unknown-quest>"
        start_node_id = str(raw_quest.get("start_node_id", "")).strip()
        completion_nodes = raw_quest.get("completion_nodes")

        if not start_node_id:
            blockers.append(f"{quest_id}: start_node_id is required")
            continue
        if start_node_id not in node_by_id:
            blockers.append(f"{quest_id}: start_node_id '{start_node_id}' does not exist in dialogue_nodes")
            continue
        if not isinstance(completion_nodes, list) or not completion_nodes:
            blockers.append(f"{quest_id}: completion_nodes must be a non-empty list")
            continue

        completion_set = {str(node_id).strip() for node_id in completion_nodes if str(node_id).strip()}
        if not completion_set:
            blockers.append(f"{quest_id}: completion_nodes must contain valid node ids")
            continue
        for completion_node_id in sorted(completion_set):
            if completion_node_id not in node_by_id:
                blockers.append(f"{quest_id}: completion node '{completion_node_id}' does not exist in dialogue_nodes")

        stack = [start_node_id]
        visited: set[str] = set()
        reachable_completion = False
        while stack:
            node_id = stack.pop()
            if node_id in visited:
                continue
            visited.add(node_id)
            if node_id in completion_set:
                reachable_completion = True

            node = node_by_id.get(node_id)
            if not isinstance(node, dict):
                blockers.append(f"{quest_id}: node '{node_id}' is not a valid object")
                continue
            choices = node.get("choices", [])
            if not isinstance(choices, list):
                blockers.append(f"{quest_id}: node '{node_id}' choices must be a list")
                continue
            if not choices and node_id not in completion_set:
                blockers.append(f"{quest_id}: terminal node '{node_id}' is a non-completion dead-end")
                continue

            for choice in choices:
                if not isinstance(choice, dict):
                    blockers.append(f"{quest_id}: node '{node_id}' contains non-object choice entry")
                    continue
                next_node_id = str(choice.get("next_node_id", "")).strip()
                if not next_node_id:
                    blockers.append(f"{quest_id}: node '{node_id}' has choice without next_node_id")
                    continue
                if next_node_id not in node_by_id:
                    blockers.append(
                        f"{quest_id}: node '{node_id}' points to missing next_node_id '{next_node_id}'"
                    )
                    continue
                stack.append(next_node_id)

        if not reachable_completion:
            blockers.append(
                f"{quest_id}: no reachable completion path from start node '{start_node_id}'"
            )

    return sorted(set(blockers))


def run_bot_playtest_scenario(prototype_root: Path, scenario_path: Path) -> BotPlaytestResult:
    scenario = _read_bot_playtest_scenario(scenario_path)
    probe_results = [_run_bot_probe(prototype_root, probe) for probe in scenario.probes]
    critical_dead_end_blockers = _scan_critical_dead_end_blockers(prototype_root)
    if critical_dead_end_blockers:
        probe_results.append(
            BotPlaytestProbeResult(
                probe_id="critical-dead-end-blockers",
                status="failed",
                details=(
                    f"Detected {len(critical_dead_end_blockers)} critical dead-end blocker(s): "
                    + "; ".join(critical_dead_end_blockers)
                ),
                required=True,
            )
        )
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
        critical_dead_end_blockers=critical_dead_end_blockers,
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
        critical_dead_end_blockers_count=len(result.critical_dead_end_blockers),
        critical_dead_end_blockers=result.critical_dead_end_blockers,
        sections=built_sections,
        source_probe_results=result.probe_results,
    )


def _write_playtest_report_markdown(report: ActionablePlaytestReport, destination: Path) -> None:
    lines = [
        "# Soul Loom Playtest Report",
        "",
        f"- Report ID: `{report.report_id}`",
        f"- Scenario: `{report.scenario_id}`",
        f"- Generated (UTC): `{report.generated_at_utc}`",
        f"- Overall status: **{report.overall_status}**",
        f"- Critical dead-end blockers: **{report.critical_dead_end_blockers_count}**",
        "",
        f"## Summary",
        report.summary,
    ]
    if report.critical_dead_end_blockers:
        lines.extend(["", "## Critical Dead-End Blockers"])
        lines.extend([f"- {blocker}" for blocker in report.critical_dead_end_blockers])

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
    scene_path = prototype_root / "scene" / "scene_scaffold.json"
    if scene_path.exists():
        try:
            record_performance_snapshot(scene_path=scene_path, session_name="post_playtest")
        except Exception:  # noqa: BLE001 - background benchmark should never block playtest output
            pass
    return result, report, json_path, markdown_path


def _try_run_forge_hooks_cli(raw_args: list[str]) -> int | None:
    """Dispatch simple hook commands before argparse-based legacy flags."""
    if not raw_args:
        return None

    progress_json = "--progress-json" in raw_args
    cancel_file_path: Path | None = None
    normalized_args: list[str] = []
    skip_next = False
    for index, token in enumerate(raw_args):
        if skip_next:
            skip_next = False
            continue
        if token == "--progress-json":
            continue
        if token == "--cancel-file":
            if index + 1 >= len(raw_args):
                raise ValueError("Usage: --cancel-file <path>")
            cancel_file_path = Path(raw_args[index + 1])
            skip_next = True
            continue
        normalized_args.append(token)

    if not normalized_args:
        return None

    command = normalized_args[0]
    raw_args = normalized_args

    def emit_progress(payload: dict[str, object]) -> None:
        if not progress_json:
            return
        line = {"event": "progress", "type": payload.get("event", "update"), **payload}
        print(json.dumps(line), flush=True)

    def emit_error(error: ManagedModelDownloadError, command_name: str) -> None:
        payload = {
            "event": "error",
            "command": command_name,
            "error": error.to_payload(),
        }
        if progress_json:
            emit_progress(payload)
        else:
            print(json.dumps(payload, indent=2))

    def is_cancelled() -> bool:
        return bool(cancel_file_path and cancel_file_path.exists())

    def _append_change_log(
        *,
        scene_arg_index: int,
        action_type: str,
        prompt: str,
        affected_systems: list[str],
    ) -> None:
        try:
            append_change_log_entry(
                scene_path=Path(raw_args[scene_arg_index]),
                action=action_type,
                prompt=prompt,
                affected_systems=affected_systems,
            )
        except Exception:  # noqa: BLE001 - change-log write should never block generation path
            pass
    if command == "generate-dialog":
        if len(raw_args) < 4:
            raise ValueError("Usage: orchestrator.py generate-dialog <npc_name> <personality> <theme>")
        payload = generate_dialog_tree(raw_args[1], raw_args[2], raw_args[3])
        print(json.dumps(payload, indent=2))
        return 0

    if command == "generate-recipes":
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py generate-recipes <theme> [count]")
        count = int(raw_args[2]) if len(raw_args) >= 3 else 4
        payload = generate_recipes(raw_args[1], count)
        print(json.dumps(payload, indent=2))
        return 0

    if command == "add-npc":
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py add-npc <name> <role>")
        payload = generate_npc_with_dialog(raw_args[1], raw_args[2])
        print(json.dumps(payload, indent=2))
        return 0

    if command == "generate-building-templates":
        count = int(raw_args[1]) if len(raw_args) >= 2 else 3
        payload = generate_building_templates(count)
        print(json.dumps(payload, indent=2))
        return 0

    if command == "modify-scene":
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py modify-scene <scene_json_path> <instruction>")
        payload = apply_to_scene_file(raw_args[1], raw_args[2])
        print(json.dumps(payload, indent=2))
        return 0

    if command == "modify-scene-json":
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py modify-scene-json <scene_json_path> <instruction>")
        scene_payload = json.loads(Path(raw_args[1]).read_text(encoding="utf-8"))
        if not isinstance(scene_payload, dict):
            raise ValueError("Scene payload must be a JSON object")
        payload = modify_scene(scene_payload, raw_args[2])
        print(json.dumps(payload, indent=2))
        return 0

    if command == "co-creator-tick":
        if len(raw_args) < 6:
            raise ValueError(
                "Usage: orchestrator.py co-creator-tick <scene_json_path> <biome> <world_style_guide> <day_progress> <recent_actions_json> [economy_json] [current_weather]"
            )
        scene_payload = json.loads(Path(raw_args[1]).read_text(encoding="utf-8"))
        if not isinstance(scene_payload, dict):
            raise ValueError("Scene payload must be a JSON object")
        recent_actions_payload = json.loads(raw_args[5])
        if not isinstance(recent_actions_payload, list):
            raise ValueError("recent_actions_json must be a JSON list")
        payload = co_creator_tick(
            scene_payload,
            raw_args[2],
            raw_args[3],
            [str(item) for item in recent_actions_payload],
            float(raw_args[4]),
            raw_args[7] if len(raw_args) >= 8 else "sunny",
        )
        print(json.dumps(payload, indent=2))
        return 0

    if command == "create-art-bible":
        destination = Path(raw_args[1]) if len(raw_args) >= 2 else Path.cwd() / "art_bible.json"
        project_name = raw_args[2] if len(raw_args) >= 3 else destination.parent.name or "Soul Loom Project"
        art_bible = write_default_art_bible(destination, project_name=project_name)
        print(
            json.dumps(
                {
                    "created": True,
                    "path": str(destination),
                    "art_bible": art_bible.to_dict(),
                },
                indent=2,
            )
        )
        return 0

    if command == "enhance-prompt":
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py enhance-prompt <raw_prompt> [art_bible_json_path]")
        art_bible_path = Path(raw_args[2]) if len(raw_args) >= 3 else Path.cwd() / "art_bible.json"
        art_bible = ArtBible.from_json_file(art_bible_path)
        enhanced = art_bible.enhance_prompt(raw_args[1])
        print(
            json.dumps(
                {
                    "art_bible_path": str(art_bible_path),
                    "raw_prompt": raw_args[1],
                    "enhanced_prompt": enhanced,
                },
                indent=2,
            )
        )
        return 0

    if command == "generate-asset":
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py generate-asset <prompt> [type] [count] [art_bible_json_path]")
        asset_type = raw_args[2] if len(raw_args) >= 3 else "sprite"
        count = 1
        art_bible_path: Path | None = None
        if len(raw_args) >= 4:
            third = str(raw_args[3]).strip()
            if re.fullmatch(r"\d+", third):
                count = int(third)
                art_bible_path = Path(raw_args[4]) if len(raw_args) >= 5 else None
            else:
                art_bible_path = Path(raw_args[3])
        result = generate_asset(raw_args[1], art_bible_path=art_bible_path, type=asset_type, count=count)
        print(json.dumps(asdict(result), indent=2))
        return 0

    if command == "kit-bash-scene":
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py kit-bash-scene <scene_json_path> <prompt> [kits_json_path] [art_bible_json_path] [variant_count]")
        kits_path = Path(raw_args[3]) if len(raw_args) >= 4 else (Path.cwd() / "kits.json")
        art_bible_path = Path(raw_args[4]) if len(raw_args) >= 5 else (Path.cwd() / "art_bible.json")
        variant_count = int(raw_args[5]) if len(raw_args) >= 6 else 1
        result = apply_kit_bash_to_scene(
            Path(raw_args[1]),
            raw_args[2],
            art_bible_path=art_bible_path,
            kits_path=kits_path,
            variant_count=variant_count,
        )
        _append_change_log(
            scene_arg_index=1,
            action_type="kitbash",
            prompt=raw_args[2],
            affected_systems=["scene", "environment", "kitbash"],
        )
        print(json.dumps(result, indent=2))
        return 0

    if command in {"apply-variations", "/apply_variations"}:
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py /apply_variations <scene_json_path> <prompt> [art_bible_json_path]")
        art_bible_path = Path(raw_args[3]) if len(raw_args) >= 4 else (Path.cwd() / "art_bible.json")
        result = apply_variations_to_scene(
            Path(raw_args[1]),
            raw_args[2],
            art_bible_path=art_bible_path,
        )
        _append_change_log(
            scene_arg_index=1,
            action_type="optimize",
            prompt=raw_args[2],
            affected_systems=["scene", "visual_variations"],
        )
        print(json.dumps(result, indent=2))
        return 0

    if command in {"edit-scene", "/edit_scene"}:
        if len(raw_args) < 3:
            raise ValueError("Usage: orchestrator.py /edit_scene <scene_json_path> <prompt> [art_bible_json_path]")
        art_bible_path = Path(raw_args[3]) if len(raw_args) >= 4 else (Path.cwd() / "art_bible.json")
        result = edit_scene_from_prompt(
            Path(raw_args[1]),
            raw_args[2],
            art_bible_path=art_bible_path,
        )
        _append_change_log(
            scene_arg_index=1,
            action_type="edit",
            prompt=raw_args[2],
            affected_systems=["scene", "live_edit"],
        )
        print(json.dumps(result, indent=2))
        return 0

    if command in {"quality-scan", "/quality_scan"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /quality_scan <scene_json_path> [art_bible_json_path]")
        art_bible_path = Path(raw_args[2]) if len(raw_args) >= 3 else (Path.cwd() / "art_bible.json")
        result = quality_scan_scene(Path(raw_args[1]), art_bible_path=art_bible_path)
        print(json.dumps(result, indent=2))
        return 0

    if command in {"generate-loot", "/generate_loot"}:
        if len(raw_args) < 3:
            raise ValueError(
                "Usage: orchestrator.py /generate_loot <scene_json_path> <prompt> [count] [target_inventory] [template_type] [seed] [items_json_path] [art_bible_json_path]"
            )
        count = int(raw_args[3]) if len(raw_args) >= 4 else 1
        target_inventory = raw_args[4] if len(raw_args) >= 5 else "player"
        template_type = raw_args[5] if len(raw_args) >= 6 else "weapon"
        seed = int(raw_args[6]) if len(raw_args) >= 7 and str(raw_args[6]).strip() else None
        items_path = Path(raw_args[7]) if len(raw_args) >= 8 else (Path.cwd() / "items.json")
        art_bible_path = Path(raw_args[8]) if len(raw_args) >= 9 else (Path.cwd() / "art_bible.json")
        result = apply_generated_loot_to_scene(
            Path(raw_args[1]),
            raw_args[2],
            template_type=template_type,
            count=count,
            target_inventory=target_inventory,
            seed=seed,
            templates_path=items_path,
            art_bible_path=art_bible_path,
        )
        _append_change_log(
            scene_arg_index=1,
            action_type="generate",
            prompt=raw_args[2],
            affected_systems=["scene", "loot", "inventory"],
        )
        print(json.dumps(result, indent=2))
        return 0

    if command == "review-asset":
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py review-asset <asset_path> [decision]")
        decision = raw_args[2] if len(raw_args) >= 3 else "approve"
        reviewer = os.environ.get("GAMEFORGE_ASSET_REVIEWER", "local-user")
        result = review_asset(raw_args[1], decision=decision, reviewer=reviewer)
        print(json.dumps(asdict(result), indent=2))
        return 0

    if command in {"download-model", "/download_model"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /download_model <friendly_name> [quantization]")
        quantization = raw_args[2] if len(raw_args) >= 3 else "Q4_K_M"
        try:
            result = download_model(
                friendly_name=raw_args[1],
                quantization=quantization,
                progress_callback=emit_progress if progress_json else None,
                cancel_check=is_cancelled if cancel_file_path else None,
            )
            print(json.dumps(result, indent=2))
            return 0
        except DownloadCancelledError as exc:
            print(json.dumps({"event": "cancelled", "message": str(exc)}))
            return 130
        except ManagedModelDownloadError as exc:
            emit_error(exc, "download-model")
            return 1

    if command in {"list-models", "/list_models"}:
        result = list_installed_models()
        print(json.dumps({"models": result}, indent=2))
        return 0

    if command in {"onboarding-run", "/onboarding_run"}:
        try:
            result = run_onboarding(
                orchestrator_file=Path(__file__).resolve(),
                progress_callback=emit_progress if progress_json else None,
                cancel_check=is_cancelled if cancel_file_path else None,
            )
            print(json.dumps(result, indent=2))
            return 0
        except DownloadCancelledError as exc:
            print(json.dumps({"event": "cancelled", "message": str(exc)}))
            return 130
        except ManagedModelDownloadError as exc:
            emit_error(exc, "onboarding-run")
            return 1

    if command in {"quick-setup", "/quick_setup"}:
        try:
            result = run_quick_setup(
                orchestrator_file=Path(__file__).resolve(),
                progress_callback=emit_progress if progress_json else None,
                cancel_check=is_cancelled if cancel_file_path else None,
            )
            print(json.dumps(result, indent=2))
            return 0
        except DownloadCancelledError as exc:
            print(json.dumps({"event": "cancelled", "message": str(exc)}))
            return 130
        except ManagedModelDownloadError as exc:
            emit_error(exc, "quick-setup")
            return 1

    if command in {"setup-freewill", "/setup_freewill"}:
        scene_path = Path(raw_args[1]) if len(raw_args) >= 2 else None
        result = ensure_freewill_model(
            orchestrator_file=Path(__file__).resolve(),
            scene_path=scene_path,
        )
        print(json.dumps(result, indent=2))
        return 0

    if command in {"remove-model", "/remove_model"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /remove_model <friendly_name>")
        result = remove_model(raw_args[1])
        print(json.dumps(result, indent=2))
        return 0

    if command in {"benchmark-now", "/benchmark_now"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /benchmark_now <scene_json_path> [session_name]")
        session_name = raw_args[2] if len(raw_args) >= 3 else "manual"
        result = record_performance_snapshot(Path(raw_args[1]), session_name=session_name)
        print(json.dumps(result, indent=2))
        return 0

    if command in {"optimization-critique", "/optimization_critique", "critique-pass", "/critique_pass"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /optimization_critique <scene_json_path> [max_suggestions]")
        max_suggestions = int(raw_args[2]) if len(raw_args) >= 3 else 5
        result = optimization_critique(Path(raw_args[1]), max_suggestions=max_suggestions)
        print(json.dumps(result, indent=2))
        return 0

    if command in {"runtime-optimize-assets", "/runtime_optimize_assets"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /runtime_optimize_assets <scene_json_path>")
        result = build_runtime_optimization_assets(Path(raw_args[1]))
        print(json.dumps(result, indent=2))
        return 0

    if command in {"scene-saved", "/scene_saved"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /scene_saved <scene_json_path>")
        result = record_performance_snapshot(Path(raw_args[1]), session_name="scene_save")
        print(json.dumps(result, indent=2))
        return 0

    if command in {"orchestrate", "/orchestrate"}:
        if len(raw_args) < 3:
            raise ValueError(
                "Usage: orchestrator.py /orchestrate <narrative_checkpoint|npc_day|scene_review> <scene_json_path> [target]; "
                "npc_day target=<npc_id>, narrative_checkpoint target=<checkpoint>, "
                "scene_review target=<playtest|with_playtest|no_playtest>"
            )
        try:
            result = _build_orchestration_result(
                orchestration_type=raw_args[1],
                scene_path=Path(raw_args[2]),
                target=raw_args[3] if len(raw_args) >= 4 else None,
            )
            print(json.dumps({"orchestration_result": asdict(result)}, indent=2))
            return 0
        except ValueError as exc:
            print(
                json.dumps(
                    {
                        "event": "error",
                        "command": "/orchestrate",
                        "error": {
                            "code": "invalid_orchestration_request",
                            "message": str(exc),
                            "remediation": "Use /orchestrate <type> <scene_json_path> [target].",
                        },
                    },
                    indent=2,
                )
            )
            return 1

    if command in {"idle-tick", "/idle_tick"}:
        if len(raw_args) < 2:
            raise ValueError("Usage: orchestrator.py /idle_tick <scene_json_path> [interval_minutes]")
        scene_path = Path(raw_args[1])
        interval_minutes = int(raw_args[2]) if len(raw_args) >= 3 else 10
        if should_run_idle_benchmark(scene_path=scene_path, interval_minutes=interval_minutes):
            result = record_performance_snapshot(scene_path, session_name="idle")
            print(json.dumps(result, indent=2))
        else:
            print(json.dumps({"recorded": False, "reason": "idle_interval_not_reached"}, indent=2))
        return 0

    return None


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Soul Loom AI orchestration skeleton")
    parser.add_argument("--suggest-uncertain", dest="uncertain_input", help="User reply to evaluate for uncertainty")
    parser.add_argument("--think-for-me", dest="think_for_me_input", help="User reply to evaluate for think-for-me mode")
    parser.add_argument("--topic", default="game-direction", help="Interview topic for the option ids")
    parser.add_argument("--generate-prototype", dest="brief_path", help="Path to saved interview brief JSON")
    parser.add_argument(
        "--run-generation-pipeline",
        action="store_true",
        help="Run the 7-stage generation pipeline (Story→Concept→Asset→Code→Integration→Bot→Export)",
    )
    parser.add_argument("--output", default="build/generated-prototypes", help="Output directory for generated prototypes")
    parser.add_argument("--launch", action="store_true", help="Compile and launch generated prototype runtime")
    parser.add_argument(
        "--launch-runtime",
        action=argparse.BooleanOptionalAction,
        default=None,
        help="Build and launch generated/runtime executable after --run-generation-pipeline (default: enabled).",
    )
    parser.add_argument("--import-asset-manifest", help="Path to JSON list of asset import requests")
    parser.add_argument(
        "--export-attribution-bundle",
        action="store_true",
        help="Generate attribution.bundle.v1.json and attribution.md when attribution-required assets exist",
    )
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
    parser.add_argument("--pipeline-hook-cmd", help="Optional shell command invoked after each pipeline stage")
    parser.add_argument(
        "--prepare-models",
        action="store_true",
        help="Ensure local-first model files are present and print runtime assignment JSON",
    )
    parser.add_argument(
        "--benchmark",
        action="store_true",
        help="Run first-launch hardware benchmark and model recommendations",
    )
    parser.add_argument(
        "--benchmark-no-prepare",
        action="store_true",
        help="Run benchmark but skip automatic --prepare-models on first launch",
    )
    return parser.parse_args()


def main() -> int:
    hook_result = _try_run_forge_hooks_cli(sys.argv[1:])
    if hook_result is not None:
        return hook_result

    args = _parse_args()
    launch_runtime = args.launch_runtime if args.launch_runtime is not None else bool(args.run_generation_pipeline)


    if args.benchmark:
        try:
            result = run_benchmark_as_dict(
                orchestrator_file=Path(__file__).resolve(),
                auto_prepare_models=not args.benchmark_no_prepare,
            )
            print(json.dumps(result, indent=2))
            return 0
        except Exception as exc:  # noqa: BLE001 - keep subprocess output structured
            print(
                json.dumps(
                    {
                        "error": "benchmark_failed",
                        "message": str(exc),
                    },
                    indent=2,
                )
            )
            return 1

    if args.prepare_models:
        try:
            result = prepare_models_as_dict(orchestrator_file=Path(__file__).resolve())
            print(json.dumps(result, indent=2))
            return 0
        except Exception as exc:  # noqa: BLE001 - keep subprocess output structured
            print(
                json.dumps(
                    {
                        "error": "model_preparation_failed",
                        "message": str(exc),
                    },
                    indent=2,
                )
            )
            return 1

    if args.uncertain_input is not None:
        response = generate_uncertainty_options(args.uncertain_input, args.topic)
        print(json.dumps(asdict(response), indent=2))
        return 0

    if args.think_for_me_input is not None:
        response = generate_think_for_me_directions(args.think_for_me_input, args.topic)
        print(json.dumps(asdict(response), indent=2))
        return 0

    if args.run_generation_pipeline:
        if not args.brief_path:
            raise ValueError("--generate-prototype <brief.json> is required with --run-generation-pipeline")
        pipeline_result = _execute_generation_pipeline(
            brief_path=Path(args.brief_path),
            output_root=Path(args.output),
            hook_command=args.pipeline_hook_cmd,
            playtest_scenario_path=Path(args.bot_playtest_scenario) if args.bot_playtest_scenario else None,
            launch_runtime=launch_runtime,
        )
        print(json.dumps(asdict(pipeline_result), indent=2))
        return 0 if pipeline_result.status == "passed" else 1

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

    if args.export_attribution_bundle:
        if not args.project_root:
            raise ValueError("--project-root is required with --export-attribution-bundle")
        result = export_attribution_bundle(Path(args.project_root))
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

    print("Soul Loom AI orchestration skeleton (Python)")
    print("Local-first orchestration placeholder")
    print(
        "Console commands: /orchestrate <narrative_checkpoint|npc_day|scene_review> <scene_json_path> [target] "
        "(npc_day target=<npc_id>, narrative_checkpoint target=<checkpoint>, "
        "scene_review target=<playtest|with_playtest|no_playtest>)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
