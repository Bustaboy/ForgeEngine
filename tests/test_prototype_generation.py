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
            SAMPLE_PROJECT / "config" / "rts_sim_balance.v1.json",
            SAMPLE_PROJECT / "save" / "savegame_hook.json",
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


if __name__ == "__main__":
    unittest.main()
