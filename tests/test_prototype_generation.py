import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"
SAMPLE_BRIEF = REPO_ROOT / "app" / "samples" / "interview-brief.sample.json"
SAMPLE_PROJECT = REPO_ROOT / "app" / "samples" / "generated-prototype" / "cozy-colony-tales"
SMOKE_SCRIPT = REPO_ROOT / "scripts" / "smoke_prototype_launch.py"


def run_cmd(cmd, cwd=REPO_ROOT):
    return subprocess.run(cmd, cwd=cwd, text=True, capture_output=True)


class TestPrototypeGeneration(unittest.TestCase):
    def test_sample_generated_artifacts_exist(self):
        expected = [
            SAMPLE_PROJECT / "prototype-manifest.json",
            SAMPLE_PROJECT / "scene" / "scene_scaffold.json",
            SAMPLE_PROJECT / "scripts" / "player_controller.json",
            SAMPLE_PROJECT / "ui" / "hud_layout.json",
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


if __name__ == "__main__":
    unittest.main()
