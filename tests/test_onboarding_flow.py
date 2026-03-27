import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
MODEL_MANAGER_PATH = REPO_ROOT / "ai-orchestration" / "python" / "model_manager.py"
ORCHESTRATOR_PATH = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


model_manager = _load_module("model_manager_under_test", MODEL_MANAGER_PATH)
orchestrator = _load_module("orchestrator_under_test", ORCHESTRATOR_PATH)


class OnboardingFlowTests(unittest.TestCase):
    def test_save_models_config_never_persists_hf_token(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            models_json_path = Path(temp_dir) / "models.json"
            model_manager._save_models_config(  # noqa: SLF001 - testing module helper
                {"models": {}, "hf_token": "secret"},
                models_json_path=models_json_path,
            )
            payload = json.loads(models_json_path.read_text(encoding="utf-8"))
            self.assertNotIn("hf_token", payload)

    def test_choose_token_prefers_environment_over_models_config(self):
        with mock.patch.dict("os.environ", {"HF_TOKEN": "env-secret"}, clear=False):
            token, source = model_manager._choose_token(  # noqa: SLF001 - testing module helper
                token=None,
                config={"hf_token": "legacy-secret"},
            )
        self.assertEqual(token, "env-secret")
        self.assertEqual(source, "environment")

    def test_generate_recommendations_returns_expected_roles(self):
        recs = model_manager.generate_recommendations(
            hardware={"gpu_vram_gb": 8},
            answers={
                "game_type": "rpg",
                "npc_importance": "high",
                "code_vs_asset": "balanced",
                "target_profile": "quality",
            },
        )
        self.assertEqual(set(recs.keys()), {"freewill", "coding", "assetgen", "forgeguard"})
        self.assertTrue(recs["forgeguard"]["kept_installed"])

    def test_run_onboarding_persists_state_and_message(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            models_json_path = Path(temp_dir) / "models.json"
            models_json_path.write_text(json.dumps({"models": {}}), encoding="utf-8")

            answers = iter(["hybrid", "high", "code-heavy", "quality"])
            with (
                mock.patch.object(
                    model_manager,
                    "run_benchmark_as_dict",
                    return_value={"hardware": {"gpu_vram_gb": 6, "gpu_name": "TestGPU"}},
                ),
                mock.patch.object(
                    model_manager,
                    "download_model",
                    return_value={"friendly_name": "forgeguard", "path": "fake.gguf"},
                ),
            ):
                result = model_manager.run_onboarding(
                    orchestrator_file=Path(temp_dir) / "orchestrator.py",
                    models_json_path=models_json_path,
                    input_fn=lambda _prompt: next(answers),
                )

            payload = json.loads(models_json_path.read_text(encoding="utf-8"))
            self.assertTrue(payload["onboarding"]["completed"])
            self.assertIn("recommendations", payload["onboarding"])
            self.assertIn("ForgeGuard", payload["onboarding"]["forgeguard_keep_message"])
            self.assertIn("ForgeGuard", result["message"])

    def test_onboarding_command_dispatches(self):
        with mock.patch.object(orchestrator, "run_onboarding", return_value={"status": "ok"}) as mocked:
            rc = orchestrator._try_run_forge_hooks_cli(["/onboarding_run"])
        self.assertEqual(rc, 0)
        mocked.assert_called_once()


if __name__ == "__main__":
    unittest.main()
