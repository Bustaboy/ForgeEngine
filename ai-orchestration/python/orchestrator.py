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


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="GameForge V1 AI orchestration skeleton")
    parser.add_argument("--suggest-uncertain", dest="uncertain_input", help="User reply to evaluate for uncertainty")
    parser.add_argument("--topic", default="game-direction", help="Interview topic for the option ids")
    return parser.parse_args()


def main() -> int:
    args = _parse_args()

    if args.uncertain_input is not None:
        response = generate_uncertainty_options(args.uncertain_input, args.topic)
        print(json.dumps(asdict(response), indent=2))
        return 0

    print("GameForge V1 AI orchestration skeleton (Python)")
    print("Local-first orchestration placeholder")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
