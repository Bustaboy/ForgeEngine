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
    def test_uncategorized_probes_do_not_drop_progression_keyword_matches(self):
        result = orchestrator.BotPlaytestResult(
            scenario_id="classification-check",
            prototype_root="prototype",
            status="failed",
            human_review_required=False,
            summary="Synthetic report for categorization coverage",
            completed_at_utc="2026-03-22T00:00:00+00:00",
            probe_results=[
                orchestrator.BotPlaytestProbeResult(
                    probe_id="level-curve-too-steep",
                    status="failed",
                    details="XP gain stalls in midgame.",
                    required=True,
                ),
                orchestrator.BotPlaytestProbeResult(
                    probe_id="uncategorized-balance-check",
                    status="passed",
                    details="General smoke signal.",
                    required=True,
                ),
            ],
            inconclusive_reasons=[],
        )

        report = orchestrator.generate_actionable_playtest_report(result)
        progression = next(section for section in report.sections if section.section_id == "progression")

        self.assertEqual("action-needed", progression.status)
        self.assertTrue(any("level-curve-too-steep" in finding for finding in progression.findings))

    def test_bot_playtest_passes_on_generated_sample(self):
        result = orchestrator.run_bot_playtest_scenario(SAMPLE_PROTOTYPE, SAMPLE_SCENARIO)
        self.assertEqual("passed", result.status)
        self.assertFalse(result.human_review_required)
        self.assertEqual([], result.inconclusive_reasons)
        self.assertEqual([], result.critical_dead_end_blockers)
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
        self.assertEqual(0, payload["actionable_report"]["critical_dead_end_blockers_count"])
        self.assertEqual([], payload["actionable_report"]["critical_dead_end_blockers"])
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

    def test_dead_end_scanner_fails_when_non_completion_terminal_is_reachable(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            prototype_root = Path(temp_dir) / "prototype"
            graph_path = prototype_root / "systems" / "rpg" / "quest_dialogue_framework.v1.json"
            graph_path.parent.mkdir(parents=True, exist_ok=True)
            graph_path.write_text(
                json.dumps(
                    {
                        "quests": [
                            {
                                "quest_id": "q_blocked",
                                "start_node_id": "start",
                                "completion_nodes": ["complete"],
                            }
                        ],
                        "dialogue_nodes": [
                            {"node_id": "start", "choices": [{"next_node_id": "dead_end"}]},
                            {"node_id": "dead_end", "choices": []},
                            {"node_id": "complete", "choices": []},
                        ],
                    }
                ),
                encoding="utf-8",
            )
            scenario_path = Path(temp_dir) / "scenario.json"
            scenario_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.bot_playtest_scenario.v1",
                        "scenario_id": "dead-end-scan-fail",
                        "title": "Dead-end scan fail",
                        "max_runtime_seconds": 20,
                        "probes": [
                            {
                                "probe_id": "smoke-file",
                                "probe_type": "file_exists",
                                "target": "systems/rpg/quest_dialogue_framework.v1.json",
                                "expected": True,
                                "required": True,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = orchestrator.run_bot_playtest_scenario(prototype_root, scenario_path)
            self.assertEqual("failed", result.status)
            self.assertGreater(len(result.critical_dead_end_blockers), 0)
            self.assertTrue(any("non-completion dead-end" in blocker for blocker in result.critical_dead_end_blockers))
            self.assertTrue(any(probe.probe_id == "critical-dead-end-blockers" for probe in result.probe_results))

            report = orchestrator.generate_actionable_playtest_report(result)
            self.assertEqual(len(result.critical_dead_end_blockers), report.critical_dead_end_blockers_count)
            self.assertTrue(report.critical_dead_end_blockers)

    def test_dead_end_scanner_handles_malformed_graph_as_blocker(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            prototype_root = Path(temp_dir) / "prototype"
            graph_path = prototype_root / "systems" / "rpg" / "quest_dialogue_framework.v1.json"
            graph_path.parent.mkdir(parents=True, exist_ok=True)
            graph_path.write_text("{invalid-json", encoding="utf-8")

            scenario_path = Path(temp_dir) / "scenario.json"
            scenario_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.bot_playtest_scenario.v1",
                        "scenario_id": "dead-end-scan-malformed",
                        "title": "Dead-end malformed graph",
                        "max_runtime_seconds": 20,
                        "probes": [
                            {
                                "probe_id": "smoke-file",
                                "probe_type": "file_exists",
                                "target": "systems/rpg/quest_dialogue_framework.v1.json",
                                "expected": True,
                                "required": True,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = orchestrator.run_bot_playtest_scenario(prototype_root, scenario_path)
            self.assertEqual("failed", result.status)
            self.assertTrue(any("malformed JSON" in blocker for blocker in result.critical_dead_end_blockers))


if __name__ == "__main__":
    unittest.main()
