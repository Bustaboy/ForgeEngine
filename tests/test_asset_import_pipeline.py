import json
import os
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
    def test_generated_asset_starts_in_pending_review_and_can_be_approved(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            previous_cwd = Path.cwd()
            os_environ_backup = dict(os.environ)
            try:
                os.chdir(project_root)
                os.environ["GAMEFORGE_GRAPHICS_BACKEND"] = "debug-local"
                os.environ["GAMEFORGE_GRAPHICS_SEED"] = "12345"
                generated = orchestrator.generate_asset("stylized forge anvil", type="sprite")
                generated_asset = Path(generated.output_path)
                generated_metadata = Path(generated.metadata_path)
                self.assertTrue(generated_asset.exists())
                self.assertTrue(generated_metadata.exists())
                metadata_payload = json.loads(generated_metadata.read_text(encoding="utf-8"))
                self.assertEqual("pending-review", metadata_payload["review_status"])
                self.assertFalse(metadata_payload["approved_for_runtime"])
                self.assertIn("consistency_score", metadata_payload)
                self.assertIn("variants", metadata_payload)
                self.assertGreaterEqual(len(metadata_payload["variants"]), 1)

                reviewed = orchestrator.review_asset(str(generated_asset), decision="approve", reviewer="qa")
                approved_path = Path(reviewed.destination_asset_path)
                approved_metadata_path = Path(reviewed.metadata_path)
                self.assertTrue(approved_path.exists())
                self.assertTrue(approved_metadata_path.exists())
                self.assertFalse(generated_asset.exists())
                approved_payload = json.loads(approved_metadata_path.read_text(encoding="utf-8"))
                self.assertEqual("approved", approved_payload["review_status"])
                self.assertTrue(approved_payload["approved_for_runtime"])
                self.assertTrue(approved_payload["production_ready"])
            finally:
                os.chdir(previous_cwd)
                os.environ.clear()
                os.environ.update(os_environ_backup)

    def test_review_asset_reject_moves_to_rejected_and_marks_not_production_ready(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            generated_root = project_root / "Assets" / "Generated"
            generated_root.mkdir(parents=True, exist_ok=True)
            asset_path = generated_root / "texture-1.svg"
            metadata_path = generated_root / "texture-1.metadata.json"
            asset_path.write_text("<svg/>", encoding="utf-8")
            metadata_path.write_text(json.dumps({"output_path": str(asset_path), "review_status": "pending-review"}), encoding="utf-8")

            result = orchestrator.review_asset(str(asset_path), decision="reject", reviewer="qa")
            rejected_path = Path(result.destination_asset_path)
            rejected_metadata_path = Path(result.metadata_path)
            self.assertTrue(rejected_path.exists())
            self.assertTrue(rejected_metadata_path.exists())
            self.assertFalse(asset_path.exists())
            payload = json.loads(rejected_metadata_path.read_text(encoding="utf-8"))
            self.assertEqual("rejected", payload["review_status"])
            self.assertFalse(payload["production_ready"])
            self.assertFalse(payload["approved_for_runtime"])

    def test_generate_asset_batch_writes_variant_outputs(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            previous_cwd = Path.cwd()
            os_environ_backup = dict(os.environ)
            try:
                os.chdir(project_root)
                os.environ["GAMEFORGE_GRAPHICS_BACKEND"] = "debug-local"
                os.environ["GAMEFORGE_GRAPHICS_SEED"] = "777"
                generated = orchestrator.generate_asset("stylized blacksmith hammer", type="sprite", count=3)
                metadata_payload = json.loads(Path(generated.metadata_path).read_text(encoding="utf-8"))
                self.assertEqual(3, metadata_payload["variant_count"])
                self.assertEqual(3, len(metadata_payload["variants"]))
                for variant in metadata_payload["variants"]:
                    self.assertTrue(Path(variant["output_path"]).exists())
                    self.assertIn("consistency_score", variant)
            finally:
                os.chdir(previous_cwd)
                os.environ.clear()
                os.environ.update(os_environ_backup)

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

    def test_attribution_export_includes_only_cc_by_assets_for_mixed_licenses(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            source_png = Path(temp_dir) / "hero.png"
            source_wav = Path(temp_dir) / "theme.wav"
            source_png.write_bytes(b"png")
            source_wav.write_bytes(b"wav")

            imported = orchestrator.import_assets(
                project_root,
                [
                    orchestrator.AssetImportRequest(
                        source_path=str(source_png),
                        source_type="manual-upload",
                        license_id="cc0-1.0",
                        display_name="Hero Texture",
                    ),
                    orchestrator.AssetImportRequest(
                        source_path=str(source_wav),
                        source_type="manual-upload",
                        license_id="cc-by-4.0",
                        display_name="Theme Track",
                    ),
                ],
            )

            self.assertEqual([], imported.errors)
            catalog_path = project_root / "assets" / "catalog.v1.json"
            catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
            for entry in catalog["assets"]:
                if entry["license_id"] == "cc-by-4.0":
                    entry["metadata"]["source"] = "Example Composer"
                    entry["metadata"]["attribution_text"] = "Theme Track by Example Composer"
                    entry["metadata"]["attribution_url"] = "https://example.com/theme-track"
            catalog_path.write_text(json.dumps(catalog, indent=2), encoding="utf-8")

            exported = orchestrator.export_attribution_bundle(project_root)
            self.assertTrue(exported.generated)
            self.assertEqual(1, exported.required_asset_count)
            self.assertTrue(Path(exported.json_path).exists())
            self.assertTrue(Path(exported.markdown_path).exists())

            bundle = json.loads(Path(exported.json_path).read_text(encoding="utf-8"))
            self.assertEqual("gameforge.attribution_bundle.v1", bundle["schema"])
            self.assertEqual(1, bundle["asset_count"])
            self.assertEqual(1, len(bundle["entries"]))
            self.assertEqual("cc-by-4.0", bundle["entries"][0]["license_id"])
            self.assertEqual("Theme Track", bundle["entries"][0]["display_name"])
            self.assertEqual("assets/library/asset-0002.wav", bundle["entries"][0]["path"])

    def test_attribution_export_fails_when_required_metadata_is_missing(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            catalog_path = project_root / "assets" / "catalog.v1.json"
            catalog_path.parent.mkdir(parents=True, exist_ok=True)
            catalog_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.asset_catalog.v1",
                        "generated_at_utc": "2026-03-22T00:00:00Z",
                        "assets": [
                            {
                                "asset_id": "asset-0001",
                                "display_name": "Ambient Loop",
                                "category": "audio",
                                "tags": ["audio", "manual-upload"],
                                "license_id": "cc-by-4.0",
                                "source_type": "manual-upload",
                                "relative_path": "assets/library/asset-0001.ogg",
                                "imported_at_utc": "2026-03-22T00:00:00Z",
                                "metadata": {"source": "Artist Name"},
                            }
                        ],
                    },
                    indent=2,
                ),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(ValueError, "metadata.attribution_text"):
                orchestrator.export_attribution_bundle(project_root)

    def test_attribution_export_returns_not_generated_when_no_required_assets_exist(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project_root = Path(temp_dir) / "project"
            project_root.mkdir(parents=True, exist_ok=True)
            catalog_path = project_root / "assets" / "catalog.v1.json"
            catalog_path.parent.mkdir(parents=True, exist_ok=True)
            catalog_path.write_text(
                json.dumps(
                    {
                        "schema": "gameforge.asset_catalog.v1",
                        "generated_at_utc": "2026-03-22T00:00:00Z",
                        "assets": [
                            {
                                "asset_id": "asset-0001",
                                "display_name": "Public Domain Texture",
                                "category": "textures",
                                "tags": ["textures", "manual-upload"],
                                "license_id": "cc0-1.0",
                                "source_type": "manual-upload",
                                "relative_path": "assets/library/asset-0001.png",
                                "imported_at_utc": "2026-03-22T00:00:00Z",
                                "metadata": {"source": "Public Domain Library"},
                            }
                        ],
                    },
                    indent=2,
                ),
                encoding="utf-8",
            )

            exported = orchestrator.export_attribution_bundle(project_root)
            self.assertFalse(exported.generated)
            self.assertEqual(0, exported.required_asset_count)
            self.assertIsNone(exported.json_path)
            self.assertIsNone(exported.markdown_path)
            self.assertFalse((project_root / "compliance" / "attribution.bundle.v1.json").exists())


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
