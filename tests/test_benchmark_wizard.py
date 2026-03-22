import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
WIZARD_PATH = REPO_ROOT / "ai-orchestration" / "python" / "benchmark" / "wizard.py"

spec = importlib.util.spec_from_file_location("benchmark_wizard", WIZARD_PATH)
benchmark_wizard = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = benchmark_wizard
assert spec.loader is not None
spec.loader.exec_module(benchmark_wizard)


class BenchmarkWizardTests(unittest.TestCase):
    def test_first_run_invokes_prepare_models_and_persists_state(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            anchor = Path(temp_dir) / "orchestrator.py"
            anchor.write_text("# test", encoding="utf-8")

            fake_gpu = benchmark_wizard.HardwareSummary(gpu_name="RTX 2070", gpu_vram_gb=8, cpu_cores=8)
            with (
                mock.patch.object(benchmark_wizard, "detect_primary_gpu", return_value=type("Gpu", (), {"name": fake_gpu.gpu_name, "total_vram_gb": fake_gpu.gpu_vram_gb})()),
                mock.patch.object(benchmark_wizard, "prepare_models_as_dict", return_value={"runtime_configs": [{"model_id": "llama-3.1-8b-q4-k-m"}]}) as prepare_mock,
            ):
                result = benchmark_wizard.run_benchmark(orchestrator_file=anchor, auto_prepare_models=True)

            self.assertTrue(result.is_first_run)
            self.assertTrue(result.prepare_models_invoked)
            prepare_mock.assert_called_once()
            self.assertTrue(Path(result.state_path).exists())

    def test_second_run_skips_prepare_models(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            anchor = Path(temp_dir) / "orchestrator.py"
            anchor.write_text("# test", encoding="utf-8")

            state_file = Path(temp_dir) / "models" / ".benchmark-first-run.json"
            state_file.parent.mkdir(parents=True, exist_ok=True)
            state_file.write_text("{}", encoding="utf-8")

            with mock.patch.object(benchmark_wizard, "prepare_models_as_dict") as prepare_mock:
                result = benchmark_wizard.run_benchmark(orchestrator_file=anchor, auto_prepare_models=True)

            self.assertFalse(result.is_first_run)
            self.assertFalse(result.prepare_models_invoked)
            prepare_mock.assert_not_called()


if __name__ == "__main__":
    unittest.main()
