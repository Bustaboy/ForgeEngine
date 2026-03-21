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


if __name__ == "__main__":
    unittest.main()
