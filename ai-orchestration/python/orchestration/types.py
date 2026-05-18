"""Dataclasses and enums for orchestration results, pipelines, assets, and playtests."""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum

from orchestration.constants import MANUAL_FALLBACK_FAILURE_THRESHOLD


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
