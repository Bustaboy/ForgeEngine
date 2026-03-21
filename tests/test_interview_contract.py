import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = REPO_ROOT / "app" / "schemas" / "interview-session.v1.schema.json"
EDITOR_CSPROJ = REPO_ROOT / "editor" / "csharp" / "GameForge.Editor.csproj"
EDITOR_TEST_CSPROJ = REPO_ROOT / "editor" / "csharp" / "tests" / "GameForge.Editor.Tests.csproj"


def run_cmd(cmd, cwd=REPO_ROOT):
    return subprocess.run(cmd, cwd=cwd, text=True, capture_output=True)


class TestInterviewContract(unittest.TestCase):
    def test_interview_json_schema_contract(self):
        payload = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

        self.assertEqual(payload["$schema"], "https://json-schema.org/draft/2020-12/schema")
        self.assertEqual(payload["properties"]["schema_version"]["const"], 1)

        required = set(payload["required"])
        self.assertIn("concept", required)
        self.assertIn("genre_weights", required)
        self.assertIn("mechanics", required)
        self.assertIn("narrative", required)
        self.assertIn("style", required)
        self.assertIn("constraints", required)
        self.assertIn("uncertainty_decisions", required)

        constraints = payload["properties"]["constraints"]["properties"]
        self.assertIn("target_platforms", constraints)
        self.assertEqual(
            constraints["target_platforms"]["items"]["enum"],
            ["windows", "ubuntu"],
        )

        options = payload["properties"]["uncertainty_decisions"]["items"]["properties"]["options"]
        self.assertEqual(options["minItems"], 3)
        self.assertEqual(options["maxItems"], 3)

    def test_editor_csharp_owns_interview_session_persistence_contract(self):
        contract_text = (REPO_ROOT / "editor" / "csharp" / "Interview" / "InterviewSessionContract.cs").read_text(encoding="utf-8")
        store_text = (REPO_ROOT / "editor" / "csharp" / "Interview" / "InterviewSessionStore.cs").read_text(encoding="utf-8")

        self.assertIn("public sealed record InterviewSession", contract_text)
        self.assertIn("public sealed record UncertaintyDecision", contract_text)
        self.assertIn("public static class InterviewSessionStore", store_text)
        self.assertIn("SaveAsync", store_text)
        self.assertIn("LoadAsync", store_text)
        self.assertIn("RequireOnlyProperties", store_text)
        self.assertIn("RequireStringArray", store_text)
        self.assertIn("ValidateUncertaintyDecisionModel", store_text)
        self.assertIn("Unsupported platform", store_text)

    def test_dotnet_smoke_round_trip_if_available(self):
        if not shutil.which("dotnet"):
            self.skipTest("dotnet SDK not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            session_path = Path(temp_dir) / "session.json"
            proc = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(EDITOR_CSPROJ),
                "--",
                "--interview-persistence-smoke",
                str(session_path),
            ])

            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            self.assertIn("Interview persistence smoke test passed.", proc.stdout)

            session_payload = json.loads(session_path.read_text(encoding="utf-8"))
            self.assertEqual(session_payload["schema_version"], 1)
            self.assertEqual(session_payload["concept"], "Smoke test concept")
            self.assertEqual(session_payload["uncertainty_decisions"], [])

    def test_dotnet_rejects_malformed_payload_if_available(self):
        if not shutil.which("dotnet"):
            self.skipTest("dotnet SDK not available")

        malformed_payload = {
            "schema_version": 1,
            "session_id": "bad-session",
            "created_at_utc": "2026-01-01T00:00:00Z",
            "updated_at_utc": "2026-01-01T00:00:00Z",
            "concept": "invalid",
            "genre_weights": {"rts_sim": 1.2, "rpg": -0.1},
            "mechanics": {
                "core_loop": "",
                "progression_systems": [],
                "failure_states": [],
                "simulation_depth_notes": ""
            },
            "narrative": {
                "premise": "",
                "player_fantasy": "",
                "tone": "",
                "world_notes": "",
                "quest_structure": []
            },
            "style": {
                "preset": "",
                "art_direction": "",
                "camera_direction": "",
                "ui_direction": "",
                "audio_direction": ""
            },
            "constraints": {
                "target_platforms": ["macos"],
                "content_rating_target": "",
                "scope_constraints": [],
                "technical_constraints": [],
                "accessibility_constraints": []
            },
            "uncertainty_decisions": [
                {
                    "topic": "genre",
                    "source_input": "idk",
                    "options": [
                        {"option_id": "a", "title": "A", "summary": "a", "tradeoff": "a"},
                        {"option_id": "b", "title": "B", "summary": "b", "tradeoff": "b"}
                    ],
                    "selected_option_id": "z"
                }
            ],
            "unexpected": True,
        }

        with tempfile.TemporaryDirectory() as temp_dir:
            payload_path = Path(temp_dir) / "bad-session.json"
            payload_path.write_text(json.dumps(malformed_payload), encoding="utf-8")

            proc = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(EDITOR_CSPROJ),
                "--",
                "--interview-validate-file",
                str(payload_path),
            ])

            self.assertEqual(proc.returncode, 3, proc.stdout + proc.stderr)
            self.assertIn("Interview payload validation failed", proc.stdout)

    def test_dotnet_uncertainty_selection_persists_and_influences_next_question_if_available(self):
        if not shutil.which("dotnet"):
            self.skipTest("dotnet SDK not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            session_path = Path(temp_dir) / "session.json"
            proc = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(EDITOR_CSPROJ),
                "--",
                "--interview-uncertainty",
                str(session_path),
                "genre",
                "I don't know",
                "1",
            ])

            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            self.assertIn("Selected option persisted:", proc.stdout)
            self.assertIn("Next question:", proc.stdout)
            self.assertIn("You selected", proc.stdout)

            session_payload = json.loads(session_path.read_text(encoding="utf-8"))
            self.assertEqual(len(session_payload["uncertainty_decisions"]), 1)
            decision = session_payload["uncertainty_decisions"][0]
            self.assertEqual(decision["source_input"], "I don't know")
            self.assertEqual(len(decision["options"]), 3)
            selected = decision["selected_option_id"]
            option_ids = {opt["option_id"] for opt in decision["options"]}
            self.assertIn(selected, option_ids)

    def test_csharp_uncertainty_tests_if_dotnet_available(self):
        if not shutil.which("dotnet"):
            self.skipTest("dotnet SDK not available")

        proc = run_cmd([
            "dotnet",
            "test",
            str(EDITOR_TEST_CSPROJ),
            "-v",
            "minimal",
        ])

        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)

    def test_python_orchestrator_supports_uncertainty_cli(self):
        proc = run_cmd([
            sys.executable,
            "ai-orchestration/python/orchestrator.py",
            "--suggest-uncertain",
            "unsure",
            "--topic",
            "genre",
        ])

        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        payload = json.loads(proc.stdout)
        self.assertTrue(payload["ambiguous"])
        self.assertEqual(len(payload["options"]), 3)

    def test_python_orchestrator_supports_think_for_me_cli(self):
        proc = run_cmd([
            sys.executable,
            "ai-orchestration/python/orchestrator.py",
            "--think-for-me",
            "think of something",
            "--topic",
            "concept",
        ])

        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        payload = json.loads(proc.stdout)
        self.assertTrue(payload["triggered"])
        self.assertTrue(payload["confirmation_required"])
        self.assertEqual(len(payload["proposals"]), 3)
        self.assertTrue(payload["human_summary_markdown"].strip())

    def test_dotnet_think_for_me_requires_confirmation_to_commit_if_available(self):
        if not shutil.which("dotnet"):
            self.skipTest("dotnet SDK not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            session_path = Path(temp_dir) / "session.json"
            unconfirmed = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(EDITOR_CSPROJ),
                "--",
                "--interview-think-for-me",
                str(session_path),
                "concept",
                "think of something",
                "1",
            ])

            self.assertEqual(unconfirmed.returncode, 0, unconfirmed.stdout + unconfirmed.stderr)
            self.assertIn("Pending direction (not committed)", unconfirmed.stdout)
            self.assertFalse(session_path.exists())

            confirmed = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(EDITOR_CSPROJ),
                "--",
                "--interview-think-for-me",
                str(session_path),
                "concept",
                "think of something",
                "1",
                "--confirm",
            ])

            self.assertEqual(confirmed.returncode, 0, confirmed.stdout + confirmed.stderr)
            self.assertIn("Direction committed after explicit confirmation", confirmed.stdout)
            self.assertTrue(session_path.exists())


if __name__ == "__main__":
    unittest.main()
