"""Validate archived Vulkan startup evidence for AT-012."""

from __future__ import annotations

import json
import os
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ARCHIVE = REPO_ROOT / "docs" / "release" / "evidence" / "archived" / "vulkan" / "20260518T120000Z"
REQUIRED_SIGNATURE = "Soul Loom Vulkan runtime initialized"


class TestVulkanStartupEvidence(unittest.TestCase):
    def test_canonical_archive_contains_startup_signature(self):
        archive = Path(os.environ.get("SOUL_LOOM_VULKAN_EVIDENCE", DEFAULT_ARCHIVE))
        log_path = archive / "runtime_startup.log"
        env_path = archive / "environment.json"
        self.assertTrue(archive.is_dir(), f"missing archive directory: {archive}")
        self.assertTrue(log_path.is_file(), f"missing log: {log_path}")
        self.assertTrue(env_path.is_file(), f"missing environment.json: {env_path}")

        log_text = log_path.read_text(encoding="utf-8")
        self.assertIn(REQUIRED_SIGNATURE, log_text)

        payload = json.loads(env_path.read_text(encoding="utf-8"))
        for key in ("host_platform", "commit", "captured_at_utc"):
            self.assertIn(key, payload)
            self.assertTrue(str(payload[key]).strip())


if __name__ == "__main__":
    unittest.main()
