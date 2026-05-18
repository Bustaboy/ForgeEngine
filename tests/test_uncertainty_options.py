import unittest
from pathlib import Path
import importlib.util
import sys


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR_PATH = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"

spec = importlib.util.spec_from_file_location("orchestrator", ORCHESTRATOR_PATH)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
spec.loader.exec_module(orchestrator)

THINK_FOR_ME_TOPICS = ("concept", "style", "mechanics", "genre", "tone")


class TestUncertaintyOptions(unittest.TestCase):
    def test_ambiguous_unknown_input_returns_exactly_three_options(self):
        response = orchestrator.generate_uncertainty_options("unknown", topic="style")
        self.assertTrue(response.ambiguous)
        self.assertEqual(len(response.options), 3)

    def test_blank_input_returns_exactly_three_options_with_tradeoffs(self):
        response = orchestrator.generate_uncertainty_options("   ", topic="mechanics")
        self.assertEqual(len(response.options), 3)
        self.assertTrue(all(option.tradeoff.strip() for option in response.options))

    def test_clear_input_does_not_return_uncertainty_options(self):
        response = orchestrator.generate_uncertainty_options("I want base building with quest hubs", topic="genre")
        self.assertFalse(response.ambiguous)
        self.assertEqual(response.options, [])

    def test_think_for_me_mode_returns_three_directional_concepts(self):
        response = orchestrator.generate_think_for_me_directions("think for me", topic="concept")
        self.assertTrue(response.triggered)
        self.assertTrue(response.confirmation_required)
        self.assertEqual(len(response.proposals), 3)
        self.assertTrue(response.human_summary_markdown.strip())

    def test_think_for_me_mode_requires_trigger_phrase(self):
        response = orchestrator.generate_think_for_me_directions("I already know what I want", topic="concept")
        self.assertFalse(response.triggered)
        self.assertEqual(response.proposals, [])

    def test_think_for_me_coherence_across_five_topics(self):
        for topic in THINK_FOR_ME_TOPICS:
            with self.subTest(topic=topic):
                response = orchestrator.generate_think_for_me_directions("think for me", topic=topic)
                self.assertTrue(response.triggered)
                self.assertTrue(response.confirmation_required)
                self.assertEqual(len(response.proposals), 3)

                direction_ids = [proposal.direction_id for proposal in response.proposals]
                titles = [proposal.title for proposal in response.proposals]
                self.assertEqual(len(set(direction_ids)), 3)
                self.assertEqual(len(set(titles)), 3)

                style_presets = []
                genre_weight_sets = []
                for proposal in response.proposals:
                    self.assertTrue(proposal.elevator_pitch.strip())
                    self.assertTrue(proposal.tradeoff.strip())
                    self.assertGreaterEqual(len(proposal.gameplay_pillars), 1)
                    self.assertTrue(all(pillar.strip() for pillar in proposal.gameplay_pillars))
                    self.assertEqual(proposal.prototype_seed.get("rendering"), "vulkan-first")
                    style_presets.append(proposal.prototype_seed.get("style_preset"))
                    genre_weight_sets.append(tuple(sorted(proposal.prototype_seed.get("genre_weights", {}).items())))

                self.assertEqual(len(set(style_presets)), 3)
                self.assertEqual(len(set(genre_weight_sets)), 3)
                self.assertIn("confirm", response.human_summary_markdown.lower())


if __name__ == "__main__":
    unittest.main()
