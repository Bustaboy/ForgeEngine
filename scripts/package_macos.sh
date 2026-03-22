#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${FORGEENGINE_VERSION:-0.1.0}"
RID="osx-arm64"
OUTPUT_ROOT="$REPO_ROOT/build/release/$RID"
PUBLISH_DIR="$OUTPUT_ROOT/publish"
RUNTIME_BIN_DIR="$OUTPUT_ROOT/runtime"
RUNTIME_BIN="$RUNTIME_BIN_DIR/forgeengine_runtime"
APP_BUNDLE="$OUTPUT_ROOT/ForgeEngine.app"
DMG_PATH="$OUTPUT_ROOT/ForgeEngine-${VERSION}-macos-arm64.dmg"
PYTHONPATH_ROOT="$REPO_ROOT/ai-orchestration/python"

export PYTHONPATH="$PYTHONPATH_ROOT${PYTHONPATH:+:$PYTHONPATH}"

mkdir -p "$PUBLISH_DIR" "$RUNTIME_BIN_DIR"

echo "== ForgeEngine macOS packaging =="
g++ -std=c++17 "$REPO_ROOT/runtime/cpp/main.cpp" -o "$RUNTIME_BIN"

dotnet publish "$REPO_ROOT/editor/csharp/GameForge.Editor.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR"

rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources"
cp "$PUBLISH_DIR/GameForge.Editor" "$APP_BUNDLE/Contents/MacOS/GameForge.Editor"
cp "$RUNTIME_BIN" "$APP_BUNDLE/Contents/MacOS/forgeengine_runtime"
cp -R "$REPO_ROOT/ai-orchestration" "$APP_BUNDLE/Contents/Resources/ai-orchestration"
cp -R "$REPO_ROOT/app" "$APP_BUNDLE/Contents/Resources/app"
cat > "$APP_BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>ForgeEngine</string>
<key>CFBundleExecutable</key><string>GameForge.Editor</string>
<key>CFBundleIdentifier</key><string>com.forgeengine.app</string>
<key>CFBundleVersion</key><string>$VERSION</string>
</dict></plist>
PLIST

hdiutil create -volname "ForgeEngine" -srcfolder "$APP_BUNDLE" -ov -format UDZO "$DMG_PATH"

echo "Created $DMG_PATH"
