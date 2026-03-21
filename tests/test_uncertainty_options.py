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


if __name__ == "__main__":
    unittest.main()
