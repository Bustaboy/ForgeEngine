"""Static cue phrases, policy thresholds, and asset license / extension rules.

These values are shared across CLI handlers, asset import, and interview helpers.
"""

from __future__ import annotations

UNCERTAINTY_CUES = {
    "i don't know",
    "idk",
    "unsure",
    "not sure",
    "you decide",
    "anything",
    "whatever",
    "unknown",
    "maybe",
}

THINK_FOR_ME_CUES = {
    "think for me",
    "think of something",
    "you pick",
    "pick for me",
    "surprise me",
}

MANUAL_FALLBACK_FAILURE_THRESHOLD = 5

ALLOWED_LICENSES = {
    "cc0-1.0",
    "public-domain",
    "cc-by-4.0",
    "user-owned",
}

BLOCKED_LICENSES = {
    "cc-by-sa",
    "cc-by-nc",
}

ATTRIBUTION_REQUIRED_LICENSES = {
    "cc-by-4.0",
}

ASSET_CATEGORY_BY_EXTENSION = {
    ".png": "textures",
    ".jpg": "textures",
    ".jpeg": "textures",
    ".tga": "textures",
    ".bmp": "textures",
    ".gif": "ui",
    ".svg": "ui",
    ".wav": "audio",
    ".mp3": "audio",
    ".ogg": "audio",
    ".flac": "audio",
    ".fbx": "characters",
    ".glb": "props",
    ".gltf": "props",
    ".obj": "props",
    ".blend": "props",
}
