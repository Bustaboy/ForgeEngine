import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import importlib.util
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"
SAMPLE_BRIEF = REPO_ROOT / "app" / "samples" / "interview-brief.sample.json"
SAMPLE_PROJECT = REPO_ROOT / "app" / "samples" / "generated-prototype" / "cozy-colony-tales"
SMOKE_SCRIPT = REPO_ROOT / "scripts" / "smoke_prototype_launch.py"

spec = importlib.util.spec_from_file_location("orchestrator", ORCHESTRATOR)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
spec.loader.exec_module(orchestrator)


def run_cmd(cmd, cwd=REPO_ROOT):
    return subprocess.run(cmd, cwd=cwd, text=True, capture_output=True)


class TestPrototypeGeneration(unittest.TestCase):
    def test_sample_generated_artifacts_exist(self):
        expected = [
            SAMPLE_PROJECT / "prototype-manifest.json",
            SAMPLE_PROJECT / "scene" / "scene_scaffold.json",
            SAMPLE_PROJECT / "scene" / "rts_sim_scenario_map.json",
            SAMPLE_PROJECT / "scripts" / "player_controller.json",
            SAMPLE_PROJECT / "systems" / "rts_sim" / "template_module.json",
            SAMPLE_PROJECT / "ui" / "hud_layout.json",
            SAMPLE_PROJECT / "ui" / "branch_visualization.v1.json",
            SAMPLE_PROJECT / "config" / "rts_sim_balance.v1.json",
            SAMPLE_PROJECT / "save" / "savegame_hook.json",
            SAMPLE_PROJECT / "systems" / "rpg" / "quest_dialogue_framework.v1.json",
            SAMPLE_PROJECT / "systems" / "rpg" / "inventory_leveling.v1.json",
            SAMPLE_PROJECT / "systems" / "rpg" / "consequence_state_tracker.v1.json",
            SAMPLE_PROJECT / "runtime" / "main.cpp",
            SAMPLE_PROJECT / "launch_prototype.sh",
            SAMPLE_PROJECT / "launch_prototype.ps1",
            SAMPLE_PROJECT / "README.md",
        ]

        for file_path in expected:
            self.assertTrue(file_path.exists(), f"Missing generated artifact: {file_path}")

    def test_orchestrator_generates_scaffold_from_brief(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            output_dir = Path(temp_dir) / "out"
            proc = run_cmd([
                sys.executable,
                str(ORCHESTRATOR),
                "--generate-prototype",
                str(SAMPLE_BRIEF),
                "--output",
                str(output_dir),
            ])

            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            self.assertIn("Generated prototype at:", proc.stdout)

            project_root = output_dir / "cozy-colony-tales"
            manifest = json.loads((project_root / "prototype-manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest["project_name"], "Cozy Colony Tales")
            self.assertEqual(manifest["platforms"], ["windows", "ubuntu"])
            self.assertEqual(manifest["rendering"], "vulkan-first")

            scene_payload = json.loads((project_root / "scene" / "scene_scaffold.json").read_text(encoding="utf-8"))
            self.assertEqual(scene_payload["scene_id"], "baseline_scene")

            rts_template = json.loads(
                (project_root / "systems" / "rts_sim" / "template_module.json").read_text(encoding="utf-8")
            )
            self.assertTrue(rts_template["single_player_only"])
            self.assertIn("units_agents", rts_template["systems"])

            scenario = json.loads((project_root / "scene" / "rts_sim_scenario_map.json").read_text(encoding="utf-8"))
            self.assertEqual(scenario["map_id"], "green-valley-outpost")

            balance = json.loads((project_root / "config" / "rts_sim_balance.v1.json").read_text(encoding="utf-8"))
            self.assertEqual(balance["difficulty"], "medium")

            quest_dialogue = json.loads(
                (project_root / "systems" / "rpg" / "quest_dialogue_framework.v1.json").read_text(encoding="utf-8")
            )
            self.assertEqual(quest_dialogue["module_id"], "rpg_baseline_quests")
            self.assertTrue(quest_dialogue["single_player_only"])

            inventory_leveling = json.loads(
                (project_root / "systems" / "rpg" / "inventory_leveling.v1.json").read_text(encoding="utf-8")
            )
            self.assertEqual(inventory_leveling["leveling"]["starting_level"], 1)

            tracker = json.loads(
                (project_root / "systems" / "rpg" / "consequence_state_tracker.v1.json").read_text(encoding="utf-8")
            )
            self.assertEqual(tracker["state"]["current_node_id"], "dialogue_mayor_intro")

    def test_orchestrator_escapes_brief_strings_in_generated_cpp(self):
        tricky_brief = {
            "concept": "My \"Quoted\" \\\\ Game",
            "mechanics": {"core_loop": "Line1\\nLine2 with \\\"quote\\\" and \\\\ slash"},
            "style": {},
            "narrative": {},
        }

        with tempfile.TemporaryDirectory() as temp_dir:
            brief_path = Path(temp_dir) / "brief.json"
            brief_path.write_text(json.dumps(tricky_brief), encoding="utf-8")
            output_dir = Path(temp_dir) / "out"

            proc = run_cmd([
                sys.executable,
                str(ORCHESTRATOR),
                "--generate-prototype",
                str(brief_path),
                "--output",
                str(output_dir),
            ])
            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)

            runtime_main = (output_dir / "my-quoted-game" / "runtime" / "main.cpp").read_text(encoding="utf-8")
            self.assertIn('Project: My \\"Quoted\\"', runtime_main)
            self.assertIn("\\\\\\\\ Game", runtime_main)
            self.assertIn("Core loop seed: Line1\\\\nLine2 with", runtime_main)

            if shutil.which("g++"):
                compile_proc = run_cmd([
                    "g++",
                    "-std=c++17",
                    "runtime/main.cpp",
                    "-o",
                    "runtime/prototype_runtime",
                ], cwd=output_dir / "my-quoted-game")
                self.assertEqual(compile_proc.returncode, 0, compile_proc.stdout + compile_proc.stderr)

    def test_orchestrator_accepts_bom_prefixed_brief_json(self):
        bom_brief = {
            "concept": "BOM Village",
            "mechanics": {"core_loop": "Gather -> Build -> Progress"},
            "style": {"ui_direction": "Minimal readable HUD"},
            "narrative": {"world_notes": "BOM compatibility check."},
        }

        temp_root = REPO_ROOT / "build" / "test-temp"
        run_root = temp_root / f"bom-brief-{os.getpid()}-{next(tempfile._get_candidate_names())}"
        shutil.rmtree(run_root, ignore_errors=True)
        run_root.mkdir(parents=True, exist_ok=True)

        try:
            brief_path = run_root / "brief.json"
            brief_path.write_text(json.dumps(bom_brief), encoding="utf-8-sig")
            output_dir = run_root / "out"

            proc = run_cmd([
                sys.executable,
                str(ORCHESTRATOR),
                "--run-generation-pipeline",
                "--generate-prototype",
                str(brief_path),
                "--output",
                str(output_dir),
                "--no-launch-runtime",
            ])

            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            payload = json.loads(proc.stdout)
            self.assertEqual(payload["status"], "passed")
        finally:
            shutil.rmtree(run_root, ignore_errors=True)

    def test_orchestrator_launch_without_gpp_returns_controlled_error(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            output_dir = Path(temp_dir) / "out"
            proc_gen = run_cmd([
                sys.executable,
                str(ORCHESTRATOR),
                "--generate-prototype",
                str(SAMPLE_BRIEF),
                "--output",
                str(output_dir),
            ])
            self.assertEqual(proc_gen.returncode, 0, proc_gen.stdout + proc_gen.stderr)

            env = dict(os.environ)
            env["PATH"] = ""
            proc_launch = subprocess.run(
                [
                    sys.executable,
                    str(ORCHESTRATOR),
                    "--generate-prototype",
                    str(SAMPLE_BRIEF),
                    "--output",
                    str(output_dir),
                    "--launch",
                ],
                cwd=REPO_ROOT,
                text=True,
                capture_output=True,
                env=env,
            )
            self.assertEqual(proc_launch.returncode, 127, proc_launch.stdout + proc_launch.stderr)
            self.assertIn("ERROR: g++ not found.", proc_launch.stdout)

    def test_smoke_script_generation_and_launch(self):
        if not shutil.which("g++"):
            self.skipTest("g++ not available")

        proc = run_cmd([sys.executable, str(SMOKE_SCRIPT)])
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("Prototype launch success.", proc.stdout)
        self.assertIn("Prototype generation + launch smoke test passed.", proc.stdout)

    def test_partial_regeneration_skips_locked_paths_without_confirmation(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir) / "prototype"
            (root / "scene").mkdir(parents=True, exist_ok=True)
            (root / "ui").mkdir(parents=True, exist_ok=True)
            (root / "scene" / "scene_scaffold.json").write_text('{"version": 1}', encoding="utf-8")
            (root / "ui" / "hud_layout.json").write_text('{"version": 1}', encoding="utf-8")

            result = orchestrator.apply_partial_regeneration(
                prototype_root=root,
                updates={
                    "scene/scene_scaffold.json": '{"version": 2}',
                    "ui/hud_layout.json": '{"version": 2}',
                },
                locked_paths=["scene"],
                confirm_destructive=False,
            )

            self.assertTrue(result.requires_confirmation)
            self.assertIn("scene/scene_scaffold.json", result.skipped_locked_files)
            self.assertIn("scene/scene_scaffold.json", result.conflict_prompt)
            self.assertEqual(
                (root / "scene" / "scene_scaffold.json").read_text(encoding="utf-8"),
                '{"version": 1}',
            )
            self.assertEqual(
                (root / "ui" / "hud_layout.json").read_text(encoding="utf-8"),
                '{"version": 2}',
            )

    def test_partial_regeneration_overwrites_locked_paths_after_confirmation(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir) / "prototype"
            (root / "scene").mkdir(parents=True, exist_ok=True)
            (root / "scene" / "scene_scaffold.json").write_text('{"version": 1}', encoding="utf-8")

            result = orchestrator.apply_partial_regeneration(
                prototype_root=root,
                updates={"scene/scene_scaffold.json": '{"version": 2}'},
                locked_paths=["scene"],
                confirm_destructive=True,
            )

            self.assertFalse(result.requires_confirmation)
            self.assertEqual(result.conflicts, [])
            self.assertEqual(result.skipped_locked_files, [])
            self.assertEqual(result.updated_files, ["scene/scene_scaffold.json"])
            self.assertEqual(
                (root / "scene" / "scene_scaffold.json").read_text(encoding="utf-8"),
                '{"version": 2}',
            )

    def test_partial_regeneration_rejects_traversal_outside_prototype_root(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            root = temp_root / "prototype"
            root.mkdir(parents=True, exist_ok=True)
            escaped_target = temp_root / "escaped.json"

            with self.assertRaisesRegex(ValueError, "escapes prototype root"):
                orchestrator.apply_partial_regeneration(
                    prototype_root=root,
                    updates={"../escaped.json": '{"bad": true}'},
                    locked_paths=[],
                    confirm_destructive=False,
                )

            self.assertFalse(escaped_target.exists())

    def test_partial_regeneration_rejects_absolute_targets(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir) / "prototype"
            root.mkdir(parents=True, exist_ok=True)

            with self.assertRaisesRegex(ValueError, "Absolute regeneration path is not allowed"):
                orchestrator.apply_partial_regeneration(
                    prototype_root=root,
                    updates={"/tmp/should-not-write.json": '{"bad": true}'},
                    locked_paths=[],
                    confirm_destructive=False,
                )

    def test_consequence_choice_updates_npc_world_and_branch_state(self):
        tracker_path = SAMPLE_PROJECT / "systems" / "rpg" / "consequence_state_tracker.v1.json"
        branch_path = SAMPLE_PROJECT / "ui" / "branch_visualization.v1.json"
        tracker = json.loads(tracker_path.read_text(encoding="utf-8"))
        branch = json.loads(branch_path.read_text(encoding="utf-8"))

        result = orchestrator.apply_consequence_choice(tracker, "support_farmers")
        self.assertTrue(result.applied)
        self.assertEqual(result.previous_node_id, "dialogue_mayor_intro")
        self.assertEqual(result.current_node_id, "dialogue_farmers_supported")
        self.assertEqual(result.npc_state["farmer_lia_affinity"], 2)
        self.assertEqual(result.world_state["grain_policy"], "farmer-priority")

        resolved = orchestrator.derive_branch_view(branch, tracker)
        statuses = {edge["edge_id"]: edge["live_status"] for edge in resolved["edges"]}
        self.assertEqual(resolved["live_state"]["current_node_id"], "dialogue_farmers_supported")
        self.assertEqual(statuses["edge_farmers"], "inactive")
        self.assertEqual(statuses["edge_merchants"], "inactive")

    def test_branch_view_shows_active_choices_before_decision(self):
        tracker_path = SAMPLE_PROJECT / "systems" / "rpg" / "consequence_state_tracker.v1.json"
        branch_path = SAMPLE_PROJECT / "ui" / "branch_visualization.v1.json"
        tracker = json.loads(tracker_path.read_text(encoding="utf-8"))
        branch = json.loads(branch_path.read_text(encoding="utf-8"))

        resolved = orchestrator.derive_branch_view(branch, tracker)
        statuses = {edge["edge_id"]: edge["live_status"] for edge in resolved["edges"]}
        self.assertEqual(resolved["live_state"]["current_node_id"], "dialogue_mayor_intro")
        self.assertEqual(statuses["edge_farmers"], "active-choice")
        self.assertEqual(statuses["edge_merchants"], "active-choice")

    def test_branch_view_requires_matching_choice_id_for_active_edge(self):
        tracker_path = SAMPLE_PROJECT / "systems" / "rpg" / "consequence_state_tracker.v1.json"
        tracker = json.loads(tracker_path.read_text(encoding="utf-8"))
        branch = {
            "schema": "gameforge.rpg.branch_view.v1",
            "view_id": "choice-id-regression",
            "nodes": [],
            "edges": [
                {
                    "edge_id": "edge_valid",
                    "from": "dialogue_mayor_intro",
                    "to": "dialogue_farmers_supported",
                    "choice_id": "support_farmers",
                },
                {
                    "edge_id": "edge_stale_same_target",
                    "from": "dialogue_mayor_intro",
                    "to": "dialogue_farmers_supported",
                    "choice_id": "support_farmers_old",
                },
            ],
        }

        resolved = orchestrator.derive_branch_view(branch, tracker)
        statuses = {edge["edge_id"]: edge["live_status"] for edge in resolved["edges"]}
        self.assertEqual(statuses["edge_valid"], "active-choice")
        self.assertEqual(statuses["edge_stale_same_target"], "inactive")


if __name__ == "__main__":
    unittest.main()
