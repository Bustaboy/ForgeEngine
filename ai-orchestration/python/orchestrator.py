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


def _escape_cpp_string_literal(value: object) -> str:
    text = str(value or "")
    text = text.replace("\\", "\\\\")
    text = text.replace('"', '\\"')
    text = text.replace("\n", "\\n")
    text = text.replace("\r", "\\r")
    text = text.replace("\t", "\\t")
    return text


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

    _write_text(prototype_root / "prototype-manifest.json", json.dumps(manifest, indent=2))
    _write_text(prototype_root / "scene" / "scene_scaffold.json", json.dumps(scene, indent=2))
    _write_text(prototype_root / "scripts" / "player_controller.json", json.dumps(player_controller, indent=2))
    _write_text(prototype_root / "ui" / "hud_layout.json", json.dumps(ui_layout, indent=2))
    _write_text(prototype_root / "save" / "savegame_hook.json", json.dumps(save_stub, indent=2))

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
    std::ifstream player("scripts/player_controller.json");
    std::ifstream ui("ui/hud_layout.json");
    std::ifstream save("save/savegame_hook.json");

    if (!scene.good() || !player.good() || !ui.good() || !save.good()) {{
        std::cerr << "Missing generated scaffold files.\\n";
        return 2;
    }}

    std::cout << "Scene scaffold loaded.\\n";
    std::cout << "Player controller loaded.\\n";
    std::cout << "Basic UI loaded.\\n";
    std::cout << "Save/load hook loaded.\\n";
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
- Player control stub (`scripts/player_controller.json`)
- Basic UI config (`ui/hud_layout.json`)
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


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="GameForge V1 AI orchestration skeleton")
    parser.add_argument("--suggest-uncertain", dest="uncertain_input", help="User reply to evaluate for uncertainty")
    parser.add_argument("--think-for-me", dest="think_for_me_input", help="User reply to evaluate for think-for-me mode")
    parser.add_argument("--topic", default="game-direction", help="Interview topic for the option ids")
    parser.add_argument("--generate-prototype", dest="brief_path", help="Path to saved interview brief JSON")
    parser.add_argument("--output", default="build/generated-prototypes", help="Output directory for generated prototypes")
    parser.add_argument("--launch", action="store_true", help="Compile and launch generated prototype runtime")
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

    print("GameForge V1 AI orchestration skeleton (Python)")
    print("Local-first orchestration placeholder")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
