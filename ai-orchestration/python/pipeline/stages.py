"""Stable stage definitions for the Soul Loom generation pipeline."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class StageDefinition:
    stage_id: str
    title: str
    description: str


PIPELINE_STAGE_ORDER: tuple[StageDefinition, ...] = (
    StageDefinition(
        stage_id="story_analysis",
        title="Story Analysis",
        description="Normalize intent, fantasy, and narrative constraints from the source brief.",
    ),
    StageDefinition(
        stage_id="concept_doc",
        title="Concept Doc",
        description="Generate a concise concept packet for downstream generation stages.",
    ),
    StageDefinition(
        stage_id="asset_planning",
        title="Asset Planning",
        description="Plan initial content scope and enforce asset/commercial policy constraints.",
    ),
    StageDefinition(
        stage_id="code_generation",
        title="Code Generation",
        description="Generate baseline prototype scaffold and runtime stubs.",
    ),
    StageDefinition(
        stage_id="integration",
        title="Integration",
        description="Connect generated systems and capture integration manifests for editor/runtime handoff.",
    ),
    StageDefinition(
        stage_id="bot_validation",
        title="Bot Validation",
        description="Run bot validation and dead-end blocker detection before export.",
    ),
    StageDefinition(
        stage_id="export",
        title="Export",
        description="Emit export-ready package metadata and shell handoff contract.",
    ),
)

