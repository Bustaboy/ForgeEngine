#!/usr/bin/env python3
"""Art bible helpers for consistent graphics prompting in local orchestration."""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class ArtBible:
    """Project-level graphics constraints for AI prompt enhancement."""

    schema: str
    project_name: str
    art_direction: str
    rendering_keywords: list[str]
    palette_keywords: list[str]
    camera_guidance: list[str]
    lighting_guidance: list[str]
    composition_guidance: list[str]
    negative_prompt: list[str]

    @classmethod
    def from_dict(cls, payload: dict[str, object]) -> "ArtBible":
        return cls(
            schema=str(payload.get("schema", "gameforge.art-bible.v1")),
            project_name=str(payload.get("project_name", "GameForge Project")),
            art_direction=str(payload.get("art_direction", "stylized indie 3D")),
            rendering_keywords=[str(item) for item in payload.get("rendering_keywords", [])],
            palette_keywords=[str(item) for item in payload.get("palette_keywords", [])],
            camera_guidance=[str(item) for item in payload.get("camera_guidance", [])],
            lighting_guidance=[str(item) for item in payload.get("lighting_guidance", [])],
            composition_guidance=[str(item) for item in payload.get("composition_guidance", [])],
            negative_prompt=[str(item) for item in payload.get("negative_prompt", [])],
        )

    @classmethod
    def from_json_file(cls, path: Path) -> "ArtBible":
        payload = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(payload, dict):
            raise ValueError("Art bible JSON must be a JSON object")
        return cls.from_dict(payload)

    def to_dict(self) -> dict[str, object]:
        return asdict(self)

    def enhance_prompt(self, raw_prompt: str) -> str:
        """Enhance a raw prompt using this art bible instance."""
        return enhance_prompt(raw_prompt, self)


def default_art_bible(project_name: str = "GameForge Project") -> ArtBible:
    return ArtBible(
        schema="gameforge.art-bible.v1",
        project_name=project_name,
        art_direction="stylized indie 3D with readable silhouettes",
        rendering_keywords=[
            "stylized PBR",
            "clean topology",
            "high readability",
            "hero prop focus",
        ],
        palette_keywords=[
            "warm midtones",
            "cool shadow accents",
            "saturated focal color",
        ],
        camera_guidance=[
            "gameplay-first framing",
            "clear horizon separation",
            "avoid extreme lens distortion",
        ],
        lighting_guidance=[
            "single dominant key light",
            "soft ambient fill",
            "contact shadows for depth",
        ],
        composition_guidance=[
            "strong foreground-midground-background layering",
            "guide eye toward interactable elements",
            "limit clutter around objective path",
        ],
        negative_prompt=[
            "photoreal skin pores",
            "noisy compression artifacts",
            "muddy silhouettes",
            "illegible UI-like textures",
        ],
    )


def enhance_prompt(raw_prompt: str, art_bible: ArtBible) -> str:
    cleaned_prompt = raw_prompt.strip()
    if not cleaned_prompt:
        raise ValueError("raw_prompt must be non-empty")

    sections = [
        cleaned_prompt,
        f"Art Direction: {art_bible.art_direction}",
        f"Rendering: {', '.join(art_bible.rendering_keywords)}",
        f"Palette: {', '.join(art_bible.palette_keywords)}",
        f"Camera: {', '.join(art_bible.camera_guidance)}",
        f"Lighting: {', '.join(art_bible.lighting_guidance)}",
        f"Composition: {', '.join(art_bible.composition_guidance)}",
        f"Negative Prompt: {', '.join(art_bible.negative_prompt)}",
    ]
    return "\n".join(section for section in sections if section.strip())


def write_default_art_bible(destination: Path, project_name: str = "GameForge Project", overwrite: bool = False) -> ArtBible:
    if destination.exists() and not overwrite:
        raise FileExistsError(f"Art bible already exists at {destination}")
    art_bible = default_art_bible(project_name=project_name)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(art_bible.to_dict(), indent=2) + "\n", encoding="utf-8")
    return art_bible


def default_asset_review_metadata() -> dict[str, object]:
    """Default review block for newly generated graphics assets."""
    return {
        "status": "pending-review",
        "decision": "pending",
        "reviewer": "",
        "timestamp_utc": None,
    }
