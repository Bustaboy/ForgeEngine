#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${FORGEENGINE_VERSION:-0.1.0}"
RID="linux-x64"
OUTPUT_ROOT="$REPO_ROOT/build/release/$RID"
PUBLISH_DIR="$OUTPUT_ROOT/publish"
RUNTIME_BIN_DIR="$OUTPUT_ROOT/runtime"
RUNTIME_BIN="$RUNTIME_BIN_DIR/forgeengine_runtime"
PACKAGE_ROOT="$OUTPUT_ROOT/package"
APP_DIR="$PACKAGE_ROOT/ForgeEngine"
DEB_ROOT="$PACKAGE_ROOT/deb"
APPIMAGE_ROOT="$PACKAGE_ROOT/appimage"
SAMPLE_BRIEF="$REPO_ROOT/app/samples/interview-brief.sample.json"
PLAYTEST_SCENARIO="$REPO_ROOT/app/samples/generated-prototype/cozy-colony-tales/testing/bot-baseline-scenario.v1.json"
PYTHONPATH_ROOT="$REPO_ROOT/ai-orchestration/python"

export PYTHONPATH="$PYTHONPATH_ROOT${PYTHONPATH:+:$PYTHONPATH}"

mkdir -p "$PUBLISH_DIR" "$RUNTIME_BIN_DIR" "$PACKAGE_ROOT"

echo "== ForgeEngine Ubuntu packaging =="
echo "Version: $VERSION"

echo "[1/8] Building C++ runtime"
g++ -std=c++17 "$REPO_ROOT/runtime/cpp/main.cpp" -o "$RUNTIME_BIN"

echo "[2/8] Publishing .NET editor"
dotnet publish "$REPO_ROOT/editor/csharp/GameForge.Editor.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR"

echo "[3/8] Staging app payload"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR"
cp -R "$PUBLISH_DIR"/* "$APP_DIR/"
cp "$RUNTIME_BIN" "$APP_DIR/forgeengine_runtime"
cp -R "$REPO_ROOT/ai-orchestration" "$APP_DIR/ai-orchestration"
cp -R "$REPO_ROOT/app" "$APP_DIR/app"


echo "[4/8] Running post-build validation"
python3 "$REPO_ROOT/ai-orchestration/python/orchestrator.py" --prepare-models
python3 "$REPO_ROOT/ai-orchestration/python/orchestrator.py" --benchmark
python3 "$REPO_ROOT/ai-orchestration/python/orchestrator.py" \
  --run-generation-pipeline \
  --generate-prototype "$SAMPLE_BRIEF" \
  --output "$REPO_ROOT/build/generated-prototypes" \
  --bot-playtest-scenario "$PLAYTEST_SCENARIO"
python3 "$REPO_ROOT/scripts/run_smoke_and_capture_evidence.py" \
  --os ubuntu \
  --output-root "$REPO_ROOT/build/release-evidence"

echo "[5/8] Building DEB"
rm -rf "$DEB_ROOT"
mkdir -p "$DEB_ROOT/DEBIAN" "$DEB_ROOT/opt/forgeengine" "$DEB_ROOT/usr/share/applications"
cp -R "$APP_DIR"/* "$DEB_ROOT/opt/forgeengine/"
cat > "$DEB_ROOT/DEBIAN/control" <<CONTROL
Package: forgeengine
Version: $VERSION
Section: games
Priority: optional
Architecture: amd64
Maintainer: ForgeEngine Team <release@forgeengine.local>
Description: ForgeEngine local-first editor + runtime
Depends: libc6 (>= 2.35), libstdc++6
CONTROL
cat > "$DEB_ROOT/usr/share/applications/forgeengine.desktop" <<DESKTOP
[Desktop Entry]
Version=1.0
Name=ForgeEngine
Exec=/opt/forgeengine/GameForge.Editor
Terminal=false
Type=Application
Categories=Game;Development;
DESKTOP
DEB_PATH="$OUTPUT_ROOT/ForgeEngine-${VERSION}-linux-amd64.deb"
dpkg-deb --build "$DEB_ROOT" "$DEB_PATH"

echo "[6/8] Building AppImage"
rm -rf "$APPIMAGE_ROOT"
APPDIR="$APPIMAGE_ROOT/ForgeEngine.AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications"
cp -R "$APP_DIR"/* "$APPDIR/usr/bin/"
cp "$DEB_ROOT/usr/share/applications/forgeengine.desktop" "$APPDIR/forgeengine.desktop"
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/GameForge.Editor" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"
if ! command -v appimagetool >/dev/null 2>&1; then
  echo "Missing appimagetool. Install it (or set APPIMAGETOOL) before running packaging." >&2
  exit 1
fi
APPIMAGE_PATH="$OUTPUT_ROOT/ForgeEngine-${VERSION}-linux-x86_64.AppImage"
appimagetool "$APPDIR" "$APPIMAGE_PATH"

echo "[7/8] Manifest"
cat > "$OUTPUT_ROOT/release_manifest.json" <<MANIFEST
{
  "version": "$VERSION",
  "rid": "$RID",
  "deb": "$(basename "$DEB_PATH")",
  "appimage": "$(basename "$APPIMAGE_PATH")",
  "runtime_binary": "forgeengine_runtime",
  "post_build_validation": [
    "orchestrator.py --prepare-models",
    "orchestrator.py --benchmark",
    "orchestrator.py --run-generation-pipeline --generate-prototype app/samples/interview-brief.sample.json",
    "run_smoke_and_capture_evidence.py --os ubuntu"
  ]
}
MANIFEST

echo "[8/8] Done"
echo "Artifacts in $OUTPUT_ROOT"
