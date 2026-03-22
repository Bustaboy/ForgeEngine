import json
import sys
import tempfile
import importlib.util
from pathlib import Path
import unittest


REPO_ROOT = Path(__file__).resolve().parents[1]
ORCHESTRATOR = REPO_ROOT / "ai-orchestration" / "python" / "orchestrator.py"

spec = importlib.util.spec_from_file_location("orchestrator", ORCHESTRATOR)
orchestrator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = orchestrator
spec.loader.exec_module(orchestrator)


class TestAssetImportPipeline(unittest.TestCase):
    def test_imported_assets_are_auto_tagged_and_searchable(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            source_png = Path(temp_dir) / "hero_portrait.png"
            source_png.write_bytes(b"fake-png-content")

            result = orchestrator.import_assets(
                project_root,
                [
                    orchestrator.AssetImportRequest(
                        source_path=str(source_png),
                        source_type="manual-upload",
                        license_id="CC0-1.0",
                        user_tags=["Hero", "portrait"],
                        rights_confirmation=False,
                    )
                ],
            )

            self.assertEqual([], result.errors)
            self.assertEqual(1, len(result.imported_assets))
            entry = result.imported_assets[0]
            self.assertEqual("textures", entry.category)
            self.assertIn("manual-upload", entry.tags)
            self.assertIn("hero", entry.tags)
            self.assertTrue((project_root / entry.relative_path).exists())

            search_results = orchestrator.search_asset_catalog(
                project_root=project_root,
                query="hero",
                category="textures",
                required_tags=["manual-upload"],
            )
            self.assertEqual(1, len(search_results))
            self.assertEqual(entry.asset_id, search_results[0]["asset_id"])

    def test_blocked_or_unclear_license_returns_actionable_errors(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            source_wav = Path(temp_dir) / "impact.wav"
            source_wav.write_bytes(b"fake-wav")

            result = orchestrator.import_assets(
                project_root,
                [
                    orchestrator.AssetImportRequest(
                        source_path=str(source_wav),
                        source_type="ai-generated",
                        license_id="CC-BY-SA",
                    ),
                    orchestrator.AssetImportRequest(
                        source_path=str(source_wav),
                        source_type="manual-upload",
                        license_id="unknown-license",
                    ),
                ],
            )

            self.assertEqual(0, len(result.imported_assets))
            self.assertEqual(2, len(result.errors))
            self.assertEqual("blocked-license", result.errors[0].code)
            self.assertIn("blocked in V1", result.errors[0].message)
            self.assertEqual("unclear-license", result.errors[1].code)
            self.assertIn("unsupported or unclear", result.errors[1].message)

            catalog = json.loads((project_root / "assets" / "catalog.v1.json").read_text(encoding="utf-8"))
            self.assertEqual([], catalog["assets"])

    def test_missing_file_and_rights_confirmation_errors_are_explicit(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            source_ogg = Path(temp_dir) / "music.ogg"
            source_ogg.write_bytes(b"fake-ogg")

            result = orchestrator.import_assets(
                project_root,
                [
                    orchestrator.AssetImportRequest(
                        source_path=str(Path(temp_dir) / "missing.glb"),
                        source_type="manual-upload",
                        license_id="cc0-1.0",
                    ),
                    orchestrator.AssetImportRequest(
                        source_path=str(source_ogg),
                        source_type="manual-upload",
                        license_id="user-owned",
                        rights_confirmation=False,
                    ),
                ],
            )

            self.assertEqual(2, len(result.errors))
            self.assertEqual("missing-source-file", result.errors[0].code)
            self.assertEqual("rights-confirmation-required", result.errors[1].code)
            self.assertIn("rights_confirmation=true", result.errors[1].remediation)

    def test_non_contiguous_existing_ids_generate_next_highest_suffix(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            catalog_path = project_root / "assets" / "catalog.v1.json"
            project_root.mkdir(parents=True, exist_ok=True)
            catalog_path.parent.mkdir(parents=True, exist_ok=True)
            catalog_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.asset_catalog.v1",
                        "generated_at_utc": "2026-03-22T00:00:00Z",
                        "assets": [
                            {"asset_id": "asset-0001", "relative_path": "assets/library/asset-0001.png"},
                            {"asset_id": "asset-0003", "relative_path": "assets/library/asset-0003.png"},
                            {"asset_id": "legacy-name", "relative_path": "assets/library/legacy-name.png"},
                        ],
                    }
                ),
                encoding="utf-8",
            )

            source_png = Path(temp_dir) / "new_sprite.png"
            source_png.write_bytes(b"sprite")

            result = orchestrator.import_assets(
                project_root,
                [
                    orchestrator.AssetImportRequest(
                        source_path=str(source_png),
                        source_type="manual-upload",
                        license_id="cc0-1.0",
                    )
                ],
            )

            self.assertEqual([], result.errors)
            self.assertEqual("asset-0004", result.imported_assets[0].asset_id)
            self.assertTrue((project_root / "assets" / "library" / "asset-0004.png").exists())


class TestProjectStyleSystem(unittest.TestCase):
    def test_can_create_select_and_list_user_preset(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)

            created = orchestrator.create_user_style_preset(
                project_root=project_root,
                display_name="Frosted Storybook",
                base_preset_id="cozy-stylized",
                overrides={"textures": {"saturation": 0.88}, "ui": {"accent_intensity": 0.6}},
            )
            self.assertEqual("frosted-storybook", created.preset_id)
            self.assertEqual("cozy-stylized", created.parent_preset_id)
            self.assertEqual(0.88, created.transformations["textures"]["saturation"])

            state = orchestrator.select_project_style_preset(project_root, "frosted-storybook")
            self.assertEqual("frosted-storybook", state.active_preset_id)
            self.assertEqual("match-project-style", state.helper_mode)

            presets = orchestrator.list_style_presets(project_root)
            self.assertTrue(any(item.preset_id == "frosted-storybook" and item.source == "user" for item in presets))

    def test_create_user_style_preset_rejects_blank_display_name(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)

            with self.assertRaisesRegex(ValueError, "cannot be blank"):
                orchestrator.create_user_style_preset(
                    project_root=project_root,
                    display_name="   ",
                    base_preset_id="cozy-stylized",
                    overrides=None,
                )

    def test_match_project_style_applies_consistent_transformations(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            orchestrator.select_project_style_preset(project_root, "cozy-stylized")

            samples = [
                {"asset_id": "asset-0001", "category": "textures", "metadata": {"source": "test"}},
                {"asset_id": "asset-0002", "category": "ui", "metadata": {"source": "test"}},
            ]
            transformed = orchestrator.match_project_style(project_root, samples)

            self.assertEqual(2, len(transformed))
            self.assertEqual("cozy-stylized", transformed[0]["style_preset_id"])
            self.assertEqual("match-project-style", transformed[0]["style_helper_action"])
            self.assertIn("style_transform", transformed[0]["metadata"])
            self.assertEqual(1.15, transformed[0]["metadata"]["style_transform"]["saturation"])
            self.assertEqual("medium", transformed[1]["metadata"]["style_transform"]["font_weight"])


if __name__ == "__main__":
    unittest.main()
