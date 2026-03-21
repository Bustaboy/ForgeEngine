#!/usr/bin/env python3
"""GameForge V1 AI orchestration entrypoint and interview helpers."""

from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass


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


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="GameForge V1 AI orchestration skeleton")
    parser.add_argument("--suggest-uncertain", dest="uncertain_input", help="User reply to evaluate for uncertainty")
    parser.add_argument("--think-for-me", dest="think_for_me_input", help="User reply to evaluate for think-for-me mode")
    parser.add_argument("--topic", default="game-direction", help="Interview topic for the option ids")
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

    print("GameForge V1 AI orchestration skeleton (Python)")
    print("Local-first orchestration placeholder")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
