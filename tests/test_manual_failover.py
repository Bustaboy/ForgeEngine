import importlib.util
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"

spec = importlib.util.spec_from_file_location("orchestrator", ORCHESTRATOR)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
spec.loader.exec_module(orchestrator)


class TestManualFailover(unittest.TestCase):
    def test_four_failures_do_not_offer_manual_fallback(self):
        tracker = orchestrator.OperationFailureTracker()
        state = None
        for _ in range(4):
            state = tracker.record_result("generate-prototype", success=False)

        self.assertIsNotNone(state)
        self.assertEqual(state.consecutive_failures, 4)
        self.assertFalse(state.fallback_offered)
        self.assertFalse(state.guided_manual_mode_available)
        self.assertTrue(state.retry_with_ai_available)
        self.assertEqual(state.retry_action, "try_ai_again")
        self.assertIsNone(state.manual_mode_action)

    def test_fifth_failure_offers_guided_manual_fallback_with_retry(self):
        tracker = orchestrator.OperationFailureTracker()
        for _ in range(4):
            tracker.record_result("generate-prototype", success=False)

        state = tracker.record_result("generate-prototype", success=False)

        self.assertEqual(state.consecutive_failures, 5)
        self.assertTrue(state.fallback_offered)
        self.assertTrue(state.guided_manual_mode_available)
        self.assertTrue(state.retry_with_ai_available)
        self.assertEqual(state.retry_action, "try_ai_again")
        self.assertEqual(state.manual_mode_action, "guided_manual_mode")

    def test_success_resets_failure_streak(self):
        tracker = orchestrator.OperationFailureTracker()
        for _ in range(4):
            tracker.record_result("style-match", success=False)

        success_state = tracker.record_result("style-match", success=True)
        self.assertEqual(success_state.consecutive_failures, 0)
        self.assertFalse(success_state.fallback_offered)

        failure_after_reset = tracker.record_result("style-match", success=False)
        self.assertEqual(failure_after_reset.consecutive_failures, 1)
        self.assertFalse(failure_after_reset.fallback_offered)

    def test_failure_streaks_are_scoped_to_operation_id(self):
        tracker = orchestrator.OperationFailureTracker()
        for _ in range(4):
            tracker.record_result("operation-a", success=False)

        operation_b = tracker.record_result("operation-b", success=False)
        self.assertEqual(operation_b.consecutive_failures, 1)
        self.assertFalse(operation_b.fallback_offered)

        operation_a = tracker.record_result("operation-a", success=False)
        self.assertEqual(operation_a.consecutive_failures, 5)
        self.assertTrue(operation_a.fallback_offered)


if __name__ == "__main__":
    unittest.main()
