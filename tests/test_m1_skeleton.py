import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_SRC = REPO_ROOT / "runtime" / "cpp" / "main.cpp"
RUNTIME_BIN = REPO_ROOT / "build" / "runtime" / "gameforge_runtime"


def run_cmd(cmd, cwd=REPO_ROOT):
    return subprocess.run(cmd, cwd=cwd, text=True, capture_output=True)


class TestMilestone1Skeleton(unittest.TestCase):
    def test_runtime_cpp_compiles_and_runs(self):
        import time

        build_dir = REPO_ROOT / "build" / "runtime-test"
        build_dir.mkdir(parents=True, exist_ok=True)

        configure_proc = run_cmd(["cmake", "-S", str(REPO_ROOT), "-B", str(build_dir)])
        if configure_proc.returncode != 0:
            if "Could NOT find Vulkan" in configure_proc.stderr or "Could NOT find glfw3" in configure_proc.stderr:
                self.skipTest("Vulkan/GLFW dependencies unavailable in test environment")
            self.fail(configure_proc.stdout + configure_proc.stderr)

        build_proc = run_cmd(["cmake", "--build", str(build_dir)])
        self.assertEqual(build_proc.returncode, 0, build_proc.stdout + build_proc.stderr)

        runtime_bin = build_dir / "bin" / "forge_runtime"
        self.assertTrue(runtime_bin.exists(), f"Runtime binary missing at {runtime_bin}")

        process = subprocess.Popen(
            [str(runtime_bin)],
            cwd=REPO_ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )
        time.sleep(2)
        process.terminate()
        stdout, _ = process.communicate(timeout=10)

        self.assertIn("ForgeEngine Vulkan runtime initialized", stdout)
        self.assertIn("Render loop started", stdout)

    def test_bootstrap_sh_runtime_only(self):
        proc = run_cmd(["./scripts/bootstrap.sh", "--runtime-only"])
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("Bootstrap completed successfully (runtime-only).", proc.stdout)

    def test_bootstrap_sh_runtime_only_from_external_cwd(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            proc = run_cmd([str(REPO_ROOT / "scripts" / "bootstrap.sh"), "--runtime-only"], cwd=temp_dir)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("Bootstrap completed successfully (runtime-only).", proc.stdout)

    def test_bootstrap_sh_default_mode_contract(self):
        proc = run_cmd(["./scripts/bootstrap.sh"])
        dotnet_available = shutil.which("dotnet") is not None
        if dotnet_available:
            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            self.assertIn("== Starting C# App Entrypoint ==", proc.stdout)
            self.assertIn("Editor launcher started successfully.", proc.stdout)
        else:
            self.assertEqual(proc.returncode, 2, proc.stdout + proc.stderr)
            self.assertIn("WARNING: dotnet SDK not found", proc.stdout)

    def test_editor_csharp_program_contract(self):
        program_text = (REPO_ROOT / "editor" / "csharp" / "Program.cs").read_text(encoding="utf-8")
        self.assertIn("editor launcher (C# app entrypoint)", program_text)
        self.assertIn("Runtime build detected.", program_text)

        if shutil.which("dotnet"):
            build_dir = REPO_ROOT / "build" / "editor-runtime-test"
            build_dir.mkdir(parents=True, exist_ok=True)

            configure_proc = run_cmd(["cmake", "-S", str(REPO_ROOT), "-B", str(build_dir)])
            if configure_proc.returncode != 0:
                if "Could NOT find Vulkan" in configure_proc.stderr or "Could NOT find glfw3" in configure_proc.stderr:
                    self.skipTest("Vulkan/GLFW dependencies unavailable in test environment")
                self.fail(configure_proc.stdout + configure_proc.stderr)

            build_proc = run_cmd(["cmake", "--build", str(build_dir)])
            self.assertEqual(build_proc.returncode, 0, build_proc.stdout + build_proc.stderr)

            runtime_bin = build_dir / "bin" / "forge_runtime"
            self.assertTrue(runtime_bin.exists(), f"Runtime binary missing at {runtime_bin}")

            run_proc = run_cmd([
                "dotnet",
                "run",
                "--project",
                str(REPO_ROOT / "editor" / "csharp" / "GameForge.Editor.csproj"),
                "--",
                str(runtime_bin),
            ])
            self.assertEqual(run_proc.returncode, 0, run_proc.stdout + run_proc.stderr)
            self.assertIn("Editor launcher started successfully.", run_proc.stdout)

    def test_bootstrap_ps1_contract(self):
        script_text = (REPO_ROOT / "scripts" / "bootstrap.ps1").read_text(encoding="utf-8")
        self.assertIn("[switch]$RuntimeOnly", script_text)
        self.assertIn("$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot \"..\")).Path", script_text)
        self.assertIn("Starting C# App Entrypoint", script_text)
        self.assertIn("WARNING: dotnet SDK not found", script_text)

    def test_no_python_wrapper_dependency_in_bootstrap_scripts(self):
        shell_script = (REPO_ROOT / "scripts" / "bootstrap.sh").read_text(encoding="utf-8")
        ps_script = (REPO_ROOT / "scripts" / "bootstrap.ps1").read_text(encoding="utf-8")
        self.assertNotIn("bootstrap.py", shell_script)
        self.assertNotIn("bootstrap.py", ps_script)


    def test_editor_csproj_contract(self):
        csproj_text = (REPO_ROOT / "editor" / "csharp" / "GameForge.Editor.csproj").read_text(encoding="utf-8")
        self.assertIn("<TargetFramework>net8.0</TargetFramework>", csproj_text)
        self.assertIn("<OutputType>Exe</OutputType>", csproj_text)

    def test_documentation_contracts(self):
        readme = (REPO_ROOT / "README.md").read_text(encoding="utf-8")
        app_readme = (REPO_ROOT / "app" / "README.md").read_text(encoding="utf-8")
        setup = (REPO_ROOT / "docs" / "SETUP.md").read_text(encoding="utf-8")

        self.assertIn("./scripts/bootstrap.sh", readme)
        self.assertIn("pwsh -f scripts/bootstrap.ps1", readme)
        self.assertIn("--runtime-only", readme)

        self.assertIn("App entrypoint (launcher/editor shell):** C#", app_readme)
        self.assertIn("Game runtime entrypoint (generated game runtime):** C++", app_readme)
        self.assertIn("optional for app startup", app_readme)

        self.assertIn("Entrypoint Boundaries (V1)", setup)
        self.assertIn("Main app entrypoint: **C# editor launcher shell**.", setup)
        self.assertIn("Game runtime entrypoint: **C++ runtime**.", setup)
        self.assertIn("Python entrypoint: **optional**", setup)

    def test_ai_orchestration_placeholder_runs(self):
        proc = run_cmd([sys.executable, "ai-orchestration/python/orchestrator.py"])
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("AI orchestration skeleton", proc.stdout)


if __name__ == "__main__":
    unittest.main()
