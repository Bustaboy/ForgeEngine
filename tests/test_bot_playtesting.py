import json
import subprocess
import sys
import tempfile
import importlib.util
from pathlib import Path
import unittest


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"
SAMPLE_PROTOTYPE = REPO_ROOT / "app" / "samples" / "generated-prototype" / "cozy-colony-tales"
SAMPLE_SCENARIO = SAMPLE_PROTOTYPE / "testing" / "bot-baseline-scenario.v1.json"

spec = importlib.util.spec_from_file_location("orchestrator", ORCHESTRATOR)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
spec.loader.exec_module(orchestrator)


class TestBotPlaytesting(unittest.TestCase):
    def test_bot_playtest_passes_on_generated_sample(self):
        result = orchestrator.run_bot_playtest_scenario(SAMPLE_PROTOTYPE, SAMPLE_SCENARIO)
        self.assertEqual("passed", result.status)
        self.assertFalse(result.human_review_required)
        self.assertEqual([], result.inconclusive_reasons)
        self.assertTrue(all(item.status == "passed" for item in result.probe_results))

    def test_inconclusive_probe_triggers_human_review_flag(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            scenario_path = Path(temp_dir) / "scenario.json"
            scenario_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.bot_playtest_scenario.v1",
                        "scenario_id": "unsupported-probe",
                        "title": "Unsupported probe should escalate",
                        "max_runtime_seconds": 20,
                        "probes": [
                            {
                                "probe_id": "unsupported-check",
                                "probe_type": "not-supported-yet",
                                "target": "prototype-manifest.json",
                                "expected": True,
                                "required": True,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            result = orchestrator.run_bot_playtest_scenario(SAMPLE_PROTOTYPE, scenario_path)
            self.assertEqual("inconclusive", result.status)
            self.assertTrue(result.human_review_required)
            self.assertEqual(1, len(result.inconclusive_reasons))
            self.assertIn("unsupported-check", result.inconclusive_reasons[0])

    def test_cli_bot_playtest_outputs_structured_result(self):
        proc = subprocess.run(
            [
                sys.executable,
                str(ORCHESTRATOR),
                "--prototype-root",
                str(SAMPLE_PROTOTYPE),
                "--bot-playtest-scenario",
                str(SAMPLE_SCENARIO),
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, proc.returncode, proc.stdout + proc.stderr)
        payload = json.loads(proc.stdout)
        self.assertEqual("cozy-colony-baseline", payload["bot_playtest_result"]["scenario_id"])
        self.assertEqual("passed", payload["bot_playtest_result"]["status"])
        self.assertIn("probe_results", payload["bot_playtest_result"])
        self.assertEqual("gameforge.playtest_report.v1", payload["actionable_report"]["schema"])
        section_ids = [section["section_id"] for section in payload["actionable_report"]["sections"]]
        self.assertEqual(["progression", "economy", "dead-end", "pacing", "performance"], section_ids)
        report_json = Path(payload["report_paths"]["json"])
        report_markdown = Path(payload["report_paths"]["markdown"])
        self.assertTrue(report_json.exists())
        self.assertTrue(report_markdown.exists())

    def test_file_exists_requires_boolean_expected_value(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            scenario_path = Path(temp_dir) / "scenario.json"
            scenario_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.bot_playtest_scenario.v1",
                        "scenario_id": "file-exists-non-bool-expected",
                        "title": "Non-boolean expected should be inconclusive",
                        "max_runtime_seconds": 20,
                        "probes": [
                            {
                                "probe_id": "bad-expected-type",
                                "probe_type": "file_exists",
                                "target": "does-not-exist.txt",
                                "expected": "false",
                                "required": True,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            result = orchestrator.run_bot_playtest_scenario(SAMPLE_PROTOTYPE, scenario_path)
            self.assertEqual("inconclusive", result.status)
            self.assertTrue(result.human_review_required)
            self.assertIn("expected must be boolean", result.inconclusive_reasons[0])

    def test_malformed_json_in_json_field_equals_is_inconclusive(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            prototype_root = Path(temp_dir) / "prototype"
            prototype_root.mkdir(parents=True, exist_ok=True)
            (prototype_root / "broken.json").write_text("{invalid-json", encoding="utf-8")
            scenario_path = Path(temp_dir) / "scenario.json"
            scenario_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.bot_playtest_scenario.v1",
                        "scenario_id": "malformed-json-escalation",
                        "title": "Malformed JSON should escalate",
                        "max_runtime_seconds": 20,
                        "probes": [
                            {
                                "probe_id": "broken-json-check",
                                "probe_type": "json_field_equals",
                                "target": "broken.json:any.path",
                                "expected": "value",
                                "required": True,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            result = orchestrator.run_bot_playtest_scenario(prototype_root, scenario_path)
            self.assertEqual("inconclusive", result.status)
            self.assertTrue(result.human_review_required)
            self.assertIn("Malformed JSON", result.inconclusive_reasons[0])


if __name__ == "__main__":
    unittest.main()
